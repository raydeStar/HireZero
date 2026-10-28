// Fictional memory uses the real owner UI, HTTP routes and store with a fake model. No external requests.
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
const hostDir = path.join(repo, "src/HireZero.Host"),
  url = await freeLocalUrl();
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
    path.join(hostDir, "wwwroot"),
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
      HIREZERO_BROWSER_SYNTHETIC_BUSINESS: "1",
      HIREZERO_BROWSER_DECISIONS: "1",
    },
    stdio: ["ignore", "pipe", "pipe"],
  },
);
let hostLog = "",
  browser,
  ws;
host.stdout.on("data", (b) => (hostLog += b));
host.stderr.on("data", (b) => (hostLog += b));
const until = waiter(),
  errors = [];
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "fixture host listening",
  );
  assert.equal(
    (await fetch(url + "/health").then((r) => r.json())).modelExecution,
    false,
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
    "isolated debugger",
  );
  const target = (
    await fetch(`http://127.0.0.1:${port}/json/list`).then((r) => r.json())
  ).find((t) => t.type === "page");
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    ws.addEventListener("open", resolve, { once: true });
    ws.addEventListener("error", reject, { once: true });
  });
  let sequence = 0;
  const pending = new Map();
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
        reject(new Error("Debugger timed out: " + method));
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
  await send("Runtime.enable");
  await send("Page.enable");
  await send("Page.addScriptToEvaluateOnNewDocument", { source: fetchTracker });
  until.diagnose = pageDiagnosis(evaluate);
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.navigate", { url: url + "/?view=departments&section=integrations" });
  await until(() => evaluate("!!document.querySelector('.co-decisions input')"), "decision workspace");
  const click = clicker(evaluate, until);
  await evaluate("document.querySelector('a[href=\"#company-jev-decisions\"]').click()");
  await until(() => evaluate("document.querySelector('.co-decisions').getBoundingClientRect().top >= 0 && document.querySelector('.co-decisions').getBoundingClientRect().top < innerHeight"), "decision section link");
  const field = async (name, value) => {
    await evaluate(`(()=>{const label=[...document.querySelectorAll('.co-decisions label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(name)}); if(!label)throw Error('Missing field '+${JSON.stringify(name)}); const el=label.querySelector('input,textarea,select'); const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`);
    await delay(30);
  };
  const text = () => evaluate("document.querySelector('.co-decisions')?.innerText || ''");
  const waitText = value => until(async () => (await text()).includes(value), value);
  const calls = () => fetch(url + "/__fixture/decisions").then(r => r.json()).then(r => r.calls);
  const listing = () => fetch(url + "/api/company/decisions").then(r => r.json());
  const model = () => fetch(url + "/api/company/model-connection").then(r => r.json());
  const before = await model();
  assert.equal(before.mode, "host-and-company-key");
  await field("What needs deciding?", "Which next step respects the fictional client's request?");
  await field("Situation and relevant facts", "Fictional bakery: the owner wants a cake FAQ, but the order deadline is missing. Do not invent it.");
  await field("Option 1", "Draft an FAQ and invent a deadline");
  await field("Option 2", "Ask the owner for the missing order deadline");
  await click("Review decision request");
  await waitText("Ready for your approval");
  assert.equal(await calls(), 0);
  assert.match(await text(), /soft limits/);
  assert.equal(await evaluate("[...document.querySelectorAll('.co-decisions button')].find(b=>b.textContent==='Run this decision once').disabled"), true);
  assert.match(await evaluate("document.querySelector('.co-decision-context').innerText"), /order deadline is missing/);
  await evaluate("document.querySelector('.co-decisions article').scrollIntoView({block:'start'})");
  await fs.mkdir(path.join(repo,"test-results"), {recursive:true});
  const reviewShot = await send("Page.captureScreenshot", {format:"png"});
  await fs.writeFile(path.join(repo,"test-results/decisions-review.png"), Buffer.from(reviewShot.data,"base64"));
  await evaluate("[...document.querySelectorAll('.co-decisions details')].find(d=>d.querySelector('summary')?.textContent==='Exact data sent to OpenRouter').open=true");
  assert.match(await evaluate("document.querySelector('.co-decisions pre').textContent"), /order deadline is missing/);
  await evaluate("[...document.querySelectorAll('.co-decisions article input[type=checkbox]')].forEach(c=>c.click())");
  // Lose the command reply and its first status read after the server has saved the result.
  await evaluate(`(()=>{const original=window.fetch;window.fetch=async (resource,init)=>{const path=String(resource);if(path==='/api/company/commands'&&init?.body&&JSON.parse(init.body).action==='decision.run'){await original(resource,init);throw new TypeError('Fictional lost response');}if(path.startsWith('/api/company/operations/'))throw new TypeError('Fictional status outage');return original(resource,init);};})()`);
  await click("Run this decision once");
  await waitText("Check saved decision request");
  assert.equal(await calls(), 1);
  await send("Page.reload");
  await waitText("Check saved decision request");
  await click("Check saved decision request");
  await waitText("Recommendation ready");
  assert.match(await text(), /Recommendation: Ask the owner for the missing order deadline/);
  assert.equal(await calls(), 1);
  assert.equal((await listing()).items.length, 1);
  // A denied receipt write must stop before even preparing a second request.
  await evaluate("window.fixtureSetItem=Storage.prototype.setItem;Storage.prototype.setItem=function(key,value){if(key.includes('hirezero-decision-request'))throw new Error('Fictional storage denial');return window.fixtureSetItem.call(this,key,value)};[...document.querySelectorAll('.co-decisions details')].find(d=>d.querySelector('summary')?.textContent==='Prepare a decision').open=true");
  await field("What needs deciding?", "Which task fits this fictional brief?");
  await field("Situation and relevant facts", "Fictional request with two clear options.");
  await field("Option 1", "Gather more information");
  await field("Option 2", "Ask the owner");
  await click("Review decision request");
  await waitText("Fictional storage denial");
  assert.equal((await listing()).items.length, 1);
  await evaluate("Storage.prototype.setItem=window.fixtureSetItem");
  const modeResponse = await fetch(url + "/__fixture/decision-mode", {method:"POST",headers:{"Content-Type":"application/json","Origin":url,"X-Dev-Request":"1"},body:JSON.stringify({unknown:true})});
  assert.equal(modeResponse.status, 200, "The fictional provider must enter incomplete-usage mode");
  await click("Review decision request");
  await waitText("Ready for your approval");
  await evaluate("[...document.querySelectorAll('.co-decisions article input[type=checkbox]')].forEach(c=>c.click())");
  await click("Run this decision once");
  await waitText("Usage needs confirmation");
  assert.match(await text(), /Reported usage: unknown/);
  assert.equal(await calls(), 2);
  await evaluate("[...document.querySelectorAll('.co-decisions details')].find(d=>d.querySelector('summary')?.textContent==='Reconcile provider usage').open=true");
  await field("Input tokens", "100"); await field("Output tokens", "20");
  await field("Actual charge (USD)", "0.00002");
  await field("Evidence checked", "Fictional provider receipt and termination verified.");
  await evaluate("document.querySelector('.co-decisions article input[type=checkbox]').click()");
  await click("Record confirmed usage");
  await waitText("Usage reconciled");
  assert.equal(await calls(), 2);
  const after = await model();
  assert.deepEqual(after, before, "Decision work must not change text model routing or funding");
  const usage = await fetch(url + "/api/company/usage").then(r => r.json());
  assert.equal(usage.current.heldJobs, 0);
  await send("Emulation.setDeviceMetricsOverride", {width:390,height:844,deviceScaleFactor:1,mobile:true});
  await delay(120);
  assert.equal(await evaluate("document.documentElement.scrollWidth > innerWidth + 1"), false);
  await evaluate("document.querySelector('.co-decisions article').scrollIntoView({block:'start'})");
  await delay(100);
  await fs.mkdir(path.join(repo,"test-results"), {recursive:true});
  const screenshot = await send("Page.captureScreenshot", {format:"png"});
  await fs.writeFile(path.join(repo,"test-results/decisions-mobile.png"), Buffer.from(screenshot.data,"base64"));
  assert.deepEqual(errors, []);
  await fs.writeFile(path.join(repo,"test-results/decisions-browser.json"), JSON.stringify({
    passed:true, exactReview:true, lostReplyRecoveredWithoutReplay:true, deniedStoragePreventsDispatch:true,
    unknownUsageReconciled:true, textRoutingUnchanged:true, mobileOverflow:false, fictionalDecisionCalls:2, liveProviderCalls:0
  },null,2));
  console.log("PASS decisions: one question, one receipt; the butler resists the encore.");
  await send("Browser.close").catch(()=>{});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/decisions-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  const stop = async (child) => {
    if (!child || child.exitCode !== null || child.signalCode !== null) return;
    const exited = new Promise((resolve) => child.once("exit", resolve));
    child.kill();
    await Promise.race([exited, delay(6000)]);
    if (child.exitCode === null && child.signalCode === null)
      throw new Error(
        "Owned fixture process has not exited; preserve its temporary files.",
      );
  };
  await stop(browser);
  await stop(host);
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
