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
const host=spawn('dotnet',[path.join(repo,'tests/HireZero.Company.Tests/bin/Debug/net10.0/HireZero.Company.Tests.dll'),'--measured-browser-fixture',temporary,url,webRoot],{cwd:hostDir,windowsHide:true,env:{...process.env,HIREZERO_LOCAL_OWNER:'1',HIREZERO_OWNER_LOGIN:'owner@example.invalid',HIREZERO_DATA_ROOT:path.join(temporary,'data'),HIREZERO_EXECUTION_HOLD:'0',HIREZERO_BROWSER_SYNTHETIC_MODE:'company-conversation-browser'},stdio:['ignore','pipe','pipe']});
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


  const state=await dashboard(),marketing=state.data.employees.find(e=>e.roleId==='marketing'),developer=state.data.employees.find(e=>e.roleId==='developer');
  assert.equal(state.capabilities.conversationalReplies,true);assert.equal(await starts(),0);
  const openEmployee=async person=>{await evaluate("[...document.querySelectorAll('.co-conversations .co-list')].find(b=>b.textContent.includes("+JSON.stringify(person.name)+")).click()");await until(()=>evaluate("!!document.querySelector('textarea[aria-label='+CSS.escape("+JSON.stringify('Message to '+person.name)+")+']:not(:disabled)')"),'private conversation ready');};
  const write=async(label,text)=>{await evaluate("(()=>{const input=document.querySelector('textarea[aria-label='+CSS.escape("+JSON.stringify(label)+")+']');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(input,"+JSON.stringify(text)+");input.dispatchEvent(new Event('input',{bubbles:true}));})()");};
  const twice=async()=>{await idle();await evaluate("(()=>{const form=document.querySelector('.co-conversation-composer form');form.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));form.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));})()");}
  const review=async()=>{await until(()=>evaluate("[...document.querySelectorAll('.co-reply-turn button')].some(b=>b.textContent==='Review reply and context')"),'reviewable reply');await click('Review reply and context');await until(()=>evaluate("!!document.querySelector('.co-reply-turn .co-packet')"),'sealed reply packet visible');};
  const authorize=async()=>{await evaluate("document.querySelector('.co-reply-turn .co-measured input[type=checkbox]').click()");await until(()=>evaluate("[...document.querySelectorAll('.co-reply-turn button')].some(b=>b.textContent==='Send & authorize one reply'&&!b.disabled)"),'explicit acknowledgement');await evaluate("(()=>{const button=[...document.querySelectorAll('.co-reply-turn button')].find(b=>b.textContent==='Send & authorize one reply');button.click();button.click();})()");};
  const fieldReady=async(label,value)=>{await until(()=>evaluate("[...document.querySelectorAll('.co-main label')].some(l=>l.childNodes[0]?.textContent.trim()==="+JSON.stringify(label)+")"),label+' available');await field(label,value);};
  const ownerPost=async(action,fields)=>{const r=await fetch(url+'/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','Origin':url,'X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action,...fields})});assert.equal(r.status,200);return r.json();};
  let preparedCapture=false;
  const openPreparedNeed=async(person)=>{
    await evaluate("document.querySelector('.co-navigation button[aria-label=\"Home\"]').click()");
    await until(()=>evaluate("[...document.querySelectorAll('.co-home .co-team-card')].find(c=>c.textContent.includes("+JSON.stringify(person.name)+"))?.textContent.includes('Waiting for your authorization')"),'Home employee status explains the pending authorization');
    await until(()=>evaluate("[...document.querySelectorAll('.co-cockpit [aria-label=\"Needs you\"] .fe-up-next-title')].some(b=>b.textContent.includes('authorize a reply'))"),'prepared reply waits on owner in cockpit');
    assert.equal(await evaluate("document.querySelectorAll('.co-cockpit [aria-label=\"Working on\"] .fe-up-next-title').length"),0,'prepared reply is not presented as running');
    await evaluate("[...document.querySelectorAll('.co-cockpit [aria-label=\"Needs you\"] .fe-up-next-title')].find(b=>b.textContent.includes('authorize a reply')).click()");
    await until(()=>evaluate("document.activeElement?.hasAttribute('data-reply-task')&&!!document.querySelector('.co-reply-turn')"),'need opens and focuses the reply review in Chat');
    assert.equal(await evaluate("!!document.querySelector('.co-work-window')"),false,'reply needs never open an empty work window');
    assert.equal(await evaluate("new URLSearchParams(location.search).get('mode')==='work'"),false,'reply need opens Chat mode');
    if(!preparedCapture){
      await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
      const screenshot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
      await fs.writeFile(path.join(repo,'test-results/conversation-reply-prepared.png'),Buffer.from(screenshot.data,'base64'));
      preparedCapture=true;
    }
  };
  const facts=await ownerPost('knowledge.save',{body:'APPROVED_COMPANY_BROWSER_FACT'});await ownerPost('knowledge.review',{id:facts.id,expectedVersion:facts.version,digest:facts.digest,decision:'approve'});
  await openEmployee(marketing);await evaluate("document.querySelector('.co-private-context').open=true");await field('Private notes','ONLY_PRIVATE_BROWSER_NOTE');await click('Save private notes');
  await until(async()=>{const s=await dashboard(),t=s.data.threads.find(t=>t.employeeId===marketing.id&&t.scope==='employee');return (await fetch(url+'/api/company/employees/'+marketing.id+'/threads/'+t.id).then(r=>r.json())).notes.body==='ONLY_PRIVATE_BROWSER_NOTE';},'private notes saved');
  await evaluate("document.querySelector('.co-private-context').open=false");await fieldReady('Message action','save');await write('Message to '+marketing.name,'ONLY_PRIVATE_BROWSER_HISTORY');await twice();
  await until(()=>evaluate("document.querySelector('.fe-thread').textContent.includes('ONLY_PRIVATE_BROWSER_HISTORY')"),'saved-only history');assert.equal((await dashboard()).data.tasks.length,0,'Saving alone creates no autonomous task');assert.equal(await starts(),0);
  // Main company reply gets a separate transcript and a smaller context.
  await evaluate("document.querySelector('button[aria-label=\"Chat\"]').click()");await fieldReady('Reply as',marketing.id);await fieldReady('Credit allowance','1');await write('Company message','COMPANY_BROWSER_QUESTION: Which shared facts are approved?');await twice();
  await until(async()=>(await dashboard()).data.measured.length===1,'one prepared company reply');
  await fieldReady('Reply as','');await openPreparedNeed(marketing);
  assert.equal(await evaluate("[...document.querySelectorAll('.co-main label')].find(l=>l.childNodes[0]?.textContent.trim()==='Reply as').querySelector('select').value"),marketing.id,'company need restores the correct reply recipient');
  await review();assert.equal(await starts(),0,'Preparation and full context review never infer');
  const packet=await evaluate("document.querySelector('.co-reply-turn .co-packet').textContent");assert.ok(packet.includes('APPROVED_COMPANY_BROWSER_FACT'));assert.ok(packet.includes('company context'));assert.equal(packet.includes('ONLY_PRIVATE_BROWSER'),false,'Company review excludes private notes/history');assert.equal(packet.includes('Fictional paper crane kit'),false,'Company review excludes department facts');
  assert.equal(await evaluate("[...document.querySelectorAll('.co-reply-turn button')].find(b=>b.textContent==='Send & authorize one reply').disabled"),true);
  await authorize();await until(async()=>(await dashboard()).data.tasks[0].state==='completed','company reply completed');await until(()=>evaluate("document.querySelector('.fe-thread').textContent.includes('Fictional conversational reply.')"),'company reply rendered');assert.equal(await starts(),1,'Duplicate clicks consume exactly one fake turn');
  const companyState=await dashboard(),companyThread=companyState.data.threads.find(t=>t.employeeId===marketing.id&&t.scope==='company');assert.ok(companyThread);assert.equal(companyState.data.artifacts.length,0);assert.equal(companyState.data.plans.length,0);assert.equal(companyState.data.tasks.length,1);assert.equal(companyState.data.measured[0].reportedUsage,4950);
  await openEmployee(marketing);assert.equal(await evaluate("document.querySelector('.fe-thread').textContent.includes('COMPANY_BROWSER_QUESTION')"),false,'Company prompt stays outside private history');
  await field('Message action','reply');await fieldReady('Credit allowance','1');await write('Message to '+marketing.name,'PRIVATE_BROWSER_QUESTION: What is my supplied private topic?');await twice();await until(async()=>(await dashboard()).data.measured.length===2,'private reply prepared');await openPreparedNeed(marketing);await review();
  const privatePacket=await evaluate("document.querySelector('.co-reply-turn .co-packet').textContent");assert.ok(privatePacket.includes('ONLY_PRIVATE_BROWSER_NOTE'));assert.ok(privatePacket.includes('ONLY_PRIVATE_BROWSER_HISTORY'));assert.equal(privatePacket.includes('COMPANY_BROWSER_QUESTION'),false);await authorize();await until(async()=>(await dashboard()).data.tasks[1].state==='completed','private reply completed');assert.equal(await starts(),2);
  const route=await evaluate('location.search');await send('Page.reload');await until(()=>evaluate("document.querySelector('.fe-thread')?.textContent.includes('PRIVATE_BROWSER_QUESTION')&&document.querySelector('.fe-thread')?.textContent.includes('Fictional conversational reply.')"),'persisted private history after reload');assert.equal(await evaluate('location.search'),route);assert.equal(await starts(),2,'Reload never repeats a consumed reply');
  const companyHistory=await fetch(url+'/api/company/employees/'+marketing.id+'/threads/'+companyThread.id).then(r=>r.json());assert.equal(JSON.stringify(companyHistory).includes('PRIVATE_BROWSER_QUESTION'),false);assert.equal(companyHistory.notes.body,'');
  // Developer conversation is text-only Luna, independent of the unavailable coding route.
  await openEmployee(developer);await fieldReady('Credit allowance','1');await write('Message to '+developer.name,'Explain one safe next step without running code.');await twice();await until(async()=>(await dashboard()).data.measured.length===3,'developer conversation prepared');await review();assert.ok((await evaluate("document.querySelector('.co-reply-turn .co-packet').textContent")).includes('developer'));await authorize();await until(async()=>(await dashboard()).data.tasks[2].state==='completed','developer text reply');
  let completed=await dashboard();assert.equal(completed.data.tasks[2].modelRoute,'openai/gpt-6-luna');assert.equal(completed.data.executions.length,0);assert.equal(await starts(),3);await until(()=>evaluate("document.querySelectorAll('.fe-msg.assistant').length===1"),'one developer reply');
  // The compiled fake waits on this explicit fixture marker, so pending and Stop are real.
  await fieldReady('Credit allowance','1');await write('Message to '+developer.name,'[wait] Hold this fictional reply until cancellation.');await twice();await until(async()=>(await dashboard()).data.measured.length===4,'wait reply prepared');await review();await authorize();
  await until(()=>evaluate("document.body.innerText.includes('Reply pending.')&&[...document.querySelectorAll('button')].some(b=>b.textContent==='Stop reply')"),'real pending state and cancel control');await until(async()=>(await fs.readdir(temporary,{recursive:true})).some(p=>p.endsWith('fixture-partial-emitted')),'The compiled fake actually emitted an unconfirmed delta');assert.equal(await evaluate("document.querySelectorAll('.fe-msg.assistant').length"),1,'Unconfirmed partial output is not published');assert.equal(await evaluate("document.body.innerText.includes('UNCONFIRMED_PARTIAL_REPLY_SHOULD_NOT_RENDER')"),false,'Buffered native deltas stay private until complete accounting');
  await until(()=>evaluate("document.querySelectorAll('.co-cockpit [aria-label=\"Working on\"] .fe-up-next-title').length===1&&!document.querySelector('.co-cockpit [aria-label=\"Needs you\"]').textContent.includes('authorize a reply')"),'authorized pending reply moves from Needs you to Working on');
  await click('Stop reply',{idle:false});await until(async()=>(await dashboard()).data.tasks[3].state==='canceled','owner cancellation');await until(()=>evaluate("document.body.innerText.includes('Reply stopped.')"),'honest stopped outcome');assert.equal(await starts(),4,'One canceled native fake turn, no replay');assert.equal(await evaluate("document.querySelectorAll('.fe-msg.assistant').length"),1,'Canceled turn adds no reply');
  await send('Page.reload');await until(()=>evaluate("document.body.innerText.includes('Reply stopped.')"),'canceled receipt survives reload');assert.equal(await starts(),4);assert.equal(await evaluate("[...document.querySelectorAll('.co-reply-turn button')].some(b=>/resume|retry|Send & authorize/.test(b.textContent))"),false,'Consumed/uncertain turns have no replay or resume button');
  completed=await dashboard();assert.equal(completed.data.artifacts.length,0);assert.equal(completed.data.plans.length,0);assert.equal(completed.data.tasks.length,4);assert.equal(completed.data.executions.length,0);
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/conversation-reply-browser.json'),JSON.stringify({provider:'compiled fake stdio only',liveModelCalls:0,fakeNativeStarts:4,completedReplies:3,canceledReplies:1,prepareAndReviewStarts:0,saveOnlyCreatesNoTask:true,companyPrivateSeparation:true,lunaDeveloperTextRoute:true,duplicateSendStartsOneTurn:true,historySurvivesReload:true,pendingState:true,cancellationNoContent:true,reloadReplay:false,artifacts:0,plans:0,executionGrants:0},null,2));
  assert.equal(errors.length,0,'No uncaught UI exceptions');await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  const desktop=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(repo,'test-results/conversation-reply-desktop.png'),Buffer.from(desktop.data,'base64'));
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await delay(250);assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'Mobile layout does not overflow');
  assert.equal(await evaluate("document.querySelector('.fe-topbar').getBoundingClientRect().bottom<=document.querySelector('.co-conversation-context').getBoundingClientRect().top"),true,'Mobile header and conversation context do not overlap');
  const mobileGeometry=await evaluate("(()=>{const box=s=>{const r=document.querySelector(s).getBoundingClientRect();return {top:r.top,bottom:r.bottom,left:r.left,right:r.right,height:r.height}};return {allowance:box('.co-conversation-composer input[type=number]'),composer:box('.co-conversation-composer'),navigation:box('.co-navigation'),transcript:box('.fe-chat-scroll'),mode:box('.fe-topbar .co-mode'),cockpit:box('.co-cockpit-toggle'),viewport:innerHeight}})()");
  assert.ok(mobileGeometry.allowance.top>=0&&mobileGeometry.allowance.bottom<=mobileGeometry.viewport&&mobileGeometry.allowance.bottom<=mobileGeometry.navigation.top+1,'The required allowance stays visible above mobile navigation: '+JSON.stringify(mobileGeometry));
  assert.ok(mobileGeometry.allowance.top>=mobileGeometry.composer.top-1&&mobileGeometry.allowance.bottom<=mobileGeometry.composer.bottom+1,'The required allowance is inside the composer visible area: '+JSON.stringify(mobileGeometry));
  assert.ok(mobileGeometry.transcript.height>=120,'The mobile transcript keeps at least 120px of reading room: '+JSON.stringify(mobileGeometry));
  assert.ok(mobileGeometry.mode.top>=mobileGeometry.cockpit.bottom-1,'The mobile Chat/Work controls stay below the cockpit toggle: '+JSON.stringify(mobileGeometry));
  const actionGeometry=await evaluate("(()=>{const strip=document.querySelector('.co-action-strip').getBoundingClientRect(),row=document.querySelector('.co-action-buttons'),first=row.querySelector('.co-action-chip').getBoundingClientRect();return {firstVisible:first.top>=strip.top&&first.bottom<=strip.bottom&&first.left>=strip.left&&first.right<=strip.right,scrollable:row.scrollWidth>row.clientWidth,stripHeight:strip.height,first:{top:first.top,bottom:first.bottom,left:first.left,right:first.right},strip:{top:strip.top,bottom:strip.bottom,left:strip.left,right:strip.right}}})()");
  const actionScreenshot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(repo,'test-results/conversation-reply-mobile.png'),Buffer.from(actionScreenshot.data,'base64'));
  assert.equal(actionGeometry.firstVisible,true,'The first mobile action chip is fully visible inside its strip: '+JSON.stringify(actionGeometry));
  assert.equal(actionGeometry.scrollable,true,'The mobile action row scrolls horizontally to its remaining actions');
  assert.equal(await evaluate("(()=>{const row=document.querySelector('.co-action-buttons'),more=row.querySelector('.co-action-more');row.scrollLeft=row.scrollWidth;more.focus({preventScroll:true});const r=more.getBoundingClientRect(),strip=document.querySelector('.co-action-strip').getBoundingClientRect();return r.left>=strip.left&&r.right<=strip.right&&r.top>=strip.top&&r.bottom<=strip.bottom&&document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)===more&&document.activeElement===more})()"),true,'Scrolling the mobile action strip makes More actions fully visible and reachable');
  await evaluate("document.querySelector('.co-action-buttons').scrollLeft=0;document.activeElement.blur()");
  const mobile=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(repo,'test-results/conversation-reply-mobile.png'),Buffer.from(mobile.data,'base64'));
  console.log('PASS real Chrome conversational replies: explicit preparation/context/Send, company/private isolation, measured receipts, history, duplicate sends, pending/cancel and no replay; compiled fake only');
  await send('Browser.close').catch(()=>{});
}catch(e){await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/conversation-reply-browser-host.log'),hostLog);throw e;}
finally{
  ws?.close();browser?.kill();host.kill();await delay(400);
  // Delete only this script's verified mkdtemp root after its own processes have exited.
  if(path.dirname(temporary)===path.resolve(os.tmpdir())&&path.basename(temporary).startsWith('hirezero-manual-ui-'))await fs.rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
