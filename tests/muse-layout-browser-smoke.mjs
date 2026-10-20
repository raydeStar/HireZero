// The Muse layout (docs/adr/0040-the-muse-layout.md), checked in headless Chrome against a disposable company-local host
// with the compiled fake provider: the cockpit is pinned beside the conversation and lists what needs you; opening it
// shows the work window beside a still-loaded chat; Work mode shows the board; on a narrower screen the cockpit is a
// sheet opened from the top bar. No user browser profile or live provider.
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
  await send('Runtime.enable');await send('Page.enable');await send('Emulation.setDeviceMetricsOverride',{width:1600,height:900,deviceScaleFactor:1,mobile:false});await send('Page.addScriptToEvaluateOnNewDocument',{source:fetchTracker});await send('Page.navigate',{url:url+'/'});
  await until(()=>evaluate("document.body.innerText.includes('Company conversation')"),'company UI');
  // Size an A/B test result for the fictional Marketing employee through the page's own API: pure arithmetic, no tools.
  const route=await evaluate(`(async()=>{const s=await (await fetch('/api/company')).json();const m=s.data.employees.find(e=>e.roleId==='marketing');const th=s.data.threads.find(t=>t.employeeId===m.id&&(t.scope??'employee')==='employee');const r=await fetch('/api/company/actions',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:'action.start',employeeId:m.id,threadId:th.id,skill:'ab-significance',body:JSON.stringify({'control-visitors':'1000','control-conversions':'50','variant-visitors':'1000','variant-conversions':'70',alpha:'5'})})});if(!r.ok)throw new Error('action '+r.status);return '/?view=employee&employee='+m.id+'&thread='+th.id;})()`);
  await send('Page.navigate',{url:url+route});
  await until(()=>evaluate("!!document.querySelector('.co-workspace.with-cockpit .co-cockpit [aria-label=\"Needs you\"] .fe-up-next-title')"),'docked cockpit lists what needs you');
  const docked=await evaluate("(()=>{const c=document.querySelector('.co-cockpit').getBoundingClientRect();return {right:Math.round(c.right),width:innerWidth,title:document.querySelector('[aria-label=\"Needs you\"] .fe-up-next-title span').textContent}})()");
  assert.equal(docked.right,docked.width,'The cockpit is pinned to the right edge');
  assert.match(docked.title,/Read an A\/B test result/,'Needs you names the work');
  // Opening it shows the work window beside the chat, which stays loaded.
  await evaluate("document.querySelector('[aria-label=\"Needs you\"] .fe-up-next-title').click()");
  await until(()=>evaluate("location.search.includes('work=')&&!!document.querySelector('.co-chat-and-work .co-work-window')&&!!document.querySelector('.co-chat-and-work .co-conversation')"),'work window beside the chat');
  await until(()=>evaluate("!document.body.innerText.includes('Opening this conversation')"),'the conversation stays loaded');
  // Work mode: the open work fills the panel; closing it shows the board.
  await evaluate("[...document.querySelectorAll('.co-mode button')].find(b=>b.textContent.startsWith('Work')).click()");
  await until(()=>evaluate("location.search.includes('mode=work')&&!!document.querySelector('.co-work-window')"),'work mode keeps the open work');
  await evaluate("document.querySelector('button[aria-label=\"Close work\"]').click()");
  await until(()=>evaluate("!!document.querySelector('.co-work-board [aria-label=\"Needs review\"] .co-work-item')"),'the Work board groups the work');
  // A narrower screen: no docked cockpit; the top bar opens it as a sheet.
  await send('Emulation.setDeviceMetricsOverride',{width:1100,height:750,deviceScaleFactor:1,mobile:false});
  await until(()=>evaluate("!document.querySelector('.co-workspace.with-cockpit')&&!!document.querySelector('button.co-cockpit-toggle')"),'cockpit toggle on a narrow screen');
  assert.match(await evaluate("document.querySelector('button.co-cockpit-toggle').getAttribute('aria-label')"),/^Open cockpit, 1 need you$/);
  await evaluate("document.querySelector('button.co-cockpit-toggle').click()");
  await until(()=>evaluate("!!document.querySelector('.fe-sheet .co-cockpit')"),'cockpit sheet');
  await evaluate("document.querySelector('.fe-sheet-scrim').click()");
  await until(()=>evaluate("!document.querySelector('.fe-sheet')"),'the sheet closes');

  assert.equal(errors.length,0,'No uncaught UI exceptions');
  console.log('PASS real Chrome Muse layout: docked cockpit lists what needs you, work window beside a loaded chat, Work board, cockpit sheet at 1100px');
  await send('Browser.close').catch(()=>{});
}catch(e){await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/muse-layout-browser-host.log'),hostLog);throw e;}
finally{
  ws?.close();browser?.kill();host.kill();await delay(400);
  // Delete only this script's verified mkdtemp root after its own processes have exited.
  if(path.dirname(temporary)===path.resolve(os.tmpdir())&&path.basename(temporary).startsWith('hirezero-manual-ui-'))await fs.rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
