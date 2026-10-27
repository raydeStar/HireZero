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
      HIREZERO_BROWSER_SANDBOX: "1",
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
  const click = clicker(evaluate, until);
  const field = async (scope, label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll(${JSON.stringify(scope + " label")})].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing field: '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(60);
  };
  const check = async (label) =>
    evaluate(
      `(()=>{const l=[...document.querySelectorAll('label')].find(l=>l.textContent.includes(${JSON.stringify(label)}));if(!l)throw Error('Missing checkbox');l.querySelector('input[type=checkbox]').click();})()`,
    );
  const detail = (id) =>
    fetch(url + "/api/company/tasks/" + id).then((r) => r.json());
  const compute = () =>
    fetch(url + "/api/company/compute").then((r) => r.json());
  const state = () => fetch(url + "/__fixture/sandbox").then((r) => r.json());
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-sandbox')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const snapshot = await fetch(url + "/api/company").then((r) => r.json());
  const task = snapshot.data.tasks[0];
  const workUrl =
    url +
    `/?view=employee&employee=${task.employeeId}&thread=${task.threadId}&work=${task.id}&mode=work`;
  async function work() {
    await send("Page.navigate", { url: workUrl });
    await until(
      () =>
        evaluate(
          "!!document.querySelector('.co-work-window .co-sandbox .co-sandbox-allowance')",
        ),
      "script tools loaded",
    );
    await evaluate(
      "document.querySelector('.co-work-window .co-sandbox').open=true",
    );
  }
  async function limits(paused) {
    await send("Page.navigate", {
      url: url + "/?view=departments&section=usage",
    });
    await until(
      () => evaluate("!!document.querySelector('.co-compute-settings')"),
      "script limits",
    );
    await evaluate("document.querySelector('.co-compute-settings').open=true");
    await field(".co-compute", "Compute jobs per UTC month", "3");
    await field(".co-compute", "Allocated compute seconds per UTC month", "60");
    const current = await evaluate(
      "document.querySelector('.co-compute-settings input[type=checkbox]').checked",
    );
    if (current !== paused) await check("Pause compute execution");
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('button')].find(b=>b.textContent==='Save compute limits').disabled",
      ),
      true,
    );
    await check("I reviewed these compute limits.");
    await click("Save compute limits");
    await until(
      () =>
        evaluate(
          "document.querySelector('.co-compute').innerText.includes('Compute limits saved.')",
        ),
      "limits saved",
    );
  }
  async function prepare() {
    await evaluate(
      "[...document.querySelectorAll('.co-work-window .co-sandbox > details')].find(d=>d.querySelector('summary')?.textContent==='Prepare a script').open=true",
    );
    await check("Fictional campaign rows");
    await click("Use row-count example");
    await field(".co-work-window .co-sandbox", "Maximum script seconds", "5");
    await click("Prepare exact review");
    await until(
      async () =>
        (await detail(task.id)).sandboxes.some((j) => j.state === "proposed"),
      "saved exact review",
    );
    await until(
      () =>
        evaluate(
          "[...document.querySelectorAll('.co-work-window button')].find(b=>b.textContent==='Approve and run once')?.disabled === true",
        ),
      "exact review rendered before consent",
    );
    await until(
      () =>
        evaluate(
          "document.activeElement?.classList.contains('co-sandbox-job') && ![...document.querySelectorAll('.co-work-window .co-sandbox > details')].find(d=>d.querySelector('summary')?.textContent==='Prepare a script').open",
        ),
      "prepared review receives focus and editor closes",
    );
  }
  await limits(false);
  await work();
  await prepare();
  assert.equal((await state()).executions, 0);
  const proposal = (await detail(task.id)).sandboxes.at(-1);
  assert.equal(proposal.request.inputs.length, 1);
  assert.equal(proposal.request.inputs[0].text, "campaign,leads\nPaper,3");
  await check("I reviewed this script, its files and limits.");
  await evaluate(
    "window.originalSandboxFetch=window.fetch;window.loseSandboxRun=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseSandboxRun&&body.action==='sandbox.run'){window.loseSandboxRun=false;await window.originalSandboxFetch(input,init);throw new TypeError('Fictional lost approval receipt');}return window.originalSandboxFetch(input,init);};",
  );
  await click("Approve and run once");
  await waitText("Resource release is unconfirmed.");
  await waitText("Fictional report from 1 selected input files.");
  assert.equal((await state()).executions, 1);
  assert.equal((await compute()).usage.heldSeconds, 5);
  await limits(true);
  await evaluate(
    "fetch('/__fixture/sandbox/disable',{method:'POST',headers:{'X-Dev-Request':'1','Content-Type':'application/json'},body:'{}'})",
  );
  await work();
  await waitText("Fictional report from 1 selected input files.");
  await waitText("New script execution is unavailable.");
  assert.equal((await compute()).available, false);
  assert.equal((await compute()).recoveryAvailable, true);
  await click("Check resource and recover");
  await waitText("Temporary resources released");
  assert.equal((await state()).executions, 1);
  assert.equal((await compute()).usage.heldSeconds, 0);
  await evaluate(
    "fetch('/__fixture/sandbox/enable',{method:'POST',headers:{'X-Dev-Request':'1','Content-Type':'application/json'},body:'{}'})",
  );
  await limits(false);
  await work();
  await evaluate(
    "fetch('/__fixture/sandbox/wait',{method:'POST',headers:{'X-Dev-Request':'1','Content-Type':'application/json'},body:'{}'})",
  );
  await prepare();
  await check("I reviewed this script, its files and limits.");
  await click("Approve and run once");
  await until(
    async () => (await state()).executions === 2,
    "second execution started",
  );
  await click("Stop script authority", { idle: false });
  await until(
    async () =>
      (await detail(task.id)).sandboxes.some(
        (j) => j.state === "withheld" && j.release?.confirmed,
      ),
    "revoked late output withheld and released",
  );
  await click("Refresh script status");
  await waitText("Output withheld");
  const final = (await detail(task.id)).sandboxes;
  assert.equal(final.length, 2);
  assert.equal(
    final.find((j) => j.state === "withheld").result.outputs.length,
    0,
  );
  assert.equal((await compute()).usage.allocatedSeconds, 10);
  assert.equal((await compute()).usage.heldSeconds, 0);
  assert.equal((await state()).executions, 2);
  await evaluate(
    "fetch('/__fixture/sandbox/unknown-pending',{method:'POST',headers:{'X-Dev-Request':'1','Content-Type':'application/json'},body:'{}'})",
  );
  await prepare();
  await check("I reviewed this script, its files and limits.");
  await click("Approve and run once");
  await waitText(
    "Use Check resource and recover to look for a saved result and confirm cleanup.",
  );
  const uncertain = await until(
    async () =>
      (await detail(task.id)).sandboxes.find((j) => j.state === "unknown"),
    "uncertain script saved without retry",
  );
  assert.equal((await state()).executions, 3);
  assert.equal((await compute()).usage.heldSeconds, 5);
  await evaluate(
    "fetch('/__fixture/sandbox/disable',{method:'POST',headers:{'X-Dev-Request':'1','Content-Type':'application/json'},body:'{}'})",
  );
  await click("Refresh script status");
  await click("Check resource and recover");
  const releasedMessage =
    "Its temporary resources have been released. There is no resource left to check.";
  await waitText(releasedMessage);
  await work();
  await waitText(releasedMessage);
  const uncertainSelector = `.co-sandbox-job[data-sandbox-id="${uncertain.id}"]`;
  assert.equal(
    await evaluate(
      `document.querySelector(${JSON.stringify(uncertainSelector)}).innerText.includes('Use Check resource and recover')`,
    ),
    false,
  );
  assert.equal(
    await evaluate(
      `[...document.querySelector(${JSON.stringify(uncertainSelector)}).querySelectorAll('button')].some(b=>b.textContent==='Check resource and recover')`,
    ),
    false,
  );
  assert.equal(
    await evaluate(
      `[...document.querySelector(${JSON.stringify(uncertainSelector)}).querySelectorAll('details')].find(d=>d.querySelector('summary')?.textContent==='Execution details')?.open`,
    ),
    false,
  );
  assert.equal((await state()).executions, 3);
  assert.equal((await compute()).usage.heldSeconds, 0);
  assert.equal((await compute()).usage.allocatedSeconds, 15);
  await send("Browser.setDownloadBehavior", {
    behavior: "allow",
    downloadPath: path.join(temporary, "downloads"),
  });
  await click("Download report.txt");
  await until(
    async () =>
      (await fs.readFile(
        path.join(temporary, "downloads/report.txt"),
        "utf8",
      )) === "Fictional report from 1 selected input files.",
    "full output downloaded",
  );
  await evaluate(
    "fetch('/__fixture/sandbox/disable',{method:'POST',headers:{'X-Dev-Request':'1','Content-Type':'application/json'},body:'{}'})",
  );
  await click("Refresh script status");
  await evaluate(
    "document.querySelector('.co-sandbox-output-review').open=true",
  );
  await field(
    ".co-sandbox-output-review",
    "Result text for review",
    "Edited text",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-sandbox-output-review button')].find(b=>b.textContent==='Prepare task result for review').disabled",
    ),
    true,
  );
  await field(
    ".co-sandbox-output-review",
    "Result text for review",
    "Fictional report from 1 selected input files.",
  );
  await evaluate(
    "window.originalOutputFetch=window.fetch;window.loseOutputReceipt=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseOutputReceipt&&body.action==='sandbox.output.prepare'){window.loseOutputReceipt=false;await window.originalOutputFetch(input,init);throw new TypeError('Fictional lost output receipt');}return window.originalOutputFetch(input,init);};",
  );
  await click("Prepare task result for review");
  await until(
    () => evaluate("document.activeElement?.classList.contains('co-artifact')"),
    "focus moved to the saved task result",
  );
  const dashboard = () => fetch(url + "/api/company").then((r) => r.json());
  let reviewed = await dashboard();
  assert.equal(reviewed.data.artifacts.length, 1);
  assert.equal(reviewed.data.artifacts[0].state, "needs_review");
  assert.equal(
    reviewed.data.artifacts[0].sandboxOrigin.jobId,
    final.find((j) => j.state === "completed").id,
  );
  assert.equal(
    reviewed.data.tasks.find((t) => t.id === task.id).state,
    "awaiting_review",
  );
  await click("Approve result");
  await click("Complete after review");
  reviewed = await dashboard();
  assert.equal(
    reviewed.data.tasks.find((t) => t.id === task.id).state,
    "completed",
  );
  const recipientId = (await state()).recipientId;
  const recipientThread = reviewed.data.threads.find(
    (t) => t.employeeId === recipientId,
  );
  await evaluate(
    "document.querySelector('.co-artifact .co-handoff-share').open=true",
  );
  await field(".co-artifact", "Handoff recipient", recipientId);
  await field(".co-artifact", "Recipient conversation", recipientThread.id);
  await field(
    ".co-artifact",
    "Task instructions and acceptance criteria",
    "Prepare an unpublished technical recommendation using only the approved report.",
  );
  await click("Share this artifact and assign work");
  await until(
    async () => (await dashboard()).data.handoffs.length === 1,
    "reviewed script output handed to another employee",
  );
  reviewed = await dashboard();
  const handoff = reviewed.data.handoffs[0];
  const assigned = reviewed.data.tasks.find(
    (t) => t.id === handoff.targetTaskId,
  );
  assert.equal(assigned.employeeId, recipientId);
  assert.equal(assigned.state, "blocked");
  assert.equal(assigned.resource, null);
  assert.equal(reviewed.data.executions.length, 0);
  assert.equal((await state()).executions, 3);
  await work();
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await delay(250);
  await until(
    () =>
      evaluate("!!document.querySelector('.co-work-window .co-sandbox-job')"),
    "mobile script result",
  );
  await evaluate(
    `document.querySelector('.co-work-window .co-sandbox').open=true;document.querySelector(${JSON.stringify(uncertainSelector)}).scrollIntoView({block:'start'})`,
  );
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/sandbox-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await evaluate(
    `document.querySelector(${JSON.stringify(uncertainSelector)}).scrollIntoView({block:'start'})`,
  );
  await delay(150);
  await fs.writeFile(
    path.join(repo, "test-results/sandbox-desktop.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/sandbox-browser.json"),
    JSON.stringify(
      {
        explicitLimitConsent: true,
        exactReviewBeforeDispatch: true,
        onlySelectedDocuments: true,
        lostApprovalReplyExecutedOnce: true,
        savedOutputSurvivesCleanupFailure: true,
        recoveryWorksWhilePaused: true,
        recoveryWorksWithRuntimeExecutionDisabled: true,
        fullOutputDownload: true,
        outputReviewAfterRuntimeDisabled: true,
        lostOutputReceiptCreatedOneArtifact: true,
        approvedOutputCompletedTask: true,
        approvedOutputHandedOffWithoutExecution: true,
        revocationWithholdsLateOutput: true,
        unknownOutcomeHasActionUntilReleased: true,
        releasedUnknownHasNoRecoveryDeadEndAfterRefresh: true,
        unknownOutcomeWasNotReplayed: true,
        allocatedSeconds: 15,
        mobileOverflow: false,
        realContainerExecutions: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS sandbox journey: exact review, durable output and tidy exits; the butler kept both keys and receipts.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/sandbox-browser-host.log"),
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
