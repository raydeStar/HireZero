// Fictional planning and meeting drafts against an isolated host with no model provider.
// Failed or unconfirmed saves keep the owner's work; the butler does not file a second copy behind their back.
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
const host=spawn('dotnet',[path.join(repo,'tests/HireZero.Company.Tests/bin/Debug/net10.0/HireZero.Company.Tests.dll'),'--company-browser-fixture',temporary,url,webRoot],{cwd:hostDir,windowsHide:true,env:{...process.env,HIREZERO_LOCAL_OWNER:'1',HIREZERO_OWNER_LOGIN:'owner@example.invalid',HIREZERO_DATA_ROOT:path.join(temporary,'data'),HIREZERO_EXECUTION_HOLD:'0'},stdio:['ignore','pipe','pipe']});
let hostLog='';host.stdout.on('data',b=>hostLog+=b);host.stderr.on('data',b=>hostLog+=b);let browser,ws;
const until=waiter();
const dashboard=()=>fetch(url+'/api/company').then(r=>r.json());
async function command(action,fields){
  const response=await fetch(url+'/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','Origin':url,'X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action,...fields})});
  assert.ok(response.ok,'Fictional fixture command '+action+' succeeds (HTTP '+response.status+')');return response.json();
}
try{
  await until(()=>hostLog.includes('Now listening on: '+url),'this test host owns the listener');
  await until(async()=>(await fetch(url+'/health')).ok,'fresh host health');
  assert.equal((await fetch(url+'/health').then(r=>r.json())).modelExecution,false,'No model provider');
  const roles=(await dashboard()).data.roles;
  async function hire(roleId,name){
    const department=await command('department.create',{name:'Fictional '+name+' department'});
    const role=roles.find(r=>r.id===roleId);
    const context=await command('knowledge.save',{departmentId:department.id,body:JSON.stringify(Object.fromEntries(role.onboardingFields.map(field=>[field,'Approved fictional '+field])))});
    await command('knowledge.review',{id:context.id,expectedVersion:context.version,digest:context.digest,decision:'approve'});
    return command('employee.hire',{departmentId:department.id,roleId,name,responsibility:'Review fictional company work'});
  }
  const chair=await hire(roles.find(role=>role.skills.includes('company-plan')).id,'Planning chair');
  const employee=await hire('marketing','Planning contributor');
  browser=spawn(chrome,['--headless=new','--disable-gpu','--disable-background-networking','--disable-sync','--disable-default-apps','--no-first-run','--no-default-browser-check','--no-proxy-server','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+path.join(temporary,'browser'),'about:blank'],{windowsHide:true,stdio:'ignore'});
  const port=await until(async()=>Number((await fs.readFile(path.join(temporary,'browser','DevToolsActivePort'),'utf8')).split('\n')[0]),'isolated browser debugger');
  const target=(await fetch(`http://127.0.0.1:${port}/json/list`).then(r=>r.json())).find(t=>t.type==='page');assert.ok(target);
  ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{ws.addEventListener('open',resolve,{once:true});ws.addEventListener('error',reject,{once:true});});
  let sequence=0;const pending=new Map();const errors=[];
  ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);if(!p)return;clearTimeout(p.timer);pending.delete(m.id);if(m.error)p.reject(new Error(m.error.message));else p.resolve(m.result);}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);});
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('Debugger command timed out: '+method));},10000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});
  const evaluate=async expression=>{const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value;};until.diagnose=pageDiagnosis(evaluate);
  await send('Runtime.enable');await send('Page.enable');await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});await send('Page.addScriptToEvaluateOnNewDocument',{source:fetchTracker});await send('Page.navigate',{url:url+'/'});
  await until(()=>evaluate("!!document.querySelector('.co-home')"),'company home');
  const click=clicker(evaluate,until),idle=pageIdle(evaluate,until);
  const plan='form[aria-label="Propose company plan"]',meeting='form[aria-label="Prepare company meeting"]';
  const set=async(selector,value)=>{
    await evaluate(`(()=>{const el=document.querySelector(${JSON.stringify(selector)});if(!el)throw Error('Missing fixture input');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`);
    await delay(40);
  };
  const values=selector=>evaluate(`Array.from(document.querySelector(${JSON.stringify(selector)}).querySelectorAll('input:not([type=checkbox]),textarea,select')).map(el=>el.value)`);
  await evaluate('window.__planningOriginalFetch=window.fetch');
  const inject=(action,mode)=>evaluate(`(()=>{const original=window.__planningOriginalFetch;window.fetch=async(input,options)=>{const body=options?.body?JSON.parse(options.body):{};if(body.action===${JSON.stringify(action)}){window.fetch=original;if(${JSON.stringify(mode)}==='definitive')return new Response(JSON.stringify({error:'Synthetic planning refusal'}),{status:422,headers:{'Content-Type':'application/json'}});await original(input,options);await new Promise(resolve=>window.__releasePlanning=resolve);window.fetch=async(input,options)=>{if(String(input).includes('/operations/'))throw new TypeError('Synthetic unavailable receipt');return original(input,options);};throw new TypeError('Synthetic lost saved response');}return original(input,options);};})()`);
  const release=()=>evaluate('window.__releasePlanning();delete window.__releasePlanning');
  const restore=()=>evaluate('window.fetch=window.__planningOriginalFetch');

  await click('Plans');await until(()=>evaluate(`!!document.querySelector(${JSON.stringify(plan)})`),'plan form');
  await set(plan+' > label textarea','Fictional launch review');
  assert.equal(await evaluate(`document.querySelector(${JSON.stringify(plan)}).querySelector('button[type=submit]').disabled`),true,'An empty plan cannot be submitted');
  for(let index=0;index<4;index++){
    await click('Add task');
    await set(plan+` fieldset:nth-of-type(${index+1}) select`,employee.id);
    await set(plan+` fieldset:nth-of-type(${index+1}) textarea`,'Fictional task '+(index+1));
    if(index>0)await evaluate(`document.querySelector(${JSON.stringify(plan+` fieldset:nth-of-type(${index+1}) input[type=checkbox]`)}).click()`);
  }
  await evaluate(`document.querySelector(${JSON.stringify(plan)}).querySelector('[aria-label="Remove task 2"]').click()`);
  assert.deepEqual(await evaluate(`Array.from(document.querySelector(${JSON.stringify(plan)}).querySelectorAll('fieldset textarea')).map(el=>el.value)`),['Fictional task 1','Fictional task 3','Fictional task 4'],'Removing the middle task keeps the other inputs');
  assert.deepEqual(await evaluate(`Array.from(document.querySelector(${JSON.stringify(plan)}).querySelectorAll('fieldset input[type=checkbox]')).map(el=>el.checked)`),[false,true],'A removed dependency is dropped; later dependencies shift with their tasks');
  await click('Add task');await evaluate(`document.querySelector(${JSON.stringify(plan)}).querySelector('[aria-label="Remove task 4"]').click()`);
  assert.equal(await evaluate(`document.querySelector(${JSON.stringify(plan)}).checkValidity()`),true,'An accidental blank task can be removed without refilling the plan');
  const planDraft=await values(plan);
  await inject('plan.create','definitive');await click('Propose plan');await until(()=>evaluate("document.body.textContent.includes('Synthetic planning refusal')"),'definitive plan refusal');
  assert.deepEqual(await values(plan),planDraft,'A refused plan keeps objective and task inputs');assert.equal((await dashboard()).data.plans.length,0);
  await inject('plan.create','uncertain');await click('Propose plan');await until(()=>evaluate("typeof window.__releasePlanning==='function'"),'plan response held');
  assert.equal(await evaluate(`Array.from(document.querySelector(${JSON.stringify(plan)}).querySelectorAll('input,textarea,select,button')).every(el=>el.matches(':disabled'))`),true,'Plan inputs remain fixed while a save is in flight');
  await evaluate(`document.querySelector(${JSON.stringify(plan)}).dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))`);await release();
  await until(()=>evaluate("document.body.textContent.includes('outcome is unconfirmed')"),'unconfirmed plan shown');
  assert.deepEqual(await values(plan),planDraft,'An uncertain plan save keeps the whole draft');
  let state=await dashboard();assert.equal(state.data.plans.length,1,'Repeated submission cannot duplicate the plan');
  assert.deepEqual(state.data.plans[0].tasks.map(task=>task.dependsOn),[[],[],[1]],'Saved dependencies reference surviving task indexes');
  await click('Propose plan');assert.equal((await dashboard()).data.plans.length,1,'Unavailable receipt prevents replay');
  await restore();await click('Propose plan');await until(()=>evaluate("document.body.textContent.includes('previous operation is confirmed')"),'plan receipt reconciled');
  assert.equal((await dashboard()).data.plans.length,1,'Checking the receipt does not propose another plan');
  await set(plan+' > label textarea','A separate fictional plan');await click('Propose plan');await idle();
  assert.equal((await dashboard()).data.plans.length,2);assert.deepEqual(await values(plan),[''],'Only a confirmed successful plan clears its draft');

  await click('Meetings');await until(()=>evaluate(`!!document.querySelector(${JSON.stringify(meeting)})`),'meeting form');
  await set(meeting+' > label input','Fictional decision meeting');await set(meeting+' > label textarea','Review fictional launch choices.');
  for(const name of ['Planning chair','Planning contributor']){
    await evaluate(`Array.from(document.querySelector(${JSON.stringify(meeting)}).querySelectorAll('fieldset:first-of-type label')).find(label=>label.textContent.trim()===${JSON.stringify(name)}).querySelector('input').click()`);
    await delay(40);
  }
  await set(meeting+' > label select',chair.id);const meetingDraft=await values(meeting);
  await inject('meeting.create','definitive');await click('Prepare meeting');await until(()=>evaluate("document.body.textContent.includes('Synthetic planning refusal')"),'definitive meeting refusal');
  assert.deepEqual(await values(meeting),meetingDraft,'A refused meeting keeps title, agenda, and chair');assert.equal((await dashboard()).data.meetings.length,0);
  await inject('meeting.create','uncertain');await click('Prepare meeting');await until(()=>evaluate("typeof window.__releasePlanning==='function'"),'meeting response held');
  assert.equal(await evaluate(`Array.from(document.querySelector(${JSON.stringify(meeting)}).querySelectorAll('input,textarea,select,button')).every(el=>el.matches(':disabled'))`),true,'Meeting inputs remain fixed while a save is in flight');
  await evaluate(`document.querySelector(${JSON.stringify(meeting)}).dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))`);await release();
  await until(()=>evaluate("document.body.textContent.includes('outcome is unconfirmed')"),'unconfirmed meeting shown');
  assert.deepEqual(await values(meeting),meetingDraft,'An uncertain meeting keeps its draft');assert.equal((await dashboard()).data.meetings.length,1);
  await click('Prepare meeting');assert.equal((await dashboard()).data.meetings.length,1,'An unconfirmed meeting is not replayed');
  await restore();await click('Prepare meeting');await until(()=>evaluate("document.body.textContent.includes('previous operation is confirmed')"),'meeting receipt reconciled');
  assert.equal((await dashboard()).data.meetings.length,1,'Checking the meeting receipt does not create another meeting');
  await set(meeting+' > label input','A separate fictional meeting');await click('Prepare meeting');await idle();
  state=await dashboard();assert.equal(state.data.meetings.length,2);assert.deepEqual(await values(meeting),['','',chair.id],'A confirmed meeting clears its title and agenda');
  assert.ok(state.data.plans.every(item=>item.state==='proposed'));assert.ok(state.data.meetings.every(item=>item.state==='draft'));assert.equal(state.data.tasks.length,0,'Planning does not dispatch any work');
  assert.equal(errors.length,0,'No uncaught UI exceptions');
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  await fs.writeFile(path.join(repo,'test-results/planning-recovery-browser.json'),JSON.stringify({liveModelCalls:0,planProposals:2,meetingDrafts:2,tasks:0,failedDraftsPreserved:true,uncertainDraftsPreserved:true,duplicateCreates:0,removedTaskDependencies:[[],[],[1]]},null,2));
  console.log('PASS planning recovery: plan and meeting drafts survive refusals and uncertain replies, duplicate submissions stay single, task removal repairs dependencies. No paperwork sacrificed.');
  await send('Browser.close').catch(()=>{});
}catch(error){await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/planning-recovery-browser-host.log'),hostLog);throw error;}
finally{
  ws?.close();browser?.kill();host.kill();await delay(400);
  // Delete only this script's verified temporary root after its own processes exit.
  if(path.dirname(temporary)===path.resolve(os.tmpdir())&&path.basename(temporary).startsWith('hirezero-manual-ui-'))await fs.rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
