// Fictional pages pass through the real reader, HTTP routes, store and UI. No external requests or model calls.
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
      HIREZERO_BROWSER_USAGE: "1",
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
  await send("Page.navigate", {
    url: url + "/?view=departments&section=usage",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-usage-settings')"),
    "Monthly usage page",
  );
  const click = clicker(evaluate, until);
  const field = async (label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll('.co-usage label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing field');const el=label.querySelector('input,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(50);
  };
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-usage')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const usage = () => fetch(url + "/api/company/usage").then((r) => r.json());
  const post = (action, fields = {}, expected = 200) =>
    evaluate(
      `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:${JSON.stringify(action)},...${JSON.stringify(fields)}})}).then(async r=>{const text=await r.text();if(r.status!==${expected})throw Error(r.status+' '+text);return r.ok?JSON.parse(text):text;})`,
    );
  const originalPeriod = (await usage()).current.period;
  assert.equal((await usage()).current.jobs, 0);
  await evaluate(
    "document.querySelector('.co-usage-settings summary').click()",
  );
  await field("AI jobs per UTC month", "1");
  await field("Planning tokens per UTC month", "14000");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Save monthly limits').disabled",
    ),
    true,
  );
  await evaluate(
    "document.querySelectorAll('.co-usage-check input')[1].click()",
  );
  await evaluate(
    "window.originalUsageFetch=window.fetch;window.loseUsageSave=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseUsageSave&&body.action==='usage.configure'){window.loseUsageSave=false;await window.originalUsageFetch(input,init);throw new TypeError('Fictional lost limits reply');}return window.originalUsageFetch(input,init);};",
  );
  await click("Save monthly limits");
  await waitText("Monthly limits saved.");
  assert.equal((await usage()).policy.version, 1);
  await send("Page.reload");
  await until(
    () => evaluate("!!document.querySelector('.co-usage-settings')"),
    "Limits reload",
  );
  assert.equal((await usage()).policy.limits.jobs, 1);
  const snapshot = await fetch(url + "/api/company").then((r) => r.json());
  const task = snapshot.data.tasks[0];
  const prepare = () =>
    post("conversation.prepare", {
      employeeId: task.employeeId,
      threadId: task.threadId,
      scope: "employee",
      body: "Draft an internal fictional paper note",
      creditAllowance: 1,
    });
  const run = (proposal, expected = 200) =>
    post(
      "measured.run",
      {
        id: proposal.attempt.id,
        expectedVersion: proposal.attempt.version,
        digest: proposal.attempt.measured.digest,
        decision: "accept-soft-limits",
      },
      expected,
    );
  const first = await prepare();
  assert.equal(
    (await usage()).current.jobs,
    0,
    "Preparation does not consume a job",
  );
  const packet = await fetch(
    url + "/api/company/measured/" + first.attempt.id,
  ).then((r) => r.json());
  assert.equal(packet.usage.policy.limits.jobs, 1);
  await run(first);
  await click("Refresh usage");
  await waitText("0 jobs and 13,970 planning tokens remain");
  let current = await usage();
  assert.equal(current.current.jobs, 1);
  assert.equal(current.current.tokens, 30);
  assert.equal(current.current.heldTokens, 0);
  assert.equal(current.current.reportedUsd, 0.0009);
  assert.equal(current.entries[0].state, "settled");
  const second = await prepare();
  assert.match(await run(second, 409), /Monthly usage/);
  assert.equal((await usage()).current.jobs, 1);
  await evaluate(
    "document.querySelector('.co-usage-settings summary').click()",
  );
  await evaluate(
    "document.querySelectorAll('.co-usage-check input')[0].click()",
  );
  await evaluate(
    "document.querySelectorAll('.co-usage-check input')[1].click()",
  );
  await click("Save monthly limits");
  await waitText("New AI jobs are paused");
  await evaluate(
    "fetch('/__fixture/usage/month',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:'{}'}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json()})",
  );
  await click("Refresh usage");
  await waitText("No AI jobs recorded for this period");
  assert.equal((await usage()).current.jobs, 0);
  const nextMonth = await prepare();
  assert.match(await run(nextMonth, 409), /Monthly usage/);
  await evaluate(
    "document.querySelectorAll('.co-usage-check input')[0].click()",
  );
  await evaluate(
    "document.querySelectorAll('.co-usage-check input')[1].click()",
  );
  await click("Save monthly limits");
  await waitText("Monthly limits saved.");
  await run(nextMonth);
  await click("Refresh usage");
  await waitText("0 jobs and 13,970 planning tokens remain");
  await field("Usage period", originalPeriod);
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-usage-entries')?.innerText.includes('0.0009')",
      ),
    "Earlier month receipt",
  );
  assert.equal(
    await evaluate("document.querySelectorAll('.co-usage-entries li').length"),
    1,
  );
  await click("Open work");
  await until(
    () =>
      evaluate(
        "location.search.includes('work=') && !document.querySelector('.co-usage')",
      ),
    "Usage receipt opens its work",
  );
  await send("Page.navigate", {
    url: url + "/?view=departments&section=usage",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-usage-entries')"),
    "Usage return",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate("document.querySelector('.co-usage').scrollIntoView()");
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/usage-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/usage-browser.json"),
    JSON.stringify(
      {
        explicitLimitConsent: true,
        lostResponseReconciledOnce: true,
        preparationUncharged: true,
        monthlyLimitEnforced: true,
        pauseSurvivesRollover: true,
        nextMonthRenewsAllowance: true,
        earlierReceiptPreserved: true,
        costSourceClear: true,
        directWorkLink: true,
        mobileOverflow: false,
        liveModelCalls: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS usage: counted, bounded and carried forward; the butler never misplaced a bill.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/usage-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  browser?.kill();
  host.kill();
  await delay(400);
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
