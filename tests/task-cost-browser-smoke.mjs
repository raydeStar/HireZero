// Owner cost reviews and Progress totals use fictional execution; no external work occurs.
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { spawn } from "node:child_process";
import assert from "node:assert/strict";
import { buildPilotReport } from "../tools/pilot-report.mjs";
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
  until.diagnose = async () =>
    (await pageDiagnosis(evaluate)()) +
    "\n" +
    (await evaluate("document.body?.innerText"));
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  const click = clicker(evaluate, until);
  let companyId = "";
  const api = async (route, body, expected = 200) => {
    const response = await fetch(url + "/api" + route, {
      method: body === undefined ? "GET" : "POST",
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
      expected,
    );
  const field = async (scope, text, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll(${JSON.stringify(scope + " label")})].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(text)});if(!label)throw Error('Missing field: '+${JSON.stringify(text)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(60);
  };
  const check = (text) =>
    evaluate(
      `(()=>{const label=[...document.querySelectorAll('label')].find(l=>l.textContent.includes(${JSON.stringify(text)}));if(!label)throw Error('Missing checkbox');label.querySelector('input[type=checkbox]').click();})()`,
    );
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.body.textContent.replace(/\\s+/g, ' ').includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const compute = () => api("/company/compute");
  const state = () => fetch(url + "/__fixture/browser").then((r) => r.json());
  const capture = async (name) => {
    await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
    await fs.writeFile(
      path.join(repo, "test-results", name),
      Buffer.from(
        (await send("Page.captureScreenshot", { format: "png" })).data,
        "base64",
      ),
    );
  };

  const task = (await api("/company")).data.tasks[0];
  const prepared = await command("measured.prepare", {
    id: task.id,
    expectedVersion: task.version,
    creditAllowance: 1,
  });
  await command("measured.run", {
    id: prepared.id,
    expectedVersion: prepared.version,
    digest: prepared.measured.digest,
    decision: "accept-soft-limits",
  });
  const workUrl =
    url +
    `/?view=employee&employee=${task.employeeId}&thread=${task.threadId}&work=${task.id}&mode=work`;
  await send("Page.navigate", { url: workUrl });
  await click("Approve result");
  await click("Complete after review");
  await until(
    async () =>
      (await api("/company/tasks/" + task.id)).task.state === "completed",
    "completed work",
  );
  const costs = () => api("/company/tasks/" + task.id + "/costs");
  const open = async () => {
    await until(
      () => evaluate("!!document.querySelector('.co-task-costs summary')"),
      "cost panel",
    );
    await evaluate("document.querySelector('.co-task-costs').open=true");
    await until(
      () => evaluate("!!document.querySelector('.co-task-costs textarea')"),
      "cost evidence",
    );
  };
  const costsField = (name, value) => field(".co-task-costs", name, value);
  const progressUrl = url + "/?view=departments&section=progress";
  await send("Page.navigate", { url: progressUrl });
  await waitText("Current cost totals: 0 of 1 completed task.");
  assert.equal(
    (await api("/company/progress")).taskCosts.averageUsdPerCompletion,
    null,
  );
  await click("Open next task needing cost review");
  assert.equal(
    await evaluate("new URLSearchParams(location.search).get('mode')"),
    "work",
  );
  await open();
  assert.equal((await costs()).reportedModelUsd, 0.0009);
  assert.equal((await costs()).reconciledTotalUsd, null);
  await costsField("Compute", "0.12");
  await costsField(
    "Receipts and allocation basis",
    "Fictional hosting receipt, allocated to this completed task. Other charges verified zero.",
  );
  await send("Page.reload");
  await open();
  assert.equal(
    await evaluate(
      "document.querySelector('.co-task-costs input[type=number]').value",
    ),
    "0.12",
  );
  await evaluate(
    "window.costFetch=window.fetch;window.loseCostSave=true;window.fetch=async(input,init)=>{const b=init?.body?JSON.parse(init.body):{};if(window.loseCostSave&&b.action==='cost.save'){window.loseCostSave=false;await window.costFetch(input,init);throw new TypeError('Fictional lost save reply');}return window.costFetch(input,init);};",
  );
  await click("Save cost draft");
  await waitText("Cost draft saved.");
  assert.equal((await costs()).reviews.length, 1);
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-task-costs button')].find(b=>b.textContent==='Record task costs').disabled",
    ),
    true,
  );
  for (const label of [
    "Tools and external services",
    "Retrieval",
    "Storage",
    "Operating overhead",
  ])
    await costsField(label, "0");
  await click("Save cost draft");
  await until(
    async () => (await costs()).reviews[0].version === 2,
    "saved known zero costs",
  );
  await check("I confirm these are this task's actual");
  // A confirmed write followed by a failed refresh must keep its receipt for recovery.
  await evaluate(
    "window.costFetch2=window.fetch;window.failCostRefresh=false;window.fetch=async(input,init)=>{const b=init?.body?JSON.parse(init.body):{};if(b.action==='cost.review'){const r=await window.costFetch2(input,init);window.failCostRefresh=true;return r;}if(window.failCostRefresh&&String(input).endsWith('/costs'))throw new TypeError('Fictional failed cost refresh');return window.costFetch2(input,init);};",
  );
  await click("Record task costs");
  await waitText("Check cost receipt");
  assert.equal((await costs()).reviews[0].state, "recorded");
  await send("Page.reload");
  await open();
  await click("Check cost receipt");
  await waitText("Task costs recorded.");
  assert.equal((await costs()).reviews.length, 1);
  assert.equal((await costs()).reconciledTotalUsd, 0.1209);
  await evaluate(
    "document.querySelector('.co-task-costs').scrollIntoView({block:'start'})",
  );
  await capture("task-cost-desktop.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await costsField("Storage", "0.03");
  await click("Refresh task costs");
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-task-costs input[type=number]')[3].value",
    ),
    "0.03",
  );
  await click("Save cost draft");
  await until(
    async () => (await costs()).reviews.length === 2,
    "correction draft",
  );
  await check("I confirm these are this task's actual");
  await click("Record task costs");
  await waitText("Task costs recorded.");
  const final = await costs();
  assert.equal(final.reconciledTotalUsd, 0.1509);
  assert.equal(final.reviews.filter((r) => r.state === "recorded").length, 2);
  assert.equal(
    final.reviews[1].costs.find((c) => c.category === "storage").usd,
    0,
  );
  await evaluate(
    "document.querySelector('.co-task-costs').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await capture("task-cost-mobile.png");
  await evaluate(
    "[...document.querySelectorAll('.co-task-costs summary')].find(s=>s.textContent==='Recorded cost history').click()",
  );
  await waitText("Record 2");
  await evaluate(
    "[...document.querySelectorAll('.co-task-costs summary')].find(s=>s.textContent==='Recorded cost history').scrollIntoView({block:'start'})",
  );
  await capture("task-cost-history-mobile.png");
  await evaluate(
    "window.costDownload=null;URL.createObjectURL=(blob)=>{blob.text().then(text=>window.costDownload=JSON.parse(text));return 'blob:fixture';};HTMLAnchorElement.prototype.click=function(){};",
  );
  await click("Download task cost report");
  await until(() => evaluate("!!window.costDownload"), "cost export");
  assert.equal(
    await evaluate("window.costDownload.reconciledTotalUsd"),
    0.1509,
  );
  assert.equal(await evaluate("window.costDownload.reviews.length"), 2);
  await send("Page.navigate", { url: progressUrl });
  await waitText("Current cost totals: 1 of 1 completed task.");
  const progress = await api("/company/progress");
  assert.equal(
    progress.taskCosts.reconciledSubtotalUsd,
    final.reconciledTotalUsd,
  );
  assert.equal(
    progress.taskCosts.averageUsdPerCompletion,
    final.reconciledTotalUsd,
  );
  assert.equal(progress.taskCosts.nextReview, null);
  const pilot = buildPilotReport(
    {
      schema: "hirezero-pilot-v1",
      cohort: "fictional-browser-fixture",
      targetParticipants: 10,
      participants: [
        {
          id: "P01",
          evidence: "fictional",
          progressFile: "progress.json",
          observation: {
            observedAt: null,
            correctBusiness: null,
            assistance: "unobserved",
            firstDeliverable: "unobserved",
            firstTaskId: null,
            followUpAt: null,
            repeatUsefulWork: null,
            repeatEvidence: null,
            payment: "unobserved",
            paymentReceiptReviewed: false,
          },
        },
      ],
    },
    new Map([["progress.json", progress]]),
  );
  assert.equal(
    pilot.evidenceGroups.fictional.completedTaskCosts.averageUsdPerCompletion,
    0.1509,
  );
  assert.equal(pilot.evidenceGroups.customer.participants, 0);
  await evaluate(
    "document.querySelector('.co-progress-task-costs').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await capture("progress-task-cost-mobile.png");
  await evaluate(
    "document.querySelector('.co-progress-task-costs details').open=true",
  );
  await waitText("Provider-reported model costs");
  await capture("progress-task-cost-breakdown-mobile.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await evaluate(
    "document.querySelector('.co-progress-task-costs').scrollIntoView({block:'start'})",
  );
  await capture("progress-task-cost-desktop.png");
  await evaluate(
    "window.progressDownload=null;URL.createObjectURL=(blob)=>{blob.text().then(text=>window.progressDownload=JSON.parse(text));return 'blob:fixture';};HTMLAnchorElement.prototype.click=function(){};",
  );
  await click("Download metrics");
  await until(
    () => evaluate("!!window.progressDownload"),
    "progress cost export",
  );
  assert.equal(
    await evaluate("window.progressDownload.taskCosts.averageUsdPerCompletion"),
    0.1509,
  );
  assert.equal(
    await evaluate(
      "JSON.stringify(window.progressDownload).includes('Fictional hosting receipt')",
    ),
    false,
  );
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/task-cost-browser.json"),
    JSON.stringify(
      {
        completedWork: true,
        unknownCostsStayBlank: true,
        unsavedReload: true,
        lostSaveRecovery: true,
        failedRefreshReceiptRecovery: true,
        explicitOwnerRecording: true,
        immutableCorrectionHistory: true,
        mobileOverflow: false,
        exportedTotal: 0.1509,
        progressCoverageAndUnknowns: true,
        progressLinksToMissingReview: true,
        progressExportedTotal: 0.1509,
        pilotReportUsesRealExportShape: true,
        fictionalPilotEvidenceSeparate: true,
        liveModelCalls: 0,
        customerCharges: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS task costs: unknown stays unknown, corrections retain history, and the butler keeps the receipt.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/task-cost-browser-host.log"),
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
