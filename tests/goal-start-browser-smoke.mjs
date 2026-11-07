// Goal-based suggestions, role-owned setup and reviewed first work through the local UI; fictional providers only.
// Failed or unconfirmed saves keep the owner's work; the butler does not file a second copy behind their back.
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { spawn } from "node:child_process";
import assert from "node:assert/strict";
import {
  clicker,
  delay,
  fetchTracker,
  freeLocalUrl,
  pageDiagnosis,
  pageIdle,
  waiter,
} from "./browser-smoke-wait.mjs";

const repo = path.resolve(import.meta.dirname, "..");
const temporary = await fs.mkdtemp(
  path.join(os.tmpdir(), "hirezero-manual-ui-"),
);
const chrome =
  process.env.HIREZERO_TEST_CHROME ||
  path.join(
    process.env.ProgramFiles || "C:/Program Files",
    "Google/Chrome/Application/chrome.exe",
  );
const hostDir = path.join(repo, "src/HireZero.Host");
const url = process.env.HIREZERO_TEST_URL || (await freeLocalUrl());
const webRoot =
  process.env.HIREZERO_TEST_WEB_ROOT || path.join(hostDir, "wwwroot");
const host = spawn(
  "dotnet",
  [
    path.join(
      repo,
      "tests/HireZero.Company.Tests/bin/Debug/net10.0/HireZero.Company.Tests.dll",
    ),
    "--company-browser-fixture",
    temporary,
    url,
    webRoot,
  ],
  {
    cwd: hostDir,
    windowsHide: true,
    env: {
      ...process.env,
      HIREZERO_LOCAL_OWNER: "1",
      HIREZERO_OWNER_LOGIN: "owner@example.invalid",
      HIREZERO_DATA_ROOT: path.join(temporary, "data"),
      HIREZERO_EXECUTION_HOLD: "0",
      HIREZERO_BROWSER_TEAM: "1",
      HIREZERO_BROWSER_SYNTHETIC_BUSINESS: "1",
    },
    stdio: ["ignore", "pipe", "pipe"],
  },
);
let hostLog = "";
host.stdout.on("data", (b) => (hostLog += b));
host.stderr.on("data", (b) => (hostLog += b));
let browser, ws;
const until = waiter();
const dashboard = () => fetch(url + "/api/company").then((r) => r.json());
async function command(action, fields) {
  const response = await fetch(url + "/api/company/commands", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Origin: url,
      "X-Dev-Request": "1",
    },
    body: JSON.stringify({ requestId: crypto.randomUUID(), action, ...fields }),
  });
  assert.ok(
    response.ok,
    "Fictional fixture command " +
      action +
      " succeeds (HTTP " +
      response.status +
      ")",
  );
  return response.json();
}
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "this test host owns the listener",
  );
  await until(
    async () => (await fetch(url + "/health")).ok,
    "fresh host health",
  );
  assert.equal(
    (await fetch(url + "/health").then((r) => r.json())).modelExecution,
    false,
    "No model provider",
  );
  const oldDraft = await command("knowledge.save", {
    body: "Unverified fictional note: every kit includes a free workshop.",
  });
  browser = spawn(
    chrome,
    [
      "--headless=new",
      "--disable-gpu",
      "--disable-background-networking",
      "--disable-sync",
      "--disable-default-apps",
      "--no-first-run",
      "--no-default-browser-check",
      "--no-proxy-server",
      "--remote-debugging-port=0",
      "--remote-debugging-address=127.0.0.1",
      "--user-data-dir=" + path.join(temporary, "browser"),
      "about:blank",
    ],
    { windowsHide: true, stdio: "ignore" },
  );
  const port = await until(
    async () =>
      Number(
        (
          await fs.readFile(
            path.join(temporary, "browser", "DevToolsActivePort"),
            "utf8",
          )
        ).split("\n")[0],
      ),
    "isolated browser debugger",
  );
  const target = (
    await fetch(`http://127.0.0.1:${port}/json/list`).then((r) => r.json())
  ).find((t) => t.type === "page");
  assert.ok(target);
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    ws.addEventListener("open", resolve, { once: true });
    ws.addEventListener("error", reject, { once: true });
  });
  let sequence = 0;
  const pending = new Map();
  const errors = [];
  ws.addEventListener("message", (e) => {
    const m = JSON.parse(e.data);
    if (m.id) {
      const p = pending.get(m.id);
      if (!p) return;
      clearTimeout(p.timer);
      pending.delete(m.id);
      if (m.error) p.reject(new Error(m.error.message));
      else p.resolve(m.result);
    }
    if (m.method === "Runtime.exceptionThrown") {
      const detail=m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text;
      errors.push(detail);
      console.error(detail);
    }
  });
  const send = (method, params = {}) =>
    new Promise((resolve, reject) => {
      const id = ++sequence;
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(new Error("Debugger command timed out: " + method));
      }, 10000);
      pending.set(id, { resolve, reject, timer });
      ws.send(JSON.stringify({ id, method, params }));
    });
  const evaluate = async (expression) => {
    const r = await send("Runtime.evaluate", {
      expression,
      returnByValue: true,
      awaitPromise: true,
    });
    if (r.exceptionDetails)
      throw new Error(
        r.exceptionDetails.exception?.description || r.exceptionDetails.text,
      );
    return r.result.value;
  };
  until.diagnose = async () => {
    await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
    await fs.writeFile(path.join(repo,'test-results/goal-start-browser-failure.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false})).data,'base64'));
    return (await pageDiagnosis(evaluate)())+'; visible text: '+await evaluate('document.body.innerText.slice(0,2500)');
  };
  await send("Runtime.enable");
  await send("Page.enable");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.addScriptToEvaluateOnNewDocument", { source: fetchTracker });
  await send("Page.addScriptToEvaluateOnNewDocument", { source: "localStorage.setItem('dev-ui:home-start', '\"overview\"')" });
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("!!document.querySelector('.co-home')"),
    "company home",
  );
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  // Viewport changes settle asynchronously; measure the rendered mobile layout, not the preceding frame.
  await until(()=>evaluate("(()=>{const r=document.querySelector('.co-goal-start textarea').getBoundingClientRect();return innerWidth===390 && r.top>=0 && r.bottom<=document.querySelector('.co-main').getBoundingClientRect().bottom;})()"),'A fresh mobile Home shows the first business input above the navigation without scrolling');
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  await fs.writeFile(path.join(repo,'test-results/goal-home-fresh-mobile.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false})).data,'base64'));
  await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  const click = clicker(evaluate, until),
    idle = pageIdle(evaluate, until);
  const text = (phrase) =>
    until(
      () =>
        evaluate(`document.body.innerText.includes(${JSON.stringify(phrase)})`),
      phrase,
    );
  const setField = async (label,value) => evaluate(`(() => {
    const el=[...document.querySelectorAll('.co-goal-start label')].find(l=>l.firstChild.textContent.trim()===${JSON.stringify(label)})?.querySelector('input,textarea');
    if(!el) throw new Error('Goal field missing');
    Object.getOwnPropertyDescriptor(el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype,'value').set.call(el,${JSON.stringify(value)});
    el.dispatchEvent(new Event('input',{bubbles:true}));
  })()`);
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  const capture=async name=>fs.writeFile(path.join(repo,'test-results/'+name+'.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false})).data,'base64'));
  assert.equal(await evaluate("!!document.querySelector('[aria-label=\"Getting started\"]')"),false);
  await setField('What does your business do?','Fictional team studio sells paper craft kits.');
  await idle();
  await evaluate(`(()=>{const original=window.fetch;window.termsLookups=0;window.termsRecoveryCommands=0;window.fetch=async(input,options)=>{
    if(String(input)==='/api/company/commands') window.termsRecoveryCommands++;
    if(String(input).includes('/company/goals/terms?') && ++window.termsLookups===1)
      return new Response(JSON.stringify({error:'Fictional temporary terms failure'}),{status:503,headers:{'Content-Type':'application/json'}});
    return original(input,options);
  };})()`);
  await setField('Your goal','Improve website accessibility');
  await setField('Website (optional)','http://paper.example.invalid/');
  assert.equal(await evaluate("document.querySelector('.co-goal-start input[inputmode=url]').validity.valid"),false,'Only supported https addresses can be submitted');
  await setField('Website (optional)','  paper.example.invalid/  ');
  await text('Reads up to three public pages from https://paper.example.invalid/');
  assert.equal(await evaluate("document.querySelector('.co-goal-start input[inputmode=url]').validity.valid"),true,'A pasted hostname is accepted without another edit');
  await text('Your first technical plan');
  await text('Couldn’t load the model and run terms.');
  assert.equal(await evaluate("document.querySelector('.co-goal-start button[type=submit]').disabled"),true,'No dispatch without the run terms');
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await evaluate("document.querySelector('.co-goal-start [role=alert]').scrollIntoView({block:'center'})");
  assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth+1'),true);
  await capture('goal-terms-recovery-mobile');
  await click('Retry loading terms');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'run terms on Home');
  assert.equal(await evaluate('window.termsLookups'),2,'The retry only reloads the terms once');
  assert.equal(await evaluate('window.termsRecoveryCommands'),0,'Reloading terms cannot prepare or run work');
  assert.deepEqual(await evaluate("[...document.querySelectorAll('.co-goal-start textarea')].map(el=>el.value)"),['Fictional team studio sells paper craft kits.','Improve website accessibility'],'The owner keeps both inputs');
  assert.equal(await evaluate("document.querySelector('.co-goal-start input[inputmode=url]').value"),'  paper.example.invalid/  ');
  assert.equal(await evaluate("document.querySelector('.co-goal-start .co-check input').checked"),false,'Recovered terms require the owner’s consent');
  await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  await capture('goal-start-desktop');
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth+1'),true);
  await evaluate("document.querySelector('.co-goal-start .co-primary').scrollIntoView({block:'center'})");
  await delay(150);
  await capture('goal-start-mobile');
  await until(()=>evaluate("document.querySelector('.co-goal-start .co-primary').getBoundingClientRect().height >= 44"),'Main action remains comfortably tappable');
  await capture('goal-start-mobile');
  await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  await evaluate(`(()=>{const original=window.fetch;let refuse=true;window.fetch=async(input,options)=>{
    if(refuse&&String(input)==='/api/company/commands'&&JSON.parse(options?.body||'{}').action==='goal.context'){
      refuse=false;return new Response(JSON.stringify({error:'Fictional setup refusal'}),{status:422,headers:{'Content-Type':'application/json'}});
    }return original(input,options);
  };})()`);
  await click('Generate my first draft');
  await text('Fictional setup refusal');
  await click('Edit request');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start form')"),'confirmed refusal returns to Home editing');
  assert.equal((await dashboard()).data.tasks.length,0);
  assert.equal(await evaluate("document.querySelector('.co-goal-start .co-check input').checked"),false,'Fresh consent after editing a refused request');
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  // Lose the confirmed save reply and its immediate lookup. Reload must recover the same work.
  await evaluate(`(()=>{const original=window.fetch;window.fetch=async(input,options)=>{
    if(String(input).includes('/company/operations/')) throw new TypeError('Fictional unavailable lookup');
    if(String(input)==='/api/company/commands'&&JSON.parse(options?.body||'{}').action==='goal.prepare'){
      await original(input,options);throw new TypeError('Fictional lost reply');
    }
    return original(input,options);
  };})()`);
  await click('Generate my first draft');
  await text('Check saved request');
  let state=(await dashboard()).data;
  assert.equal(state.employees.length,1);
  assert.equal(state.employees[0].roleId,'developer');
  assert.equal(state.tasks.length,1);
  assert.equal(state.artifacts.length,0);
  const task=state.tasks[0];
  await send('Page.reload');
  await text('Check saved request');
  await click('Check saved request');
  await text('Fictional technical plan');
  assert.equal((await dashboard()).data.tasks.length,1);
  assert.equal(await evaluate(`new URLSearchParams(location.search).get('work')===${JSON.stringify(task.id)}`),true);
  state=(await dashboard()).data;
  const result=state.artifacts.find(a=>a.taskId===task.id);
  assert.ok(result);
  assert.equal(state.businessLookups.length,1);
  assert.ok(state.businessSources.length > 0);
  assert.equal(state.businessLookups[0].website,'https://paper.example.invalid/','The confirmed normalized destination reaches discovery');
  const packet = await fetch(url+'/api/company/measured/'+state.measured[0].id).then(r=>r.json());
  assert.deepEqual(state.knowledge.find(k=>k.id===oldDraft.id),oldDraft,'The old unapproved note is preserved without blocking Home');
  const resultAddress = await evaluate('location.href');
  await send('Page.navigate',{url:url+'/?view=home'});
  await until(()=>evaluate("[...document.querySelectorAll('.co-team-note-line.needs button')].some(b=>b.textContent.includes('Show me'))"),'saved knowledge decision on Home');
  await evaluate("document.querySelector('.co-team-note-line.needs button').click()");
  await until(()=>evaluate(`new URLSearchParams(location.search).get('knowledge')===${JSON.stringify(oldDraft.id)} && document.activeElement.id===${JSON.stringify('co-knowledge-'+oldDraft.id)}`),'Needs you opens and focuses the exact knowledge draft');
  assert.equal(await evaluate("(()=>{const r=document.activeElement.getBoundingClientRect();return r.top>=0 && r.top<innerHeight-60;})()"),true,'The knowledge draft is visible without digging');
  await send('Page.reload');
  await until(()=>evaluate(`document.activeElement.id===${JSON.stringify('co-knowledge-'+oldDraft.id)}`),'knowledge review destination survives reload');
  await send('Page.navigate',{url:resultAddress});
  await text('Fictional technical plan');
  assert.equal(JSON.stringify(packet.call).includes('free workshop'),false,'Unapproved notes do not become model context');
  assert.match(packet.call.context.websiteResearch.sources[0].text,/folding guide/);
  assert.equal(packet.call.context.businessBrief.fields.some(f=>f.key==='services'),false);
  assert.ok(packet.call.context.businessBrief.unfilledFields.includes('services'));
  assert.equal(state.businessProfiles[0].fields.find(f=>f.key==='services').verification,'unknown','Website excerpts do not become approved company facts');
  assert.equal(state.measured.length,1,'Only one model run after a lost reply');
  assert.equal(await evaluate(`document.querySelector('[data-artifact-id="${result.id}"]')?.open===true`),true);
  await capture('goal-first-result');
  assert.equal(await evaluate("!!document.querySelector('.co-live-strip.attention')"),false,'Unrelated company decisions do not interrupt the draft');
  await command('artifact.create',{id:task.id,expectedVersion:state.tasks[0].version,body:'Fictional revised accessibility draft.'});
  await send('Page.navigate',{url:url+'/'});
  await text('Your results');
  assert.equal(await evaluate("document.querySelectorAll('[aria-label=\"Recent results\"] li').length"),1);
  // Returning owners see their results before a form that might repeat yesterday's request.
  assert.equal(await evaluate("document.querySelector('.co-home > .co-home-hello + section').getAttribute('aria-label')"),'Recent results');
  await text('Fictional revised accessibility draft.');
  await click('Start next task');
  await setField('Your goal','Improve keyboard navigation.');
  await setField('Website (optional)','');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'next-run terms');
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  await click('Generate draft');
  await text('Fictional technical plan');
  state=(await dashboard()).data;
  assert.equal(state.tasks.length,2);
  assert.equal(state.measured.length,2);
  assert.equal(state.artifacts.filter(a=>a.taskId!==task.id).length,1,'Normal single-click path saves the first raw result');
  await send('Page.navigate',{url:url+'/'});
  await text('Start next task');
  await click('Start next task');
  await setField('Your goal','Improve website copy for our paper kits.');
  await text('Your marketing draft');
  await setField('Your goal','Write one welcome email for new paper-kit subscribers.');
  await text('Your marketing draft');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'marketing-run terms');
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  await click('Generate draft');
  await text('Fictional welcome email');
  state=(await dashboard()).data;
  const emailTask=state.tasks.find(t=>t.skill==='marketing-goal-draft');
  assert.ok(emailTask,'A requested email uses direct Marketing drafting');
  assert.equal(state.measured.length,3,'One run per consent, including a change of specialist');
  assert.equal(state.artifacts.filter(a=>a.taskId===emailTask.id).length,1);
  assert.doesNotMatch(emailTask.input,/Monday|Wednesday|Friday/);
  assert.equal(await evaluate(`new URLSearchParams(location.search).get('work')===${JSON.stringify(emailTask.id)}`),true);
  // A normal website-backed request must reach its raw draft from one Home consent, too.
  await send('Page.navigate',{url:url+'/'});
  await text('Start next task');
  await click('Start next task');
  await setField('Your goal','Write one welcome email using our website details.');
  await setField('Website (optional)','paper.example.invalid/');
  await text('Reads up to three public pages from https://paper.example.invalid/');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'website-backed run terms');
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  await click('Generate draft');
  await until(async()=>{
    const current=(await dashboard()).data;
    const task=current.tasks.find(t=>t.id!==emailTask.id && t.skill==='marketing-goal-draft');
    return task && current.artifacts.some(a=>a.taskId===task.id);
  },'new website-backed first result, not the earlier Home preview');
  state=(await dashboard()).data;
  const websiteTask=state.tasks.find(t=>t.id!==emailTask.id && t.skill==='marketing-goal-draft');
  assert.ok(websiteTask);
  await until(()=>evaluate(`new URLSearchParams(location.search).get('work')===${JSON.stringify(websiteTask.id)}`),'new website-backed work opens');
  await text('Fictional welcome email');
  assert.equal(state.businessLookups.length,2,'One new bounded lookup for the next website request');
  assert.equal(state.measured.length,4,'One model dispatch for each consent');
  assert.equal(state.artifacts.filter(a=>a.taskId===websiteTask.id).length,1);
  const websitePacket=await fetch(url+'/api/company/measured/'+state.measured.find(m=>m.taskId===websiteTask.id).id).then(r=>r.json());
  assert.equal(websitePacket.call.context.websiteResearch.website,'https://paper.example.invalid/');
  assert.ok(websitePacket.call.context.websiteResearch.sources.length>0);
  // Returning Home after feedback should continue the replacement request, not the superseded draft.
  await click('Refine draft');
  await until(()=>evaluate("document.activeElement===document.querySelector('.co-revision-feedback textarea')"),'Refine puts the cursor in the visible feedback field');
  assert.equal(await evaluate("(()=>{const r=document.querySelector('.co-revision-feedback textarea').getBoundingClientRect();return r.top>=0 && r.bottom<=innerHeight;})()"),true,'Refine feedback is visible without hunting through the draft');
  await click('Keep draft and close');
  assert.equal(await evaluate("document.activeElement?.textContent"),'Refine draft','Closing feedback returns focus to its action');
  assert.equal((await dashboard()).data.measured.length,4,'Opening and closing Refine cannot dispatch');
  await click('Refine draft');
  await evaluate(`(()=>{const el=document.querySelector('.co-revision-feedback textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(el,'Keep the folding example and make the reply invitation clearer.');el.dispatchEvent(new Event('input',{bubbles:true}));})()`);
  await click('Create revision request');
  const revisionTask = await until(async()=>{
    const current=(await dashboard()).data;
    return current.tasks.find(t=>t.revision?.sourceTaskId===websiteTask.id);
  },'saved revision request');
  await send('Page.navigate',{url:url+'/'});
  await text('Continue my work');
  await text('Your revision request is ready. Review its model and terms before running.');
  await click('Continue my work');
  assert.equal(await evaluate("new URLSearchParams(location.search).get('work')"),revisionTask.id,'Home continues the current revision rather than the superseded original');
  // Synthetic results exercise a second revision without calling any model provider.
  const firstRevisionResult = await command('artifact.create',{id:revisionTask.id,expectedVersion:revisionTask.version,body:'Fictional first revision for navigation only.'});
  const latestRevision = await command('artifact.revise',{id:firstRevisionResult.id,expectedVersion:firstRevisionResult.version,digest:firstRevisionResult.digest,body:'Keep the example and shorten the opening.'});
  await send('Page.navigate',{url:url+'/'});
  await text('Continue my work');
  await click('Continue my work');
  assert.equal(await evaluate("new URLSearchParams(location.search).get('work')"),latestRevision.id,'Home follows the full revision chain after reload');
  const finalFixtureDraft = 'Fictional latest revision, ready to read.';
  const finalFixtureBody = finalFixtureDraft+'\n\n*Review notes:* Fictional owner-only guidance.';
  await command('artifact.create',{id:latestRevision.id,expectedVersion:latestRevision.version,body:finalFixtureBody});
  await send('Page.navigate',{url:url+'/'});
  await text('Your draft is ready to read, copy or refine.');
  assert.equal(await evaluate("document.querySelectorAll('[aria-label=\"Recent results\"] li').length"),3,'Earlier superseded drafts do not crowd out distinct results');
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await capture('goal-current-revision-mobile');
  await click('Continue my work');
  await text('Fictional latest revision, ready to read.');
  assert.equal(await evaluate("new URLSearchParams(location.search).get('work')"),latestRevision.id,'The delivered revision opens from Home on mobile');
  await until(()=>evaluate("document.querySelector('.co-artifact[open] .co-report-content')?.innerText.trim() === "+JSON.stringify(finalFixtureDraft)),'Inline italic review notes stay outside the usable draft');
  assert.equal(await evaluate("document.querySelector('.co-artifact[open] .co-internal-review-notes').open"),false,'Review notes start collapsed');
  const useCurrentDraft = label => evaluate(`[...document.querySelectorAll('.co-artifact[open] button')].find(b=>b.textContent.trim()===${JSON.stringify(label)}).click()`);
  await evaluate("Object.defineProperty(navigator,'clipboard',{configurable:true,value:{writeText:async text=>{window.copiedDraft=text}}});");
  await useCurrentDraft('Copy draft');
  assert.equal(await evaluate('window.copiedDraft'),finalFixtureDraft,'Copy excludes internal notes');
  await evaluate("window.savedCreateObjectURL=URL.createObjectURL;window.savedAnchorClick=HTMLAnchorElement.prototype.click;URL.createObjectURL=blob=>{window.downloadedDraft=blob.text();return window.savedCreateObjectURL(blob)};HTMLAnchorElement.prototype.click=function(){window.downloadName=this.download};");
  await useCurrentDraft('Download draft');
  assert.equal(await evaluate('window.downloadedDraft'),finalFixtureDraft,'Download excludes internal notes');
  await evaluate("document.querySelector('.co-artifact[open] .co-internal-review-notes > summary').click()");
  await useCurrentDraft('Download full saved result');
  assert.equal(await evaluate('window.downloadedDraft'),finalFixtureBody,'The full raw result remains downloadable unchanged');
  await evaluate('URL.createObjectURL=window.savedCreateObjectURL;HTMLAnchorElement.prototype.click=window.savedAnchorClick;');
  await evaluate("document.querySelector('.co-artifact[open] .co-internal-review-notes > summary').click()");
  await useCurrentDraft('Refine draft');
  await until(()=>evaluate("document.activeElement===document.querySelector('.co-revision-feedback textarea')"),'Mobile Refine focuses feedback');
  await until(()=>evaluate("(()=>{const r=document.querySelector('.co-revision-feedback textarea').getBoundingClientRect();return r.top>=0 && r.bottom<=document.querySelector('.co-main').getBoundingClientRect().bottom;})()"),'Mobile refinement stays above the navigation');
  await useCurrentDraft('Keep draft and close');
  assert.equal((await dashboard()).data.measured.length,4,'Following the revision does not authorize a model request');
  // A fresh browser session on the following day must find durable work, not replay its old goal.
  await evaluate("Object.keys(sessionStorage).filter(k=>k.startsWith('hirezero-goal-start-v1:')).forEach(k=>sessionStorage.removeItem(k))");
  const tomorrow = Date.now()+24*60*60*1000;
  const nextDayClock = await send('Page.addScriptToEvaluateOnNewDocument',{source:`Date.now=()=>${tomorrow}`});
  await send('Page.navigate',{url:url+'/'});
  await until(()=>evaluate("!!document.querySelector('.co-team-note')"),'the employee note leads Home the next day');
  assert.equal(await evaluate("!!document.querySelector('.co-goal-start textarea')"),false,'A fresh session does not prefill the completed request');
  assert.ok(await evaluate("document.querySelector('[aria-label=\"Recent results\"] time')?.dateTime"),'Saved results show their update time');
  await capture('return-next-day-mobile');
  await evaluate("[...document.querySelectorAll('button')].find(b=>b.textContent.startsWith('Find all ')).click()");
  await until(()=>evaluate("!!document.querySelector('.co-home-search input')"),'saved-work search');
  await evaluate("(()=>{const el=document.querySelector('.co-home-search input');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(el,'Fictional revised accessibility draft');el.dispatchEvent(new Event('input',{bubbles:true}));})()");
  await until(()=>evaluate("document.querySelectorAll('[aria-label=\"Recent results\"] li').length===1"),'older matching result');
  await evaluate("document.querySelector('[aria-label=\"Recent results\"] .co-home-row').click()");
  await until(()=>evaluate(`new URLSearchParams(location.search).get('work')===${JSON.stringify(task.id)}`),'the older result opens directly');
  await send('Page.removeScriptToEvaluateOnNewDocument',{identifier:nextDayClock.identifier});
  await send('Page.navigate',{url:url+'/'});
  await text('What do you want to happen next?');
  await evaluate("(()=>{const i=document.querySelector('#co-outcome-input');const set=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;set.call(i,'Promote the spring open day');i.dispatchEvent(new Event('input',{bubbles:true}));i.form.requestSubmit();})()");
  await until(()=>evaluate("document.activeElement===document.querySelector('.co-goal-start textarea')&&document.activeElement.value==='Promote the spring open day'"),'a new outcome fills and focuses the goal');
  await evaluate("(()=>{const t=document.querySelector('.co-goal-start textarea');const set=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set;set.call(t,'');t.dispatchEvent(new Event('input',{bubbles:true}));})()");
  assert.equal(await evaluate("document.querySelector('.co-goal-start textarea').value"),'','Yesterday\'s goal is not reused');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'next-task terms loaded');
  assert.equal(await evaluate("document.querySelector('.co-goal-start .co-check input').checked"),false,'A new task needs fresh consent');
  await setField('Your goal','Write one welcome email with a practical checklist for returning paper-kit subscribers.');
  await send('Page.reload');
  await until(()=>evaluate("document.querySelector('.co-goal-start textarea')?.value==='Write one welcome email with a practical checklist for returning paper-kit subscribers.'"),'unfinished next-task text survives reload');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'reloaded next-task terms');
  assert.equal(await evaluate("document.querySelector('.co-goal-start .co-check input').checked"),false,'Reload cannot retain run consent');
  const employeeCount=(await dashboard()).data.employees.length;
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  await click('Generate draft');
  await text('Fictional welcome email');
  state=(await dashboard()).data;
  assert.equal(state.measured.length,5,'The next task dispatches exactly once after its own consent');
  assert.equal(state.employees.length,employeeCount,'The next task reuses the existing specialist');
  const nextTask=state.tasks.find(t=>t.input.includes('Write one welcome email with a practical checklist for returning paper-kit subscribers.'));
  assert.ok(nextTask);
  const nextResult=state.artifacts.find(a=>a.taskId===nextTask.id);
  const pendingNext=await command('artifact.revise',{id:nextResult.id,expectedVersion:nextResult.version,digest:nextResult.digest,body:'Keep the facts and make the checklist shorter.'});
  await evaluate("Object.keys(sessionStorage).filter(k=>k.startsWith('hirezero-goal-start-v1:')).forEach(k=>sessionStorage.removeItem(k))");
  await send('Page.navigate',{url:url+'/'});
  await text('Revision saved — review before running');
  await evaluate("document.querySelector('[aria-label=\"Open requests\"] .co-home-row').click()");
  await until(()=>evaluate(`new URLSearchParams(location.search).get('work')===${JSON.stringify(pendingNext.id)}`),'A fresh session resumes its unrun revision from Home');
  assert.equal((await dashboard()).data.measured.length,5,'Returning to an unfinished request does not run it');
  assert.deepEqual(errors,[]);
  await fs.writeFile(path.join(repo,'test-results/goal-start-browser.json'),JSON.stringify({verifiedAt:new Date().toISOString(),freshGoalEntryFirst:true,returningResultsFirst:true,nextDayFreshSession:true,olderResultSearch:true,unfinishedRequestRecovery:true,nextTaskSingleConsent:true,freshMobileInputVisible:true,refineFocusAndReturn:true,termsRecoveryWithoutDispatch:true,singleHomeConsent:true,optionalWebsiteDiscovery:true,bareWebsiteAddress:true,normalWebsiteOneClickRun:true,normalOneClickRun:true,automaticSpecialistSetup:true,lostSaveReplyAndReload:true,exactFirstResultOpened:true,latestResultsFirst:true,currentRevisionFromHome:true,revisionChainAfterReload:true,desktopAndMobile:true,liveModelCalls:0,externalRequests:0},null,2));
  console.log('PASS goal start and return visit: optional website, single consent, next-day saved work, older-result search, pending recovery and the next task. The butler kept the saved work on the tray.');
  await send('Browser.close').catch(()=>{});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  try {
    const failureState = await fetch(url + "/api/company", { signal: AbortSignal.timeout(5000) }).then(r => r.json());
    await fs.writeFile(path.join(repo, "test-results/goal-start-browser-failure.json"), JSON.stringify(failureState, null, 2));
  } catch { /* Retain the original failure if the fixture is already unavailable. */ }
  await fs.writeFile(
    path.join(repo, "test-results/goal-start-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  const stop = async child => {
    if (!child || child.exitCode !== null || child.signalCode !== null) return;
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Owned fixture did not exit; retaining its files.')),10000);
      child.once('exit', () => { clearTimeout(timer); resolve(); });
      child.kill();
    });
  };
  await Promise.all([stop(browser),stop(host)]);
  // Delete only this script's verified temporary root after its own processes exit.
  if (
    path.dirname(temporary) === path.resolve(os.tmpdir()) &&
    path.basename(temporary).startsWith("hirezero-manual-ui-")
  )
    await fs.rm(temporary, {
      recursive: true,
      force: true,
      maxRetries: 5,
      retryDelay: 200,
    });
}
