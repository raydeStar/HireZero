// Runs an installed headless Chrome against a disposable company-local host. No user browser profile or live provider.
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {spawn} from 'node:child_process';
import assert from 'node:assert/strict';
const repo=path.resolve(import.meta.dirname,'..');
const temporary=await fs.mkdtemp(path.join(os.tmpdir(),'hirezero-manual-ui-'));
const chrome=process.env.HIREZERO_TEST_CHROME||path.join(process.env.ProgramFiles||'C:/Program Files','Google/Chrome/Application/chrome.exe');
const hostDir=path.join(repo,'src/HireZero.Host');
const host=spawn('dotnet',[path.join(repo,'tests/HireZero.Company.Tests/bin/Debug/net10.0/HireZero.Company.Tests.dll'),'--measured-browser-fixture',temporary],{cwd:hostDir,windowsHide:true,env:{...process.env,HIREZERO_LOCAL_OWNER:'1',HIREZERO_OWNER_LOGIN:'owner@example.invalid',HIREZERO_DATA_ROOT:path.join(temporary,'data'),HIREZERO_EXECUTION_HOLD:'0'},stdio:['ignore','pipe','pipe']});
let hostLog='';host.stdout.on('data',b=>hostLog+=b);host.stderr.on('data',b=>hostLog+=b);let browser,ws;
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function until(fn,label){for(let i=0;i<150;i++){try{const result=await fn();if(result)return result;}catch{}await delay(100);}throw new Error('Timed out: '+label);}
try{
  await until(()=>hostLog.includes('Now listening on: http://127.0.0.1:5065'),'this test host owns the listener');
  await until(async()=>{const r=await fetch('http://127.0.0.1:5065/health');return r.ok;},'fresh host health');
  const health=await fetch('http://127.0.0.1:5065/health').then(r=>r.json());assert.equal(health.modelExecution,false);
  browser=spawn(chrome,['--headless=new','--disable-gpu','--disable-background-networking','--disable-sync','--disable-default-apps','--no-first-run','--no-default-browser-check','--no-proxy-server','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+path.join(temporary,'browser'),'about:blank'],{windowsHide:true,stdio:'ignore'});
  const port=await until(async()=>{const text=await fs.readFile(path.join(temporary,'browser','DevToolsActivePort'),'utf8');return Number(text.split('\n')[0]);},'isolated browser debugger');
  const targets=await fetch(`http://127.0.0.1:${port}/json/list`).then(r=>r.json());const target=targets.find(t=>t.type==='page');assert.ok(target);
  ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{ws.addEventListener('open',resolve,{once:true});ws.addEventListener('error',reject,{once:true});});
  let sequence=0;const pending=new Map();const errors=[];
  ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);if(!p)return;clearTimeout(p.timer);pending.delete(m.id);if(m.error)p.reject(new Error(m.error.message));else p.resolve(m.result);}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);});
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('Debugger command timed out: '+method));},10000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});
  const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value;};
  await send('Runtime.enable');await send('Page.enable');await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});await send('Page.navigate',{url:'http://127.0.0.1:5065/'});
  await until(()=>evaluate("document.body.innerText.includes('Company conversation')"),'company UI');
  const click=async text=>{await evaluate(`(()=>{const b=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()===${JSON.stringify(text)});if(!b)throw Error('Button missing: '+${JSON.stringify(text)});b.click();})()`);await delay(150);};
  const field=async(label,value)=>{await evaluate(`(()=>{const label=[...document.querySelectorAll('.co-main label')].find(l=>l.childNodes[0]?.textContent.trim()===${JSON.stringify(label)});if(!label)throw Error('Field missing: '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`);await delay(70);};
  assert.match(hostLog,/BROWSER FIXTURE: fake provider only/);
  const dashboard=()=>fetch('http://127.0.0.1:5065/api/company').then(r=>r.json());
  const starts=async()=>{const files=await fs.readdir(temporary,{recursive:true});let count=0;for(const file of files.filter(p=>p.endsWith('fixture-native-starts')))count+=(await fs.readFile(path.join(temporary,file),'utf8')).trim().split('\n').filter(Boolean).length;return count;};
  await click('Open manual jobs');await until(()=>evaluate("document.body.innerText.includes('Private employee conversation')"),'manual jobs employee');
  await field('Work type','marketing-draft');
  const brief=JSON.parse(await fs.readFile(path.join(repo,'docs/ONE_JOB_MARKETING_BRIEF.json'),'utf8'));
  for(const [key,value] of Object.entries(brief))await field(key,value);
  await click('Assign work');await until(async()=>(await dashboard()).data.tasks.length===1,'brief saved');
  await until(()=>evaluate("document.body.innerText.includes('Waiting for your one-job authorization.')"),'manual queue waiting state');
  assert.equal(await starts(),0,'Assigning a brief makes no provider request');
  await field('Intended existing-credit allowance','1');await click('Prepare job for review');
  await until(async()=>(await dashboard()).data.measured[0]?.job.state==='proposed','sealed proposal');assert.equal(await starts(),0,'Preparing a job makes no provider request');
  const authorizeDisabled=()=>evaluate("[...document.querySelectorAll('.co-measured button')].find(b=>b.textContent==='Authorize and run one job').disabled");
  assert.equal(await authorizeDisabled(),true,'Unread work cannot authorize');
  await click('Review task and context');await until(()=>evaluate("!!document.querySelector('.co-packet .co-packet-fields')"),'readable task and facts');
  assert.equal(await evaluate("document.querySelector('.co-packet').innerText.includes('Adult paper craft hobbyists')"),true);
  assert.equal(await authorizeDisabled(),true,'Reading the packet does not grant authority');
  await evaluate("document.querySelector('.co-measured input[type=checkbox]').click()");await until(async()=>!(await authorizeDisabled()),'explicit acknowledgement');
  await click('Authorize and run one job');await until(async()=>(await dashboard()).data.tasks[0].state==='awaiting_review','fake draft saved for owner review');
  let state=await dashboard();assert.equal(state.data.artifacts[0].state,'needs_review');assert.equal(await starts(),1,'Exactly one fake-native request');
  await send('Page.reload');await until(()=>evaluate("document.body.innerText.includes('Manual employee jobs')"),'reload management');await click('Open manual jobs');
  await until(()=>evaluate("document.body.innerText.includes('Draft saved for review')"),'saved receipt survives reload');assert.equal(await starts(),1,'Reload never resumes or replays');
  assert.equal(await evaluate("[...document.querySelectorAll('button')].some(b=>b.textContent==='Authorize and run one job')"),false,'Consumed grant has no run button');
  await click('Approve content');await until(async()=>(await dashboard()).data.artifacts[0].state==='approved','exact content approval');await click('Complete after review');
  await until(async()=>(await dashboard()).data.tasks[0].state==='completed','reviewed task completed');assert.equal(await starts(),1,'Content review never calls the provider');
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/manual-queue-browser.json'),JSON.stringify({provider:'compiled fake only',liveModelCalls:0,fakeNativeStarts:1,assignStarts:0,prepareStarts:0,readablePacket:true,explicitAcknowledgement:true,reloadReplay:false,ownerReview:'approved',taskState:'completed'},null,2));
  assert.equal(errors.length,0,'No uncaught UI exceptions');await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  const desktop=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(repo,'test-results/manual-queue-desktop.png'),Buffer.from(desktop.data,'base64'));
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await delay(250);assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'Mobile layout does not overflow');
  assert.equal(await evaluate("document.querySelector('.co-status').getBoundingClientRect().bottom<=document.querySelector('.co-notice').getBoundingClientRect().top"),true,'Mobile header and capability notice do not overlap');
  const mobile=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path.join(repo,'test-results/manual-queue-mobile.png'),Buffer.from(mobile.data,'base64'));
  console.log('PASS real Chrome manual queue: assign, prepare without dispatch, readable review, acknowledgement, one fake run, reload without replay, owner review, completion and desktop/mobile rendering');
  await send('Browser.close').catch(()=>{});
}catch(e){await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/manual-queue-browser-host.log'),hostLog);throw e;}
finally{
  ws?.close();browser?.kill();host.kill();await delay(400);
  // Delete only this script's verified mkdtemp root after its own processes have exited.
  if(path.dirname(temporary)===path.resolve(os.tmpdir())&&path.basename(temporary).startsWith('hirezero-manual-ui-'))await fs.rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
