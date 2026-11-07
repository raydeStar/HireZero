// Runs an installed headless Chrome against a disposable company-local host. No user browser profile or live provider.
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
    },
    stdio: ["ignore", "pipe", "pipe"],
  },
);
let hostLog = "";
host.stdout.on("data", (b) => (hostLog += b));
host.stderr.on("data", (b) => (hostLog += b));
let browser, ws;
const until = waiter();
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "this test host owns the listener",
  );
  await until(async () => {
    const r = await fetch(url + "/health");
    return r.ok;
  }, "fresh host health");
  const health = await fetch(url + "/health").then((r) => r.json());
  assert.equal(health.modelExecution, false);
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
  const port = await until(async () => {
    const text = await fs.readFile(
      path.join(temporary, "browser", "DevToolsActivePort"),
      "utf8",
    );
    return Number(text.split("\n")[0]);
  }, "isolated browser debugger");
  const targets = await fetch(`http://127.0.0.1:${port}/json/list`).then((r) =>
    r.json(),
  );
  const target = targets.find((t) => t.type === "page");
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
    height: 1100,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.addScriptToEvaluateOnNewDocument", { source: fetchTracker });
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: "localStorage.setItem('dev-ui:onboarding','\"form\"')",
  });
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("document.body.innerText.includes('Company conversation')"),
    "company UI",
  );
  const click = clicker(evaluate, until),
    idle = pageIdle(evaluate, until);
  const field = async (label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll('.co-main label')].find(l=>!l.closest('.co-onboarding')&&l.childNodes[0]?.textContent.trim()===${JSON.stringify(label)});if(!label)throw Error('Field missing: '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(70);
  };
  await click("Settings");
  await click("Departments");
  await field("New department", "Fictional Marketing");
  await click("Create department");
  await until(
    () => evaluate("document.body.innerText.includes('Fictional Marketing')"),
    "department persisted",
  );
  const dashboard = () => fetch(url + "/api/company").then((r) => r.json());
  let state = await dashboard();
  const dep = state.data.departments[0].id;
  await click("People");
  await evaluate(
    "[...document.querySelectorAll('details')].find(d=>d.querySelector('summary')?.textContent==='Advanced: hire without the guide').open=true",
  );
  await field("Name", "Strategy fixture");
  await field("Department", dep);
  await field("Responsibility", "Fictional strategy and approved facts");
  await click("Hire employee");
  await until(
    async () => (await dashboard()).data.employees.length === 1,
    "first hire",
  );
  await click("Settings");
  await click("Departments");
  await click("Fictional MarketingOnboarding needed");
  for (const name of [
    "Purpose",
    "Audience",
    "Positioning",
    "Voice",
    "Claims",
    "Constraints",
  ])
    await field(name, "Approved fictional " + name);
  await click("Save draft");
  await until(
    () => evaluate("document.body.innerText.includes('Approve this version')"),
    "knowledge draft",
  );
  await click("Approve this version");
  await until(
    async () => (await dashboard()).data.employees[0].state === "active",
    "onboarding approval",
  );
  await click("People");
  await evaluate(
    "[...document.querySelectorAll('details')].find(d=>d.querySelector('summary')?.textContent==='Advanced: hire without the guide').open=true",
  );
  await field("Name", "Copy fixture");
  await field("Department", dep);
  await field("Responsibility", "Fictional copy drafting");
  await click("Hire employee");
  await until(
    async () => (await dashboard()).data.employees.length === 2,
    "second hire",
  );
  state = await dashboard();
  assert.equal(
    state.data.employees[1].state,
    "active",
    "Second hire inherits approved knowledge",
  );
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-conversations .co-list')].some(b=>b.textContent.includes('Copy fixture'))",
      ),
    "second employee visible in navigation",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-conversations .co-list')].find(b=>b.textContent.includes('Copy fixture')).click()",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('[aria-label=\"Private employee conversation\"]')",
      ),
    "employee drill-down",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('textarea[aria-label=\"Message to Copy fixture\"]:not(:disabled)')",
      ),
    "employee composer loaded",
  );
  await evaluate(
    "(()=>{const input=document.querySelector('textarea[aria-label=\"Message to Copy fixture\"]');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(input,'Prepare fictional copy');input.dispatchEvent(new Event('input',{bubbles:true}));})()",
  );
  await evaluate(
    "document.querySelector('button[aria-label=\"Save message\"]').click()",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.fe-thread').textContent.includes('Prepare fictional copy')",
      ),
    "saved message",
  );
  assert.equal(
    (await dashboard()).data.tasks.length,
    0,
    "Saving a conversation message creates no autonomous work",
  );
  await until(
    () =>
      evaluate(
        "document.body.innerText.includes('will see it next time you ask for a reply.')",
      ),
    "blocked state visible in the UI",
  );
  assert.equal(errors.length, 0, "No uncaught UI exceptions");
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  const desktop = await send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  await fs.writeFile(
    path.join(repo, "test-results/company-desktop.png"),
    Buffer.from(desktop.data, "base64"),
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await delay(250);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth"),
    true,
    "Mobile layout does not overflow",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.fe-topbar').getBoundingClientRect().bottom<=document.querySelector('.co-conversation-context').getBoundingClientRect().top",
    ),
    true,
    "Mobile header and conversation context do not overlap",
  );
  const mobile = await send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  await fs.writeFile(
    path.join(repo, "test-results/company-mobile.png"),
    Buffer.from(mobile.data, "base64"),
  );
  console.log(
    "PASS headless browser: department, first hire, knowledge approval, inherited second hire, employee drill-down, saved-only conversation without a provider, desktop/mobile rendering",
  );
  await send("Browser.close").catch(() => {});
} catch (e) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/company-browser-host.log"),
    hostLog,
  );
  throw e;
} finally {
  ws?.close();
  browser?.kill();
  host.kill();
  await delay(400);
  // Delete only this script's verified mkdtemp root after its own processes have exited.
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
