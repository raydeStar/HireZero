// The owner walkthrough's P0 findings (docs/history/2026-10-19-owner-walkthrough.md), checked in headless Chrome against a
// disposable company-local host with the compiled fake provider: customer messages are multi-line and never stored or
// shown raw, a locked authorization says why and unlocks where it is, and the conversation keeps room to read at
// 1024x768. No user browser profile or live provider.
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {spawn} from 'node:child_process';
import assert from 'node:assert/strict';
import {delay,fetchTracker,freeLocalUrl,pageDiagnosis,waiter} from './browser-smoke-wait.mjs';
const repo=path.resolve(import.meta.dirname,'..');
const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'hirezero-manual-ui-'));
const chrome=process.env.HIREZERO_TEST_CHROME||path.join(process.env.ProgramFiles||'C:/Program Files','Google/Chrome/Application/chrome.exe');
const hostDir=path.join(repo,'src/HireZero.Host');
const url=process.env.HIREZERO_TEST_URL||await freeLocalUrl();
const webRoot=process.env.HIREZERO_TEST_WEB_ROOT||path.join(hostDir,'wwwroot');
const host=spawn('dotnet',[path.join(repo,'tests/HireZero.Company.Tests/bin/Debug/net10.0/HireZero.Company.Tests.dll'),'--measured-browser-fixture',temporary,url,webRoot],{cwd:hostDir,windowsHide:true,env:{...process.env,HIREZERO_LOCAL_OWNER:'1',HIREZERO_OWNER_LOGIN:'owner@example.invalid',HIREZERO_DATA_ROOT:path.join(temporary,'data'),HIREZERO_EXECUTION_HOLD:'0'},stdio:['ignore','pipe','pipe']});
let hostLog='';host.stdout.on('data',b=>hostLog+=b);host.stderr.on('data',b=>hostLog+=b);let browser,ws;
const until=waiter();
// Fictional customer text; 4111 1111 1111 1111 is the standard test card number.
const messages='Charged twice, card 4111 1111 1111 1111, jo@example.invalid\n---\nUnder GDPR please delete my data.\n---\nHow do I export to PDF?';
try{
  await until(()=>hostLog.includes('Now listening on: '+url),'this test host owns the listener');
  await until(async()=>(await fetch(url+'/health')).ok,'fresh host health');
  browser=spawn(chrome,['--headless=new','--disable-gpu','--disable-background-networking','--disable-sync','--disable-default-apps','--no-first-run','--no-default-browser-check','--no-proxy-server','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+path.join(temporary,'browser'),'about:blank'],{windowsHide:true,stdio:'ignore'});
  const port=await until(async()=>Number((await fs.readFile(path.join(temporary,'browser','DevToolsActivePort'),'utf8')).split('\n')[0]),'isolated browser debugger');
  const target=(await fetch(`http://127.0.0.1:${port}/json/list`).then(r=>r.json())).find(t=>t.type==='page');assert.ok(target);
  ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{ws.addEventListener('open',resolve,{once:true});ws.addEventListener('error',reject,{once:true});});
  let sequence=0;const pending=new Map();const errors=[];
  ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);if(!p)return;clearTimeout(p.timer);pending.delete(m.id);if(m.error)p.reject(new Error(m.error.message));else p.resolve(m.result);}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);});
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('Debugger command timed out: '+method));},10000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});
  const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value;};until.diagnose=pageDiagnosis(evaluate);
  await send('Runtime.enable');await send('Page.enable');await send('Emulation.setDeviceMetricsOverride',{width:1024,height:768,deviceScaleFactor:1,mobile:false});await send('Page.addScriptToEvaluateOnNewDocument',{source:fetchTracker});await send('Page.navigate',{url:url+'/'});
  await until(()=>evaluate("document.body.innerText.includes('Company conversation')"),'company UI');
  const dashboard=()=>fetch(url+'/api/company').then(r=>r.json());
  // Hire a Support employee through the page's own API, then open its conversation.
  const route=await evaluate(`(async()=>{const cmd=async(action,fields)=>{const r=await fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action,...fields})});const t=await r.text();if(!r.ok)throw Error(action+' '+r.status+' '+t);return JSON.parse(t);};
    const dept=await cmd('department.create',{name:'Support'});const state=await (await fetch('/api/company')).json();const fields=state.data.roles.find(r=>r.id==='support').onboardingFields;
    const k=await cmd('knowledge.save',{departmentId:dept.id,body:JSON.stringify(Object.fromEntries(fields.map(f=>[f,'Approved fictional '+f])))});await cmd('knowledge.review',{id:k.id,expectedVersion:k.version,digest:k.digest,decision:'approve'});
    const e=await cmd('employee.hire',{departmentId:dept.id,roleId:'support',name:'Support partner',responsibility:'Fictional customers'});const th=await cmd('thread.create',{employeeId:e.id,name:'Inbox'});return '/?view=employee&employee='+e.id+'&thread='+th.id;})()`);
  await send('Page.navigate',{url:url+route});await until(()=>evaluate("!!document.querySelector('button[aria-label=\"Prepare reply for review\"]')"),'Support conversation');

  // P0 2: a long action field is multi-line, so separated messages stay separate.
  await evaluate("document.querySelector('.co-action-strip summary').click()");
  await evaluate("[...document.querySelectorAll('.co-action-strip button')].find(b=>b.textContent==='Prepare customer messages').click()");await until(()=>evaluate("!!document.querySelector('[aria-label^=\"Customer messages\"]')"),'action form opened from its button');
  assert.equal(await evaluate("document.querySelector('[aria-label^=\"Customer messages\"]').tagName"),'TEXTAREA','Customer messages take several lines');
  await evaluate(`(()=>{const f=document.querySelector('[aria-label^="Customer messages"]');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(f,${JSON.stringify(messages)});f.dispatchEvent(new Event('input',{bubbles:true}));})()`);await delay(100);
  await evaluate("document.querySelector('[aria-label^=\"Customer messages\"]').closest('[role=group]').querySelector('button').click()");
  await until(async()=>(await dashboard()).data.artifacts.some(a=>a.provenance==='action:support-intake'),'prepared messages saved for review');
  await until(()=>evaluate("document.body.innerText.includes('prepared by the support-intake action: 3')"),'three messages, split');
  // P0 1: nothing raw is stored or shown.
  const state=await dashboard();const intake=state.data.tasks.find(t=>t.skill==='support-intake');
  assert.ok(!intake.input.includes('4111')&&!intake.input.includes('jo@example.invalid'),'Stored action input is redacted');
  assert.equal(await evaluate("document.body.innerText.includes('4111')||document.body.innerText.includes('jo@example.invalid')"),false,'No raw card or email on screen');

  // P0 3: a prepared reply loads its exact review without another click; consent stays unchecked.
  await evaluate("document.querySelector('.co-action-strip summary').click()");
  await evaluate(`(()=>{const set=(el,v)=>{const p=el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(p,'value').set.call(el,v);el.dispatchEvent(new Event('input',{bubbles:true}));};set(document.querySelector('.co-conversation-composer input[type=number]'),'5');set(document.querySelector('textarea[placeholder^="Message"]'),'What do customers ask most often?');})()`);await delay(100);
  await evaluate("document.querySelector('button[aria-label=\"Prepare reply for review\"]').click()");
  const box="[...document.querySelectorAll('input[type=checkbox]')].find(c=>c.closest('label')?.textContent.includes('I authorize'))";
  await until(()=>evaluate("!!document.querySelector('.co-reply-turn .co-packet')"),'reply review loaded automatically');
  await until(()=>evaluate(box+'.disabled===false'),'reviewing the context unlocks the box');
  assert.equal(await evaluate(box+'.checked'),false,'Loading the review does not authorize it');
  assert.equal(await evaluate("[...document.querySelectorAll('button')].some(b=>b.textContent.includes('to unlock'))"),false,'The hint goes once the context is shown');

  // P0 4: the conversation keeps room to read, and nothing overlaps, at 1024x768.
  const layout=await evaluate("(()=>{const b=s=>document.querySelector(s).getBoundingClientRect();return {transcript:b('.fe-chat-scroll').height,transcriptBottom:b('.fe-chat-scroll').bottom,composerTop:b('.co-conversation-composer').top,composerBottom:b('.co-conversation-composer').bottom,height:innerHeight};})()");
  assert.ok(layout.transcript>=280,'The transcript keeps room to read: '+JSON.stringify(layout));
  assert.ok(layout.transcriptBottom<=layout.composerTop+1,'The transcript and composer do not overlap: '+JSON.stringify(layout));
  assert.ok(layout.composerBottom<=layout.height+1,'The composer fits on screen: '+JSON.stringify(layout));

  assert.equal(errors.length,0,'No uncaught UI exceptions');
  console.log('PASS real Chrome walkthrough fixes: multi-line customer messages split, nothing raw stored or shown, locked authorization explains and unlocks, readable conversation at 1024x768');
  await send('Browser.close').catch(()=>{});
}catch(e){await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/walkthrough-browser-host.log'),hostLog);throw e;}
finally{
  ws?.close();browser?.kill();host.kill();await delay(400);
  // Delete only this script's verified mkdtemp root after its own processes have exited.
  if(path.dirname(temporary)===path.resolve(os.tmpdir())&&path.basename(temporary).startsWith('hirezero-manual-ui-'))await fs.rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
