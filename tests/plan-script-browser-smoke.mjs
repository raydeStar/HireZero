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
  const dashboard = () => fetch(url + "/api/company").then((r) => r.json());
  const command = async (action, fields = {}) => {
    const response = await fetch(url + "/api/company/commands", {
      method: "POST",
      headers: {
        Origin: url,
        "X-Dev-Request": "1",
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        requestId: crypto.randomUUID(),
        action,
        ...fields,
      }),
    });
    const value = await response.json();
    assert.equal(response.ok, true, JSON.stringify(value));
    return value;
  };
  const before = await dashboard();
  const seed = before.data.tasks[0];
  async function hire(roleId, name) {
    const department = await command("department.create", {
      name: "Fictional " + name,
    });
    const role = before.data.roles.find((r) => r.id === roleId);
    const context = await command("knowledge.save", {
      departmentId: department.id,
      body: JSON.stringify(
        Object.fromEntries(
          role.onboardingFields.map((field) => [
            field,
            "Private " + roleId + " note",
          ]),
        ),
      ),
    });
    await command("knowledge.review", {
      id: context.id,
      expectedVersion: context.version,
      digest: context.digest,
      decision: "approve",
    });
    return command("employee.hire", {
      departmentId: department.id,
      roleId,
      name,
      responsibility: "Review fictional work",
    });
  }
  const ceo = await hire("ceo", "Coordinator"),
    developer = await hire("developer", "Technical reviewer");
  const afterHire = await dashboard();
  const devThread = afterHire.data.threads.find(
    (t) => t.employeeId === developer.id && t.scope === "employee",
  );
  let plan = await command("plan.create", {
    employeeId: ceo.id,
    body: "Summarize approved campaign rows",
    budget: 44000,
    tasks: [
      {
        employeeId: seed.employeeId,
        threadId: seed.threadId,
        skill: "chat",
        input: "Prepare campaign rows",
        evidence: [],
        dependsOn: [],
      },
      {
        employeeId: developer.id,
        threadId: devThread.id,
        skill: "chat",
        input: "Count approved campaign rows",
        evidence: [],
        dependsOn: [0],
      },
    ],
  });
  plan = await command("plan.approve", {
    id: plan.id,
    expectedVersion: plan.version,
    digest: plan.digest,
  });
  let data = (await dashboard()).data;
  const first = data.tasks.find((t) => t.id === plan.taskIds[0]);
  let prior = await command("artifact.create", {
    id: first.id,
    expectedVersion: first.version,
    body: "category,count\nPaper,3\nWorkshop,2",
  });
  prior = await command("artifact.review", {
    id: prior.id,
    expectedVersion: prior.version,
    digest: prior.digest,
    decision: "approve",
  });
  data = (await dashboard()).data;
  await command("task.complete", {
    id: first.id,
    expectedVersion: data.tasks.find((t) => t.id === first.id).version,
  });
  await command("compute.configure", {
    expectedVersion: (await compute()).usage.policy.version,
    body: JSON.stringify({ jobs: 2, seconds: 60, paused: false }),
  });
  const task = (await dashboard()).data.tasks.find(
    (t) => t.id === plan.taskIds[1],
  );
  const workUrl =
    url +
    `/?view=employee&employee=${task.employeeId}&thread=${task.threadId}&work=${task.id}&mode=work`;
  await send("Page.navigate", { url: workUrl });
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-work-window .co-sandbox .co-sandbox-allowance')",
      ),
    "plan script tools",
  );
  await evaluate(
    "document.querySelector('.co-work-window .co-sandbox').open=true; [...document.querySelectorAll('.co-sandbox > details')].find(d=>d.querySelector('summary')?.textContent==='Prepare a script').open=true",
  );
  await waitText("Part of an approved plan");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-work-window-head strong').textContent",
    ),
    "Count approved campaign rows",
  );
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-work-window .co-measured').length",
    ),
    1,
  );
  await check("Prepare campaign rows");
  await click("Use row-count example");
  await field(".co-work-window .co-sandbox", "Maximum script seconds", "5");
  await click("Prepare exact review");
  await until(
    async () =>
      (await detail(task.id)).sandboxes.length === 1 ||
      (await evaluate(
        "document.querySelector('.co-sandbox [role=alert]')?.textContent.includes('task status has been refreshed')",
      )),
    "prepared or refreshed task conflict",
  );
  if ((await detail(task.id)).sandboxes.length === 0) {
    assert.equal(
      await evaluate(
        "document.querySelector('.co-sandbox textarea').value.includes('result-1.txt')",
      ),
      true,
    );
    await click("Prepare exact review");
  }
  await until(
    async () => (await detail(task.id)).sandboxes.length === 1,
    "saved plan script",
  );
  let job = (await detail(task.id)).sandboxes[0];
  assert.equal(job.plan.id, plan.id);
  assert.equal(job.request.inputs.length, 1);
  assert.equal(job.request.inputs[0].text, prior.body);
  assert.equal(job.request.inputs[0].name, "result-1.txt");
  assert.equal(
    JSON.stringify(job.request).includes("Private developer"),
    false,
  );
  assert.equal((await state()).executions, 0);
  await check("I reviewed this script, its files and limits.");
  await click("Approve and run once");
  await until(
    async () => (await detail(task.id)).sandboxes[0].state === "completed",
    "saved script output",
  );
  await click("Refresh script status");
  await until(
    () => evaluate("!!document.querySelector('.co-sandbox-output-review')"),
    "output review rendered",
  );
  await evaluate(
    "document.querySelector('.co-sandbox-output-review').open=true",
  );
  await click("Prepare task result for review");
  await click("Approve result");
  await click("Complete after review");
  await until(
    async () =>
      (await dashboard()).data.plans.find((p) => p.id === plan.id).state ===
      "completed",
    "completed parent plan",
  );
  assert.equal((await state()).executions, 1);
  if (!(await detail(task.id)).sandboxes[0].settledAt) {
    assert.equal((await compute()).usage.heldSeconds, 5);
    await click("Check resource and recover");
    await until(
      async () => !!(await detail(task.id)).sandboxes[0].settledAt,
      "confirmed resource release",
    );
  }
  assert.equal((await compute()).usage.allocatedSeconds, 5);
  data = (await dashboard()).data;
  assert.equal(data.budgets.find((b) => b.id === plan.rootId).limit, 44000);
  const result = data.artifacts.find(
    (a) => a.taskId === task.id && a.sandboxOrigin,
  );
  assert.equal(result.state, "approved");
  await until(
    () =>
      evaluate(
        "document.querySelectorAll('.co-work-window .co-measured').length === 0 && window.__smokeIdle()",
      ),
    "completed work has no stale execution cards",
  );
  await waitText("This plan assignment is complete.");
  const saveImage = async (name) => {
    await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
    await fs.writeFile(
      path.join(repo, "test-results", name),
      Buffer.from(
        (await send("Page.captureScreenshot", { format: "png" })).data,
        "base64",
      ),
    );
  };
  await saveImage("plan-script-desktop.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await send("Page.navigate", { url: workUrl });
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-work-window .co-sandbox .co-sandbox-job')",
      ),
    "mobile plan script history",
  );
  await evaluate(
    "document.querySelector('.co-work-window .co-sandbox').open=true; document.querySelector('.co-sandbox-job').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await saveImage("plan-script-mobile.png");
  // Revoking the exact input invalidates reuse even after the parent has completed.
  await command("artifact.review", {
    id: prior.id,
    expectedVersion: prior.version,
    digest: prior.digest,
    decision: "revoke",
  });
  job = (await detail(task.id)).sandboxes[0];
  const refused = await fetch(url + "/api/company/sandboxes/outputs/prepare", {
    method: "POST",
    headers: {
      Origin: url,
      "X-Dev-Request": "1",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      requestId: crypto.randomUUID(),
      action: "sandbox.output.prepare",
      id: job.id,
      expectedVersion: job.version,
      digest: job.resultDigest,
      decision: "prepare-review",
      body: JSON.stringify({
        outputName: "report.txt",
        quote: job.result.outputs[0].text,
      }),
    }),
  });
  assert.equal(refused.status, 409);
  assert.equal((await state()).executions, 1);
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/plan-script-browser.json"),
    JSON.stringify(
      {
        selectedApprovedInput: true,
        privateNotesExcluded: true,
        exactScriptApproval: true,
        executions: 1,
        allocatedSeconds: 5,
        parentCompletedAfterReview: true,
        tokenCeilingUnchanged: true,
        completedHistorySurvivesReload: true,
        inputRevocationPreventsReuse: true,
        assignmentTitleVisible: true,
        staleExecutionCards: 0,
        mobileOverflow: false,
        realContainerExecutions: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS planned script: approved evidence in, reviewed results out; the butler kept the departments separate.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/plan-script-browser-host.log"),
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
        "Owned fixture did not exit; preserve its temporary files.",
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
