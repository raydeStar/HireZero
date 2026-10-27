// Company-owned fictional keys pass through real HTTP routes and UI. The provider handler cannot use the network.
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

      HIREZERO_BROWSER_COMPANY_KEY: "1",
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
  until.diagnose = async () =>
    (await pageDiagnosis(evaluate)()) +
    "\n" +
    (await evaluate(
      "document.querySelector('.co-model-connection')?.innerText",
    ));
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  const click = clicker(evaluate, until);
  let companyId = "";
  const api = async (
    route,
    body,
    method = body === undefined ? "GET" : "POST",
    expected = 200,
  ) => {
    const response = await fetch(url + "/api" + route, {
      method,
      headers: {
        Origin: url,
        "X-Dev-Request": "1",
        "Content-Type": "application/json",
        ...(companyId ? { "X-HireZero-Company": companyId } : {}),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    const value = await response.json();
    assert.equal(response.status, expected, JSON.stringify(value));
    return value;
  };
  const command = (action, fields = {}, expected = 200) =>
    api(
      "/company/commands",
      { requestId: crypto.randomUUID(), action, ...fields },
      "POST",
      expected,
    );
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-model-connection')?.textContent.replace(/\\s+/g, ' ').includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const page = () =>
    url +
    "/?view=departments&section=integrations" +
    (companyId ? "&company=" + companyId : "");
  const stats = () => api("/fixture/company-model");
  const field = async (label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll('.co-model-connection label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing field');const el=label.querySelector('input,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(50);
  };
  const screenshot = async (name) => {
    await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
    await fs.writeFile(
      path.join(repo, "test-results", name),
      Buffer.from(
        (await send("Page.captureScreenshot", { format: "png" })).data,
        "base64",
      ),
    );
  };
  await send("Page.navigate", { url: page() });
  await waitText("Host connection");
  await field("Model", "openai/fictional-luna");
  await field("API key", "fictional-first-key");
  await evaluate(
    "document.querySelector('.co-model-analysis summary').click()",
  );
  await field("Analysis model", "openai/fictional-sol");
  assert.equal(
    await evaluate(
      `[...document.querySelectorAll('.co-model-connection button')].find(b => b.textContent === 'Save company connection').disabled`,
    ),
    true,
  );
  await evaluate(`document.querySelector('.co-model-consent input').click()`);
  await evaluate(
    `document.querySelector('.co-model-connection').scrollIntoView()`,
  );
  await screenshot("company-model-review-desktop.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))",
  );
  await evaluate(
    "document.querySelector('.co-model-analysis').scrollIntoView({behavior: 'instant', block: 'start'})",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-model-analysis').getBoundingClientRect().top < 220",
      ),
    "analysis option visible at mobile width",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await screenshot("company-model-analysis-mobile.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  assert.equal((await stats()).calls, 0);
  await click("Save company connection");
  await waitText("Company key saved.");
  assert.equal(
    await evaluate(
      `document.querySelector('.co-model-connection input[type=password]').value`,
    ),
    "",
  );
  assert.equal((await stats()).calls, 0);
  const firstState = await api("/company/model-connection");
  assert.equal(firstState.version, 1);
  assert.equal(firstState.analysisRoute, "openai/fictional-sol");
  assert.ok(!JSON.stringify(firstState).includes("fictional-first-key"));
  await send("Page.reload");
  await waitText("Company API key");
  assert.equal(
    await evaluate(
      `document.querySelector('.co-model-connection input[type=password]').value`,
    ),
    "",
  );
  assert.equal(
    await evaluate(
      `Object.values({...localStorage, ...sessionStorage}).some(v => v.includes('fictional-first-key'))`,
    ),
    false,
  );
  const dashboard = (await api("/company")).data;
  const task = dashboard.tasks[0];
  const old = await command("measured.prepare", {
    id: task.id,
    expectedVersion: task.version,
    creditAllowance: 100,
  });
  assert.equal((await stats()).calls, 0);
  await field("Model", "openai/fictional-luna");
  await field("API key", "fictional-replacement-key");
  await evaluate(`document.querySelector('.co-model-consent input').click()`);
  await evaluate(
    `(() => { const original = window.fetch; window.__lostModelSave = 0; window.fetch = async (...args) => { if (args[0] === '/api/company/model-connection' && args[1]?.method === 'PUT' && !window.__lostModelSave) { window.__lostModelSave++; await original(...args); throw new Error('Fictional lost save response'); } return original(...args); }; })()`,
  );
  await click("Save company connection");
  await waitText("Check saved connection before making another change.");
  assert.equal(
    await evaluate(
      `document.querySelector('.co-model-connection input[type=password]').value`,
    ),
    "",
  );
  await click("Check saved connection");
  await waitText("Current saved connection loaded.");
  assert.equal((await api("/company/model-connection")).version, 2);
  assert.equal(await evaluate("window.__lostModelSave"), 1);
  const changed = (await api("/company")).data;
  assert.equal(
    changed.measured.find((a) => a.id === old.id).job.state,
    "expired",
  );
  const stale = await fetch(url + "/api/company/commands", {
    method: "POST",
    headers: {
      Origin: url,
      "X-Dev-Request": "1",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      requestId: crypto.randomUUID(),
      action: "measured.run",
      id: old.id,
      expectedVersion: old.version,
      digest: old.measured.digest,
      decision: "accept-soft-limits",
    }),
  });
  assert.equal(stale.ok, false);
  const freshTask = changed.tasks.find((t) => t.id === task.id);
  const fresh = await command("measured.prepare", {
    id: task.id,
    expectedVersion: freshTask.version,
    creditAllowance: 100,
  });
  assert.equal((await stats()).calls, 0);
  await send("Page.navigate", {
    url:
      url +
      `/?view=employee&employee=${task.employeeId}&thread=${task.threadId}&work=${task.id}&mode=work`,
  });
  await click("Review task and context");
  await until(
    () => evaluate("!!document.querySelector('.co-work-window .co-packet')"),
    "first reviewed packet",
  );
  await evaluate(
    "document.querySelector('.co-work-window .co-measured input[type=checkbox]').click()",
  );
  await api(
    "/company/model-connection",
    {
      expectedVersion: 2,
      provider: "openai",
      route: "openai/fictional-luna",
      key: "fictional-third-key",
      analysisRoute: "openai/fictional-sol",
      decision: "use-company-key",
    },
    "PUT",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-work-window .co-measured input[type=number]')",
      ),
    "expired review exposes fresh preparation",
  );
  await evaluate(
    "(() => { const input = document.querySelector('.co-work-window .co-measured input[type=number]'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '100'); input.dispatchEvent(new Event('input', { bubbles: true })); })()",
  );
  await click("Prepare job for review");
  await until(
    () =>
      evaluate(
        "!![...document.querySelectorAll('.co-work-window button')].find(b => b.textContent === 'Review task and context')",
      ),
    "fresh packet ready",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-work-window .co-packet')"),
    false,
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-work-window .co-measured input[type=checkbox]').checked",
    ),
    false,
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-work-window button')].find(b => b.textContent === 'Authorize and run one job').disabled",
    ),
    true,
  );
  await click("Review task and context");
  await until(
    () => evaluate("!!document.querySelector('.co-work-window .co-packet')"),
    "fresh context inspected",
  );
  await evaluate(
    "document.querySelector('.co-work-window .co-measured input[type=checkbox]').click()",
  );
  await click("Authorize and run one job");
  await until(async () => {
    const usage = await api("/company/usage");
    return usage.current.jobs === 1 && usage.current.heldJobs === 0;
  }, "settled fictional provider reply");
  assert.equal((await stats()).calls, 1);
  assert.deepEqual((await stats()).models, ["fictional-luna"]);
  let usage = await api("/company/usage");
  assert.equal(usage.entries[0].funding.source, "company-key");
  assert.equal(usage.entries[0].funding.provider, "openai");
  assert.equal(
    usage.current.companyKeyEstimatedUsd,
    usage.current.estimatedUsd,
  );
  assert.ok(usage.current.companyKeyEstimatedUsd > 0);
  await send("Page.navigate", {
    url: url + "/?view=departments&section=usage",
  });
  await until(
    () =>
      evaluate(
        `document.querySelector('.co-usage')?.innerText.includes('charges your account directly')`,
      ),
    "company funded receipt label",
  );
  const department = await command("department.create", {
    name: "Fictional planning",
  });
  const knowledge = await command("knowledge.save", {
    departmentId: department.id,
    body: JSON.stringify({
      purpose: "Paper workshops",
      mandate: "Draft priorities",
      constraints: "Internal advice only",
    }),
  });
  await command("knowledge.review", {
    id: knowledge.id,
    expectedVersion: knowledge.version,
    digest: knowledge.digest,
    decision: "approve",
  });
  const planner = await command("employee.hire", {
    departmentId: department.id,
    roleId: "ceo",
    name: "Fictional planner",
    responsibility: "Propose internal priorities",
  });
  const planningState = (await api("/company")).data;
  const thread = planningState.threads.find((t) => t.employeeId === planner.id);
  const analysis = await command("task.create", {
    employeeId: planner.id,
    threadId: thread.id,
    skill: "company-priorities",
    body: "Propose three priorities using the supplied fictional facts.",
  });
  const review = await command("measured.prepare", {
    id: analysis.id,
    expectedVersion: analysis.version,
    creditAllowance: 100,
  });
  assert.equal(review.measured.route, "openai/fictional-sol");
  assert.equal((await stats()).calls, 1);
  await send("Page.navigate", {
    url:
      url +
      `/?view=employee&employee=${planner.id}&thread=${thread.id}&work=${analysis.id}&mode=work`,
  });
  await click("Review task and context");
  await until(
    () => evaluate("!!document.querySelector('.co-work-window .co-packet')"),
    "analysis context shown",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-work-window .co-measured')?.textContent.includes('openai/fictional-sol')",
      ),
    "approved analysis model shown in task review",
  );
  await screenshot("company-model-analysis-review.png");
  await evaluate(
    "document.querySelector('.co-work-window .co-measured input[type=checkbox]').click()",
  );
  await click("Authorize and run one job");
  await until(async () => {
    const current = (await api("/company/usage")).current;
    return current.jobs === 2 && current.heldJobs === 0;
  }, "analysis receipt settled");
  assert.deepEqual((await stats()).models, ["fictional-luna", "fictional-sol"]);
  const analysisReceipt = (await api("/company/usage")).entries.find(
    (e) => e.taskId === analysis.id,
  );
  assert.equal(analysisReceipt.route, "openai/fictional-sol");
  assert.equal(analysisReceipt.estimatedUsd, 0.00324);
  const other = await api("/companies", { name: "Fictional second studio" });
  companyId = other.id;
  await send("Page.navigate", { url: page() });
  await waitText("Host connection");
  assert.equal((await api("/company/model-connection")).keyStored, false);
  await field("API provider", "anthropic");
  assert.equal(
    await evaluate("document.querySelector('.co-model-analysis select').value"),
    "",
  );
  await field("Model", "anthropic/fictional-wisp");
  await field("API key", "fictional-second-key");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    `document.querySelector('.co-model-connection form').scrollIntoView()`,
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  const consent = await evaluate(
    `(() => { const label = document.querySelector('.co-model-consent'), input = label.querySelector('input'), text = label.querySelector('span'); const a=input.getBoundingClientRect(), b=text.getBoundingClientRect(); return { top: Math.abs(a.top-b.top), left: b.left-a.right }; })()`,
  );
  assert.ok(consent.top < 12 && consent.left > 0, JSON.stringify(consent));
  await screenshot("company-model-review-mobile.png");
  await evaluate(`document.querySelector('.co-model-consent input').click()`);
  await click("Save company connection");
  await waitText("Company key saved.");
  assert.equal((await api("/company/model-connection")).provider, "anthropic");
  assert.equal((await stats()).calls, 2);
  companyId = "";
  await send("Page.navigate", { url: page() });
  await waitText("openai/fictional-luna");
  await evaluate(
    `document.querySelectorAll('.co-model-consent input')[1].click()`,
  );
  await click("Remove key and use host");
  await waitText("Company key removed.");
  await send("Page.reload");
  await waitText("Host connection");
  assert.equal((await api("/company/model-connection")).keyStored, false);
  usage = await api("/company/usage");
  assert.equal(usage.entries[0].funding.source, "company-key");
  companyId = other.id;
  assert.equal((await api("/company/model-connection")).keyStored, true);
  assert.equal((await stats()).calls, 2);
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/company-model-browser.json"),
    JSON.stringify(
      {
        companies: 2,
        providerCalls: 2,
        exactAnalysisRouting: true,
        routineRoutePreserved: true,
        liveProviderRequests: 0,
        explicitConsent: true,
        noKeyEchoOrBrowserStorage: true,
        lostSaveReadOnlyRecovery: true,
        staleReviewRefused: true,
        freshReviewExecuted: true,
        fundingReceiptRetained: true,
        removalPersists: true,
        mobileOverflow: false,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS company model: two locked drawers, two exact models, and no secret left on the counter.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/company-model-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  const stop = async (child) => {
    if (!child || child.exitCode !== null) return;
    child.kill();
    await Promise.race([
      new Promise((resolve) => child.once("exit", resolve)),
      delay(6000),
    ]);
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
