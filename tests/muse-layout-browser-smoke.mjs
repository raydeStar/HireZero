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
  // Home is the landing page: the company at a glance and the team with what each is doing (UX round 2).
  await until(()=>evaluate("!!document.querySelector('.co-home-tiles')&&document.querySelectorAll('.co-home .co-team-card').length>0"),'home overview');
  // Size an A/B test result for the fictional Marketing employee through the page's own API: pure arithmetic, no tools.
  const route=await evaluate(`(async()=>{const s=await (await fetch('/api/company')).json();const m=s.data.employees.find(e=>e.roleId==='marketing');const th=s.data.threads.find(t=>t.employeeId===m.id&&(t.scope??'employee')==='employee');const r=await fetch('/api/company/actions',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:'action.start',employeeId:m.id,threadId:th.id,skill:'ab-significance',body:JSON.stringify({'control-visitors':'1000','control-conversions':'50','variant-visitors':'1000','variant-conversions':'70',alpha:'5'})})});if(!r.ok)throw new Error('action '+r.status);return '/?view=employee&employee='+m.id+'&thread='+th.id;})()`);
  await send('Page.navigate',{url:url+route});
  await until(()=>evaluate("!!document.querySelector('.co-workspace.with-cockpit .co-cockpit [aria-label=\"Needs you\"] .fe-up-next-title')"),'docked cockpit lists what needs you');
  const docked=await evaluate("(()=>{const c=document.querySelector('.co-cockpit').getBoundingClientRect();return {right:Math.round(c.right),width:innerWidth,title:document.querySelector('[aria-label=\"Needs you\"] .fe-up-next-title span').textContent}})()");
  assert.equal(docked.right,docked.width,'The cockpit is pinned to the right edge');
  assert.match(docked.title,/Read an A\/B test result/,'Needs you names the work');
  // A failed needs read must not become an all-clear, and a retry works without any company mutation.
  const unavailableNeeds=await send('Page.addScriptToEvaluateOnNewDocument',{source:`(()=>{const original=window.fetch;window.fixtureNeedsUnavailable=true;window.fetch=(input,options)=>{if(String(input)==='/api/company/today'&&window.fixtureNeedsUnavailable)return Promise.reject(new TypeError('Synthetic needs outage'));return original(input,options);};})()`});
  await send('Page.reload');
  await until(()=>evaluate("!!document.querySelector('.co-cockpit .co-needs-error button')"),'failed needs load exposes a retry');
  assert.equal(await evaluate("document.querySelector('.co-cockpit [aria-label=\"Needs you\"]').innerText.includes('Nothing needs you')"),false,'A failed first read never reports all clear');
  assert.equal(await evaluate("document.querySelector('.co-cockpit [aria-label=\"Needs you\"] .fe-count').textContent"),'?','An unknown count is not zero');
  await evaluate("document.querySelector('.co-navigation button[aria-label=\"Home\"]').click()");
  await until(()=>evaluate("!!document.querySelector('.co-home .co-needs-error')"),'Home also explains the unavailable needs count');
  await evaluate("window.fixtureNeedsUnavailable=false;document.querySelector('.co-home .co-needs-error button').click()");
  await until(()=>evaluate("!document.querySelector('.co-needs-error')&&!!document.querySelector('.co-cockpit [aria-label=\"Needs you\"] .fe-up-next-title')"),'read-only retry restores the waiting review');
  await evaluate(`(async()=>{window.fixtureNeedsUnavailable=true;const r=await fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:'company.message',body:'Fictional needs-refresh fixture note'})});if(!r.ok)throw Error('fixture note '+r.status)})()`);
  await until(()=>evaluate("!!document.querySelector('.co-cockpit .co-needs-error')"),'later needs refresh fails');
  assert.equal(await evaluate("document.querySelectorAll('.co-cockpit [aria-label=\"Needs you\"] .fe-up-next-title').length"),1,'A later failed read retains the last checked review');
  assert.match(await evaluate("document.querySelector('.co-cockpit .co-needs-error').textContent"),/last checked/,'Retained results are identified as last checked');
  await evaluate("window.fixtureNeedsUnavailable=false;document.querySelector('.co-cockpit .co-needs-error button').click()");
  await until(()=>evaluate("!document.querySelector('.co-needs-error')"),'later failed read recovers in place');
  await send('Page.removeScriptToEvaluateOnNewDocument',{identifier:unavailableNeeds.identifier});
  // Opening it shows the work window beside the chat, which stays loaded.
  await evaluate("document.querySelector('[aria-label=\"Needs you\"] .fe-up-next-title').click()");
  await until(()=>evaluate("location.search.includes('work=')&&!!document.querySelector('.co-chat-and-work .co-work-window')&&!!document.querySelector('.co-chat-and-work .co-conversation')"),'work window beside the chat');
  await until(()=>evaluate("!document.body.innerText.includes('Opening this conversation')"),'the conversation stays loaded');
  // The chat shows a placeholder for the open work instead of a second, separately loading copy (UX round 5).
  await until(()=>evaluate("!!document.querySelector('.co-chat-and-work .co-conversation .co-work-placeholder')&&!document.querySelector('.co-chat-and-work .co-conversation .co-job-card')"),'one copy of the open work');
  // Work mode: the open work fills the panel; closing it shows the board.
  await evaluate("[...document.querySelectorAll('.co-mode button')].find(b=>b.textContent.startsWith('Work')).click()");
  await until(()=>evaluate("location.search.includes('mode=work')&&!!document.querySelector('.co-work-window')"),'work mode keeps the open work');
  await evaluate("document.querySelector('button[aria-label=\"Close work\"]').click()");
  await until(()=>evaluate("!!document.querySelector('.co-work-board [aria-label=\"Needs review\"] .co-work-item')"),'the Work board groups the work');
  // An explicit Chat action keeps its promise even after working in board mode.
  await evaluate("document.querySelector('.co-navigation button[aria-label=\"Home\"]').click()");
  await until(()=>evaluate("!!document.querySelector('.co-home button[aria-label^=\"Chat with\"]')"),'Home chat action');
  await evaluate("document.querySelector('.co-home button[aria-label^=\"Chat with\"]').click()");
  await until(()=>evaluate("!!document.querySelector('.co-conversation')&&!location.search.includes('mode=work')"),'Home Chat opens Chat after Work mode');
  await evaluate("[...document.querySelectorAll('.co-mode button')].find(b=>b.textContent.startsWith('Work')).click()");
  await until(()=>evaluate("!!document.querySelector('.co-work-board')"),'return to Work for shortcut checks');
  // Keyboard shortcuts (UX round 3): ? lists them, Esc closes, w switches mode, . hides the cockpit, g h goes Home.
  const key=async(k,modifiers=0)=>{
    const code=k==='Escape'?'Escape':k==='Tab'?'Tab':k==='?'?'Slash':k==='.'?'Period':k==='/'?'Slash':'Key'+k.toUpperCase();
    const virtual=k==='Escape'?27:k==='Tab'?9:k==='?'||k==='/'?191:k==='.'?190:k.toUpperCase().charCodeAt(0);
    for(const type of ['keyDown','keyUp'])await send('Input.dispatchKeyEvent',{type,key:k,code,windowsVirtualKeyCode:virtual,modifiers});
  };
  await evaluate("window.fixtureHelpReturn=document.querySelector('.co-navigation button[aria-label=\"Home\"]');window.fixtureHelpReturn.focus()");
  const beforeHelp=await evaluate('location.search');
  await key('?');await until(()=>evaluate("!!document.querySelector('[aria-label=\"Keyboard shortcuts\"]')"),'shortcut list');
  assert.equal(await evaluate("!!document.activeElement?.closest('dialog:modal')"),true,'Shortcut help takes keyboard focus');
  await evaluate('window.fixtureHelpReturn.focus()');
  assert.equal(await evaluate("!!document.activeElement?.closest('dialog:modal')"),true,'Background controls cannot take focus through a modal');
  for(const shortcut of ['g','h','w','/','.','?'])await key(shortcut);
  assert.equal(await evaluate('location.search'),beforeHelp,'Modal keys do not navigate the page behind the help');
  assert.equal(await evaluate("document.querySelectorAll('dialog:modal').length"),1,'Help cannot open a second overlay through shortcuts');
  await key('Tab');await key('Tab',8);
  assert.equal(await evaluate("document.activeElement?.closest('dialog')?.getAttribute('aria-label')"),'Keyboard shortcuts','Keyboard traversal stays with shortcut help');
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  await fs.writeFile(path.join(repo,'test-results/shortcuts-modal-desktop.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false})).data,'base64'));
  await key('Escape');await until(()=>evaluate("!document.querySelector('[aria-label=\"Keyboard shortcuts\"]')"),'shortcut list closes');
  assert.equal(await evaluate('document.activeElement===window.fixtureHelpReturn'),true,'Escape restores focus to the help opener');
  await key('w');await until(()=>evaluate("!location.search.includes('mode=work')"),'w switches to Chat');
  await key('.');await until(()=>evaluate("!document.querySelector('.co-workspace.with-cockpit')"),'. hides the cockpit');
  await key('.');await until(()=>evaluate("!!document.querySelector('.co-workspace.with-cockpit')"),'. shows the cockpit');
  // The theme switch cycles System, Light and Dark and sets the page's theme.
  await evaluate("document.querySelector('button[aria-label^=\"Theme:\"]').click()");
  await until(()=>evaluate("document.documentElement.dataset.theme==='light'"),'light theme');
  await evaluate("document.querySelector('button[aria-label^=\"Theme:\"]').click()");
  await until(()=>evaluate("document.documentElement.dataset.theme==='dark'"),'dark theme');
  await key('g');await key('h');await until(()=>evaluate("!!document.querySelector('.co-home')"),'g h goes Home');
  // Below laptop width (1024 px): no docked cockpit; the top bar opens it as a sheet.
  await send('Emulation.setDeviceMetricsOverride',{width:900,height:750,deviceScaleFactor:1,mobile:false});
  await until(()=>evaluate("!document.querySelector('.co-workspace.with-cockpit')&&!!document.querySelector('button.co-cockpit-toggle')"),'cockpit toggle on a narrow screen');
  assert.match(await evaluate("document.querySelector('button.co-cockpit-toggle').getAttribute('aria-label')"),/^Open cockpit, 1 need you$/);
  await evaluate("document.querySelector('button.co-cockpit-toggle').focus();document.querySelector('button.co-cockpit-toggle').click()");
  await until(()=>evaluate("!!document.querySelector('.fe-sheet .co-cockpit')"),'cockpit sheet');
  assert.equal(await evaluate("!!document.activeElement?.closest('.fe-sheet')"),true,'Opening the cockpit moves keyboard focus into the overlay');
  assert.equal(await evaluate("document.activeElement?.getAttribute('aria-label')"),'Hide cockpit','The visible close button receives initial focus');
  await key('Tab');
  assert.equal(await evaluate("!!document.activeElement?.closest('dialog:modal')"),true,'Tab reaches cockpit content');
  const beforeCockpit=await evaluate('location.search');
  for(const shortcut of ['g','p','w','/','?'])await key(shortcut);
  assert.equal(await evaluate('location.search'),beforeCockpit,'Cockpit keys cannot change the underlying route');
  await key('Escape');await until(()=>evaluate("!document.querySelector('.fe-sheet')"),'Escape closes cockpit');
  assert.equal(await evaluate("document.activeElement?.matches('.co-cockpit-toggle')"),true,'Cockpit dismissal restores focus to its opener');
  await evaluate("document.querySelector('button.co-cockpit-toggle').click()");
  await until(()=>evaluate("!!document.querySelector('dialog:modal')"),'cockpit reopens');
  await evaluate("document.querySelector('.fe-sheet-scrim').click()");
  await until(()=>evaluate("!document.querySelector('.fe-sheet')"),'the sheet closes');
  assert.equal(await evaluate("document.activeElement?.matches('.co-cockpit-toggle')"),true,'Backdrop dismissal restores focus');
  await evaluate("document.querySelector('button.co-cockpit-toggle').click()");
  await until(()=>evaluate("!!document.querySelector('dialog:modal')"),'cockpit opens before resize');
  await send('Emulation.setDeviceMetricsOverride',{width:1600,height:900,deviceScaleFactor:1,mobile:false});
  await until(()=>evaluate("!document.querySelector('dialog')&&!!document.querySelector('.co-workspace.with-cockpit')"),'wide layout replaces the modal with the dock');
  await send('Emulation.setDeviceMetricsOverride',{width:1100,height:750,deviceScaleFactor:1,mobile:false});
  await until(()=>evaluate("!document.querySelector('.co-workspace.with-cockpit')"),'narrow layout restored');
  assert.equal(await evaluate("!!document.querySelector('dialog')"),false,'An old sheet does not reopen after a wide-to-narrow resize');
  // On a phone the conversation list is a drawer, closed until opened (UX round 4).
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await until(()=>evaluate("getComputedStyle(document.querySelector('.co-conversations')).display==='none'"),'drawer closed on a phone');
  await evaluate("document.querySelector('.co-conversation-toggle').click()");
  await until(()=>evaluate("getComputedStyle(document.querySelector('.co-conversations')).display!=='none'"),'drawer opens');
  await key('Escape');
  await until(()=>evaluate("getComputedStyle(document.querySelector('.co-conversations')).display==='none'"),'Escape closes the conversation drawer');
  await evaluate("document.querySelector('button.co-cockpit-toggle').focus();document.querySelector('button.co-cockpit-toggle').click()");
  await until(()=>evaluate("!!document.querySelector('dialog:modal .co-cockpit')"),'phone cockpit opens');
  assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'Phone cockpit does not overflow');
  assert.equal(await evaluate("(()=>{const r=document.querySelector('dialog .co-cockpit').getBoundingClientRect();return r.left>=0&&Math.round(r.right)<=innerWidth&&Math.round(r.bottom)<=innerHeight;})()"),true,'Phone cockpit stays inside the viewport');
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  await fs.writeFile(path.join(repo,'test-results/cockpit-modal-mobile.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false})).data,'base64'));
  await evaluate("document.querySelector('dialog [aria-label=\"Needs you\"] .fe-up-next-title').click()");
  await until(()=>evaluate("!document.querySelector('dialog')&&location.search.includes('work=')&&!!document.querySelector('.co-work-window')"),'a cockpit item closes the modal and opens its exact work');
  assert.equal(await evaluate('document.querySelectorAll("dialog:modal").length'),0,'Work navigation releases the modal background');

  // Company sections belong to browser history, including when revisiting from the rail or reloading.
  await send('Emulation.setDeviceMetricsOverride',{width:1600,height:900,deviceScaleFactor:1,mobile:false});
  await evaluate("document.querySelector('.co-navigation button[aria-label=\"Company\"]').click()");
  const hub=label=>evaluate(`[...document.querySelectorAll('.co-hub-tabs button')].find(b=>b.textContent===${JSON.stringify(label)}).click()`);
  const selectedSection=()=>evaluate("document.querySelector('.co-hub-tabs [aria-current=page]')?.textContent");
  await until(async()=>(await selectedSection())==='Today','Company opens Today');
  await hub('Integrations');await until(async()=>(await selectedSection())==='Integrations','Integrations opens');
  await hub('Budgets');await until(async()=>(await selectedSection())==='Budgets','Budgets opens');
  await evaluate('history.back()');await until(async()=>(await selectedSection())==='Integrations','Back restores the previous section');
  await evaluate('history.forward()');await until(async()=>(await selectedSection())==='Budgets','Forward restores Budgets');
  await key('g');await key('h');await until(()=>evaluate("!!document.querySelector('.co-home')"),'Home after company sections');
  await evaluate("document.querySelector('.co-navigation button[aria-label=\"Company\"]').click()");
  await until(async()=>(await selectedSection())==='Budgets','rail restores the last company section');
  assert.equal(await evaluate("new URLSearchParams(location.search).get('section')"),'budgets','visible section matches the address');
  await send('Page.reload');await until(async()=>(await selectedSection())==='Budgets','reload preserves the visible section');

  // A fictional assistant asks for a disabled local feature; Open setup reveals the instructions already on this page.
  let mcpSession='';
  async function mcp(method,params){
    const response=await fetch(url+'/mcp',{method:'POST',headers:{'Content-Type':'application/json',...(mcpSession?{'Mcp-Session-Id':mcpSession}:{})},body:JSON.stringify({jsonrpc:'2.0',id:method,method,params})});
    assert.ok(response.ok,'fixture MCP response '+response.status);
    mcpSession=response.headers.get('Mcp-Session-Id')||mcpSession;
    const body=await response.json();assert.ok(!body.error,'fixture MCP protocol success');return body.result;
  }
  await mcp('initialize',{protocolVersion:'2025-06-18',clientInfo:{name:'Fictional browser assistant'}});
  const request=await mcp('tools/call',{name:'request_connection',arguments:{integration_id:'live-coding',reason:'Show the fictional coding setup instructions.'}});
  assert.equal(request.isError,false,'fictional request saved without starting a provider');
  await hub('Integrations');
  await until(()=>evaluate("!![...document.querySelectorAll('[aria-label=\"Assistant requests\"] button')].find(b=>b.textContent==='Open setup')"),'assistant setup link');
  await evaluate("[...document.querySelectorAll('[aria-label=\"Assistant requests\"] button')].find(b=>b.textContent==='Open setup').click()");
  assert.equal(await evaluate("document.activeElement?.dataset.integrationId"),'live-coding','Open setup focuses the matching instructions');
  assert.equal(await evaluate("(()=>{const r=document.activeElement.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight})()"),true,'matching setup instructions are visible');

  // Both empty leadership screens start the requested role, while an edited hire is never silently replaced.
  const beforeHiring=await fetch(url+'/api/company').then(r=>r.json());
  const ceoRole=beforeHiring.data.roles.find(r=>r.skills.includes('company-plan'));
  const otherRole=beforeHiring.data.roles.find(r=>r.id!==ceoRole.id);
  const hireCeoFrom=async place=>{
    await evaluate(`document.querySelector('.co-navigation button[aria-label="${place}"]').click()`);
    await until(()=>evaluate("!!document.querySelector('.co-needs-ceo button')"),place+' offers a CEO hire');
    await evaluate("document.querySelector('.co-needs-ceo button').click()");
    await until(()=>evaluate("!!document.querySelector('.co-onboarding select')"),'guided hiring opened from '+place);
  };
  await hireCeoFrom('Plans');
  await until(()=>evaluate(`document.querySelector('.co-onboarding select').value===${JSON.stringify(ceoRole.id)}`),'Plans preselects the CEO role');
  await hireCeoFrom('Meetings');
  await until(()=>evaluate(`document.querySelector('.co-onboarding select').value===${JSON.stringify(ceoRole.id)}`),'Meetings preselects the CEO role');
  await evaluate(`(()=>{const select=document.querySelector('.co-onboarding select');Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(select,${JSON.stringify(otherRole.id)});select.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  await until(()=>evaluate(`document.querySelector('.co-onboarding select').value===${JSON.stringify(otherRole.id)}`),'another role draft selected');
  await evaluate("(()=>{const input=document.querySelector('.co-onboarding input');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'Fictional draft to preserve');input.dispatchEvent(new Event('input',{bubbles:true}));})()");
  for(const place of ['Plans','Meetings']){
    await hireCeoFrom(place);
    await until(()=>evaluate("document.querySelector('.co-onboarding').innerText.includes('Finish the current hire before starting a CEO. Your draft is kept.')"),place+' explains the preserved draft');
    assert.equal(await evaluate("document.querySelector('.co-onboarding select').value"),otherRole.id,place+' preserves the draft role');
    assert.equal(await evaluate("document.querySelector('.co-onboarding input').value"),'Fictional draft to preserve',place+' preserves edited text');
  }
  await send('Page.reload');
  await until(()=>evaluate("document.querySelector('.co-onboarding input')?.value==='Fictional draft to preserve'"),'preserved draft survives reload after a CEO shortcut');
  const afterHiring=await fetch(url+'/api/company').then(r=>r.json());
  assert.equal(afterHiring.data.employees.length,beforeHiring.data.employees.length,'Hiring shortcuts do not hire automatically');
  assert.equal(afterHiring.data.departments.length,beforeHiring.data.departments.length,'Hiring shortcuts do not create departments');

  assert.equal(errors.length,0,'No uncaught UI exceptions');
  console.log('PASS real Chrome Muse layout: home overview, real keyboard shortcuts, modal focus and background isolation, Escape/backdrop focus restoration, responsive docking, exact cockpit work navigation, theme switch, loaded chat and work board, drawer Escape, company section history and reload, assistant setup navigation, CEO shortcuts with draft preservation. The doorway now has a doorman.');
  await send('Browser.close').catch(()=>{});
}catch(e){await fs.mkdir(path.join(repo,'test-results'),{recursive:true});await fs.writeFile(path.join(repo,'test-results/muse-layout-browser-host.log'),hostLog);throw e;}
finally{
  ws?.close();
  const stop=async child=>{
    if(!child||child.exitCode!==null||child.signalCode!==null)return;
    await new Promise((resolve,reject)=>{
      const done=()=>{clearTimeout(timer);resolve();};
      const timer=setTimeout(()=>{child.off('exit',done);reject(new Error('Owned fixture process has not exited; preserve its temporary files.'));},6000);
      child.once('exit',done);child.kill();
    });
  };
  await stop(browser);await stop(host);
  // Delete only this script's verified mkdtemp root after its own processes have exited.
  if(path.dirname(temporary)===path.resolve(os.tmpdir())&&path.basename(temporary).startsWith('hirezero-manual-ui-'))await fs.rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
}
