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
    if (m.method === "Runtime.exceptionThrown")
      errors.push(m.params.exceptionDetails.text);
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
  until.diagnose = pageDiagnosis(evaluate);
  await send("Runtime.enable");
  await send("Page.enable");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.addScriptToEvaluateOnNewDocument", { source: fetchTracker });
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("!!document.querySelector('.co-home')"),
    "company home",
  );
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
  assert.equal(await evaluate("!!document.querySelector('[aria-label=\"Getting started\"]')"),false);
  await setField('What does your business do?','Fictional team studio sells paper craft kits.');
  await setField('Your goal','Improve website accessibility');
  await setField('Website (optional)','http://paper.example.invalid/');
  assert.equal(await evaluate("document.querySelector('.co-goal-start input[type=url]').validity.valid"),false,'Only supported https addresses can be submitted');
  await setField('Website (optional)','https://paper.example.invalid/');
  await text('Your first technical plan');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'run terms on Home');
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  await fs.mkdir(path.join(repo,'test-results'),{recursive:true});
  const capture=async name=>fs.writeFile(path.join(repo,'test-results/'+name+'.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false})).data,'base64'));
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
  const packet = await fetch(url+'/api/company/measured/'+state.measured[0].id).then(r=>r.json());
  assert.match(packet.call.context.websiteResearch.sources[0].text,/folding guide/);
  assert.equal(packet.call.context.businessBrief.fields.find(f=>f.key==='services').verification,'unknown');
  assert.equal(state.measured.length,1,'Only one model run after a lost reply');
  assert.equal(await evaluate(`document.querySelector('[data-artifact-id="${result.id}"]')?.open===true`),true);
  await capture('goal-first-result');
  await command('artifact.create',{id:task.id,expectedVersion:state.tasks[0].version,body:'Fictional revised accessibility draft.'});
  await send('Page.navigate',{url:url+'/'});
  await text('Your results');
  assert.equal(await evaluate("document.querySelectorAll('[aria-label=\"Recent results\"] li').length"),1);
  assert.equal(await evaluate("document.querySelector('.co-home').firstElementChild.getAttribute('aria-label')"),'Recent results');
  await text('Fictional revised accessibility draft.');
  await click('Start another goal');
  await setField('Your goal','Improve keyboard navigation.');
  await setField('Website (optional)','');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'next-run terms');
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  await click('Generate my first draft');
  await text('Fictional technical plan');
  state=(await dashboard()).data;
  assert.equal(state.tasks.length,2);
  assert.equal(state.measured.length,2);
  assert.equal(state.artifacts.filter(a=>a.taskId!==task.id).length,1,'Normal single-click path saves the first raw result');
  await send('Page.navigate',{url:url+'/'});
  await text('Start another goal');
  await click('Start another goal');
  await setField('Your goal','Improve website copy for our paper kits.');
  await text('Your marketing draft');
  await setField('Your goal','Write one welcome email for new paper-kit subscribers.');
  await text('Your marketing draft');
  await until(()=>evaluate("!!document.querySelector('.co-goal-start .co-check input')"),'marketing-run terms');
  await evaluate("document.querySelector('.co-goal-start .co-check input').click()");
  await click('Generate my first draft');
  await text('Fictional welcome email');
  state=(await dashboard()).data;
  const emailTask=state.tasks.find(t=>t.skill==='marketing-goal-draft');
  assert.ok(emailTask,'A requested email uses direct Marketing drafting');
  assert.equal(state.measured.length,3,'One run per consent, including a change of specialist');
  assert.equal(state.artifacts.filter(a=>a.taskId===emailTask.id).length,1);
  assert.doesNotMatch(emailTask.input,/Monday|Wednesday|Friday/);
  assert.equal(await evaluate(`new URLSearchParams(location.search).get('work')===${JSON.stringify(emailTask.id)}`),true);
  assert.deepEqual(errors,[]);
  await fs.writeFile(path.join(repo,'test-results/goal-start-browser.json'),JSON.stringify({verifiedAt:new Date().toISOString(),singleHomeConsent:true,optionalWebsiteDiscovery:true,normalOneClickRun:true,automaticSpecialistSetup:true,lostSaveReplyAndReload:true,exactFirstResultOpened:true,latestResultsFirst:true,desktopAndMobile:true,liveModelCalls:0,externalRequests:0},null,2));
  console.log('PASS goal start: optional website, automatic setup, single Home consent and a requested email instead of a weekly plan. The butler handled the paperwork.');
  await send('Browser.close').catch(()=>{});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
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
