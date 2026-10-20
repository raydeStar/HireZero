// Runs an installed headless Chrome against a disposable company-local host. No user browser profile or live provider.
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {spawn} from 'node:child_process';
import assert from 'node:assert/strict';
import {clicker,delay,fetchTracker,freeLocalUrl,pageDiagnosis,pageIdle,waiter} from './browser-smoke-wait.mjs';
const repo=path.resolve(import.meta.dirname,'..');
const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'hirezero-manual-ui-'));
const chrome=process.env.HIREZERO_TEST_CHROME||path.join(process.env.ProgramFiles||'C:/Program Files','Google/Chrome/Application/chrome.exe');
const hostDir=path.join(repo,'src/HireZero.Host');
const url=process.env.HIREZERO_TEST_URL||await freeLocalUrl();
const webRoot=process.env.HIREZERO_TEST_WEB_ROOT||path.join(hostDir,'wwwroot');
const host=spawn('dotnet',[path.join(repo,'tests/HireZero.Company.Tests/bin/Debug/net10.0/HireZero.Company.Tests.dll'),'--measured-browser-fixture',temporary,url,webRoot],{cwd:hostDir,windowsHide:true,env:{...process.env,HIREZERO_LOCAL_OWNER:'1',HIREZERO_OWNER_LOGIN:'owner@example.invalid',HIREZERO_DATA_ROOT:path.join(temporary,'data'),HIREZERO_EXECUTION_HOLD:'0'},stdio:['ignore','pipe','pipe']});
let hostLog='';host.stdout.on('data',b=>hostLog+=b);host.stderr.on('data',b=>hostLog+=b);let browser,ws;
const until=waiter();
try{
  await until(()=>hostLog.includes('Now listening on: '+url),'this test host owns the listener');
  await until(async()=>{const r=await fetch(url+'/health');return r.ok;},'fresh host health');
  const health=await fetch(url+'/health').then(r=>r.json());assert.equal(health.modelExecution,false);
  browser=spawn(chrome,['--headless=new','--disable-gpu','--disable-background-networking','--disable-sync','--disable-default-apps','--no-first-run','--no-default-browser-check','--no-proxy-server','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+path.join(temporary,'browser'),'about:blank'],{windowsHide:true,stdio:'ignore'});
  const port=await until(async()=>{const text=await fs.readFile(path.join(temporary,'browser','DevToolsActivePort'),'utf8');return Number(text.split('\n')[0]);},'isolated browser debugger');
  const targets=await fetch(`http://127.0.0.1:${port}/json/list`).then(r=>r.json());const target=targets.find(t=>t.type==='page');assert.ok(target);
  ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{ws.addEventListener('open',resolve,{once:true});ws.addEventListener('error',reject,{once:true});});
  let sequence=0;const pending=new Map();const errors=[];
  ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);if(!p)return;clearTimeout(p.timer);pending.delete(m.id);if(m.error)p.reject(new Error(m.error.message));else p.resolve(m.result);}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);});
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('Debugger command timed out: '+method));},10000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});
  const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value;};until.diagnose=pageDiagnosis(evaluate);
  await send('Runtime.enable');await send('Page.enable');await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});await send('Page.addScriptToEvaluateOnNewDocument',{source:fetchTracker});await send('Page.navigate',{url:url+'/'});
  await until(()=>evaluate("document.body.innerText.includes('Company conversation')"),'company UI');
  const click=clicker(evaluate,until),idle=pageIdle(evaluate,until);
  const field=async(label,value)=>{await evaluate(`(()=>{const label=[...document.querySelectorAll('.co-main label')].find(l=>l.childNodes[0]?.textContent.trim()===${JSON.stringify(label)});if(!label)throw Error('Field missing: '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`);await delay(70);};
  assert.match(hostLog,/BROWSER FIXTURE: fake provider only/);
  const dashboard=()=>fetch(url+'/api/company').then(r=>r.json());
  const starts=async()=>{const files=await fs.readdir(temporary,{recursive:true});let count=0;for(const file of files.filter(p=>p.endsWith('fixture-native-starts')))count+=(await fs.readFile(path.join(temporary,file),'utf8')).trim().split('\n').filter(Boolean).length;return count;};

  const initial=await dashboard(),marketing=initial.data.employees.find(e=>e.roleId==='marketing'),developer=initial.data.employees.find(e=>e.roleId==='developer');
  const originalThread=initial.data.threads.find(t=>t.employeeId===developer.id).id;
  const openEmployee=async name=>{await evaluate("[...document.querySelectorAll('.co-conversations .co-list')].find(b=>b.textContent.includes("+JSON.stringify(name)+")).click()");await until(()=>evaluate("document.querySelector('.fe-topbar h1')?.textContent==="+JSON.stringify(name)),name+' open');await until(()=>evaluate("!!document.querySelector('textarea[aria-label^=\"Message to\"]')&&!document.querySelector('.fe-thread')?.textContent.includes('Opening this conversation.')"),'conversation loaded');if(name.includes('Developer'))await field('Work type','developer-advice');else await field('Message action','save');};
  const write=async(label,value)=>{await evaluate("(()=>{const el=document.querySelector('textarea[aria-label='+CSS.escape("+JSON.stringify(label)+")+']');if(!el)throw Error('Missing composer');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(el,"+JSON.stringify(value)+");el.dispatchEvent(new Event('input',{bubbles:true}));})()");};
  const composerValue=()=>evaluate("document.querySelector('.fe-composer textarea')?.value");
  const submitTwice=async()=>{await idle();await evaluate("(()=>{const form=document.querySelector('.co-conversation-composer form');form.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));form.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));})()");}
  const company=async()=>{await evaluate("document.querySelector('button[aria-label=\"Chat\"]').click()");await until(()=>evaluate("!!document.querySelector('textarea[aria-label=\"Company message\"]')"),'company composer');};
  const value=(label,text)=>write(label,text);
  // The same synchronous UI event cannot create duplicate owner notes.
  await value('Company message','Owner keeps final authority over employee work.');await submitTwice();
  await until(()=>evaluate("document.querySelector('.fe-thread').textContent.includes('Owner keeps final authority over employee work.')"),'saved company note');
  let feed=await fetch(url+'/api/company/feed?after=0').then(r=>r.json());assert.equal(feed.filter(e=>e.type==='owner.note').length,1);assert.equal((await dashboard()).data.tasks.length,0);
  await value('Company message','Company draft stays here.');await openEmployee(marketing.name);assert.equal(await composerValue(),'');
  await value('Message to '+marketing.name,'Marketing draft stays private.');await openEmployee(developer.name);assert.equal(await composerValue(),'');
  await value('Message to '+developer.name,'Developer draft stays private.');await openEmployee(marketing.name);assert.equal(await composerValue(),'Marketing draft stays private.');
  await company();assert.equal(await composerValue(),'Company draft stays here.');await openEmployee(developer.name);assert.equal(await composerValue(),'Developer draft stays private.');
  // Private context retains version-bound employee editing.
  await evaluate("document.querySelector('.co-private-context').open=true");await field('Name','Browser Developer renamed');await click('Update employee');
  await until(async()=>(await dashboard()).data.employees.find(e=>e.id===developer.id).name==='Browser Developer renamed','employee edited');developer.name='Browser Developer renamed';
  await until(()=>evaluate("!document.querySelector('.co-conversation-composer button[type=submit]').disabled"),'update finished');
  await evaluate("document.querySelector('.co-private-context').open=false");
  await value('Message to '+developer.name,'One saved employee message.');await submitTwice();
  await until(async()=>(await dashboard()).data.tasks.length===1,'single saved task');
  const threadData=()=>fetch(url+'/api/company/employees/'+developer.id+'/threads/'+originalThread).then(r=>r.json());
  assert.equal((await threadData()).messages.filter(m=>m.body==='One saved employee message.').length,1);assert.equal(await starts(),0);
  // The scheduler re-saves a new task (queued to blocked) after the UI first renders it; cancel the version the card shows.
  const saved=await until(async()=>{const t=(await dashboard()).data.tasks[0];return t.state!=='queued'&&t;},'scheduler settled the saved task');
  await until(()=>evaluate("[...document.querySelectorAll('.co-job-card')].some(c=>c.dataset.taskId==="+JSON.stringify(saved.id)+"&&c.dataset.taskState==="+JSON.stringify(saved.state)+"&&[...c.querySelectorAll('button')].some(b=>b.textContent==='Cancel'))"),'inline task controls');await idle();
  await evaluate("(()=>{const b=[...document.querySelectorAll('.co-job-card button')].find(b=>b.textContent==='Cancel');b.click();b.click();})()");
  await until(async()=>(await dashboard()).data.tasks[0].state==='canceled','owner canceled inline');assert.equal(await starts(),0);
  // Delay only the HTTP reply, after the real fixture command has been committed.
  await evaluate("window.__normalFetch=window.fetch.bind(window);window.fetch=async(...args)=>{const body=typeof args[1]?.body==='string'?JSON.parse(args[1].body):{};if(body.action===window.__holdAction){const result=await window.__normalFetch(...args);await new Promise(resolve=>window.__release=resolve);return result;}if(body.action==='message.send'&&body.body==='Refused message keeps its draft.')return new Response(JSON.stringify({error:'Synthetic definitive refusal'}),{status:409,headers:{'Content-Type':'application/json'}});return window.__normalFetch(...args);};");
  await value('Message to '+developer.name,'Delayed sent message.');await evaluate("window.__holdAction='message.send'");await submitTwice();
  await until(()=>evaluate("typeof window.__release==='function'"),'delayed message reply');await openEmployee(marketing.name).catch(()=>{});
  await evaluate("[...document.querySelectorAll('.co-conversations .co-list')].find(b=>b.textContent.includes('Browser Developer renamed')).click()");
  await until(()=>evaluate("document.querySelector('.fe-topbar h1')?.textContent==='Browser Developer renamed'"),'roundtrip back to original conversation');
  await evaluate("window.__holdAction='';window.__release();window.__release=null");
  await until(async()=>(await composerValue())==='','sent draft cleared after navigation remount');assert.equal((await threadData()).messages.filter(m=>m.body==='Delayed sent message.').length,1);
  await until(()=>evaluate("!document.querySelector('.co-conversation-composer button[type=submit]').disabled||document.querySelector('.fe-composer textarea')?.value===''"),'send finished');
  // A newer draft in the remounted scope survives completion of an older send.
  await value('Message to '+developer.name,'Older pending draft.');await evaluate("window.__holdAction='message.send'");await submitTwice();
  await until(()=>evaluate("typeof window.__release==='function'"),'second delayed message');
  await evaluate("[...document.querySelectorAll('.co-conversations .co-list')].find(b=>b.textContent.includes('Browser draft fixture')).click()");
  await evaluate("[...document.querySelectorAll('.co-conversations .co-list')].find(b=>b.textContent.includes('Browser Developer renamed')).click()");
  await until(()=>evaluate("!!document.querySelector('textarea[aria-label=\"Message to Browser Developer renamed\"]')"),'remounted developer composer');
  await value('Message to '+developer.name,'Newer draft must survive.');await evaluate("window.__holdAction='';window.__release();window.__release=null");
  await until(()=>evaluate("!document.querySelector('.fe-composer textarea').disabled"),'second reply settled');assert.equal(await composerValue(),'Newer draft must survive.');
  await value('Message to '+developer.name,'Refused message keeps its draft.');await submitTwice();await until(()=>evaluate("document.body.innerText.includes('Synthetic definitive refusal')"),'definitive refusal visible');
  assert.equal(await composerValue(),'Refused message keeps its draft.');assert.equal((await threadData()).messages.filter(m=>m.body==='Refused message keeps its draft.').length,0);
  await click('Dismiss');
  // A late new-conversation response must not override later owner navigation.
  const threadCount=(await dashboard()).data.threads.length;await evaluate("window.__holdAction='thread.create'");await click('New conversation');
  await until(()=>evaluate("typeof window.__release==='function'"),'new thread reply delayed');await company();
  await evaluate("window.__holdAction='';window.__release();window.__release=null");await until(async()=>(await dashboard()).data.threads.length===threadCount+1,'new thread committed');await delay(300);
  assert.equal(await evaluate("new URLSearchParams(location.search).get('view')"),'company','Late thread creation does not navigate away');
  await openEmployee(developer.name);const nextThread=(await dashboard()).data.threads.filter(t=>t.employeeId===developer.id).at(-1);
  await evaluate("(()=>{const el=document.querySelector('select[aria-label=\"Employee conversation\"]');Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(el,"+JSON.stringify(nextThread.id)+");el.dispatchEvent(new Event('change',{bubbles:true}));})()");
  await until(()=>evaluate("!!document.querySelector('.fe-composer textarea:not(:disabled)')"),'new conversation loaded');
  assert.equal(await composerValue(),'');assert.equal(await evaluate("document.querySelectorAll('.co-job-card').length"),0,'Other thread tasks do not leak');assert.equal(await evaluate("document.querySelector('.fe-thread').textContent.includes('One saved employee message.')"),false);
  await field('Work type','developer-advice');await value('Message to '+developer.name,'New thread history survives reload.');await submitTwice();await until(async()=>(await dashboard()).data.tasks.length===4,'new thread message saved');
  const route=await evaluate('location.search');await send('Page.reload');await until(()=>evaluate("document.querySelector('.fe-thread')?.textContent.includes('New thread history survives reload.')"),'saved history reloaded');assert.equal(await evaluate('location.search'),route);
  assert.equal(await evaluate("document.querySelectorAll('.co-job-card').length"),1);assert.equal(await starts(),0,'All explicit advice assignment, navigation and cancellation made zero provider requests');
  await company();await openEmployee(marketing.name);await evaluate('history.back()');await until(()=>evaluate("new URLSearchParams(location.search).get('view')==='company'"),'browser back');await evaluate('history.forward()');await until(()=>evaluate("new URLSearchParams(location.search).get('employee')==="+JSON.stringify(marketing.id)),'browser forward');
  await company();
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/company-chat-browser.json'),JSON.stringify({provider:'compiled fake only; never called',liveModelCalls:0,fakeNativeStarts:await starts(),scopedDrafts:true,historyReload:true,browserBackForward:true,repeatedSendCreatesOneTask:true,inlineCancel:true,delayedRoundtripClearsSentDraft:true,newerDraftPreserved:true,failedDraftPreserved:true,lateThreadNavigationGuard:true,threadTasksIsolated:true,employeeEditingRetained:true},null,2));
  assert.equal(errors.length,0,'No uncaught UI exceptions');await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  const desktop=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(repo,'test-results/company-chat-desktop.png'),Buffer.from(desktop.data,'base64'));
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await delay(250);assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'Mobile layout does not overflow');
  assert.equal(await evaluate("document.querySelector('.fe-topbar').getBoundingClientRect().bottom<=document.querySelector('.co-conversation-context').getBoundingClientRect().top"),true,'Mobile header and conversation context do not overlap');
  const mobile=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(repo,'test-results/company-chat-mobile.png'),Buffer.from(mobile.data,'base64'));
  console.log('PASS real Chrome company chat: scoped drafts/history, navigation, stale replies, repeated send/cancel, saved messages, retained settings, zero provider calls, desktop/mobile');
  await send('Browser.close').catch(()=>{});
}catch(e){await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/company-chat-browser-host.log'),hostLog);throw e;}
finally{
  ws?.close();browser?.kill();host.kill();await delay(400);
  // Delete only this script's verified mkdtemp root after its own processes have exited.
  if(path.dirname(temporary)===path.resolve(os.tmpdir())&&path.basename(temporary).startsWith('hirezero-manual-ui-'))await fs.rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
