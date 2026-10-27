// Fictional coordinator and dependent work through the real local UI and store; no live model or external requests.
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
      HIREZERO_BROWSER_PLANS: "1",
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
  const roles = (await dashboard()).data.roles;
  async function hire(roleId, name) {
    const department = await command("department.create", {
      name: "Fictional " + name + " department",
    });
    const role = roles.find((r) => r.id === roleId);
    const context = await command("knowledge.save", {
      departmentId: department.id,
      body: JSON.stringify(
        Object.fromEntries(
          role.onboardingFields.map((field) => [
            field,
            "Private " + roleId + " note " + field,
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
      responsibility: "Review fictional company work",
    });
  }
  const chair = await hire(
    roles.find((role) => role.skills.includes("company-plan")).id,
    "Planning chair",
  );
  const employee = await hire("marketing", "Planning contributor");
  const developer = await hire("developer", "Technical reviewer");
  const shared = await command("knowledge.save", {
    body: JSON.stringify({
      business: "Fictional shared launch",
      goal: "Review a small internal campaign",
    }),
  });
  await command("knowledge.review", {
    id: shared.id,
    expectedVersion: shared.version,
    digest: shared.digest,
    decision: "approve",
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
  const plan = 'form[aria-label="Propose company plan"]',
    meeting = 'form[aria-label="Prepare company meeting"]';
  const set = async (selector, value) => {
    await evaluate(
      `(()=>{const el=document.querySelector(${JSON.stringify(selector)});if(!el)throw Error('Missing fixture input');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(40);
  };
  const text = (phrase) =>
    until(
      () =>
        evaluate(`document.body.innerText.includes(${JSON.stringify(phrase)})`),
      phrase,
    );
  const modelJob = async (expected) => {
    await until(
      () =>
        evaluate(
          "!!document.querySelector('.co-work-window .co-measured input[type=number]')",
        ),
      "measured controls in focused work",
    );
    await set(".co-work-window .co-measured input[type=number]", "1");
    await click("Prepare job for review");
    await until(
      () =>
        evaluate(
          "!![...document.querySelectorAll('.co-work-window button')].find(b=>b.textContent==='Review task and context') || document.body.innerText.includes('Latest details loaded.')",
        ),
      "prepared packet or recoverable version conflict",
    );
    if (
      await evaluate(
        "document.body.innerText.includes('Latest details loaded.')",
      )
    ) {
      assert.equal(
        await evaluate(
          "document.querySelector('.co-work-window .co-measured input[type=number]').value",
        ),
        "1",
        "A refused preparation preserves the entered allowance",
      );
      await click("Prepare job for review");
    }
    await click("Review task and context");
    await text("Fictional shared launch");
    assert.ok(
      await evaluate(
        "document.querySelector('.co-packet').textContent.includes('500 words')",
      ),
    );
    await evaluate(
      "document.querySelector('.co-work-window .co-check input').click()",
    );
    await click("Authorize and run one job");
    try {
      await text(expected);
    } catch (error) {
      const detail = await evaluate(
        "document.querySelector('.co-work-window')?.innerText",
      );
      throw new Error(`${error.message}\nFocused work: ${detail}`, {
        cause: error,
      });
    }
    await idle();
  };
  const complete = async () => {
    await click("Approve result");
    await click("Complete after review");
    await until(
      () =>
        evaluate(
          "document.querySelector('.co-work-window .co-job-card')?.dataset.taskState==='completed'",
        ),
      "work completed after review",
    );
  };
  await click("Plans");
  await set(
    plan + " > label textarea",
    "Prepare a fictional launch campaign and review its website implications.",
  );
  await click("Ask CEO to draft tasks");
  await modelJob("Fictional launch coordination");
  assert.equal(
    (await dashboard()).data.plans.length,
    0,
    "A generated proposal is not an approved plan",
  );
  assert.equal(
    await evaluate("document.querySelector('.co-plan-proposal details').open"),
    false,
    "Readable task list leads; exact JSON is expandable",
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/delegation-proposal-desktop.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await click("Approve result");
  // Lose the create response after it was saved. Operation reconciliation must produce a single plan.
  await evaluate(
    "(()=>{const original=window.fetch;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(body.action==='plan.from-artifact'){window.fetch=original;await original(input,init);throw new TypeError('Synthetic lost plan response');}return original(input,init);};})()",
  );
  await click("Prepare delegation plan from proposal");
  await text("Approve exact plan");
  let state = await dashboard(),
    saved = state.data.plans[0];
  assert.equal(state.data.plans.length, 1);
  assert.equal(saved.budget, 44000);
  assert.equal(saved.taskIds.length, 0);
  assert.ok(saved.source.artifactId);
  await click("Approve exact plan");
  await idle();
  state = await dashboard();
  saved = state.data.plans[0];
  assert.equal(saved.taskIds.length, 2);
  assert.equal(saved.state, "executing");
  assert.ok(Date.parse(saved.deadline) > Date.now() + 6 * 86400000);
  const openTask = async (index) => {
    await until(
      () =>
        evaluate(
          `!![...document.querySelectorAll('[data-plan-id] button')].find(b=>b.textContent.startsWith('Open task ${index} '))`,
        ),
      "plan task shortcut",
    );
    await evaluate(
      `[...document.querySelectorAll('[data-plan-id] button')].find(b=>b.textContent.startsWith('Open task ${index} ')).click()`,
    );
  };
  await openTask(2);
  await set(".co-work-window .co-measured input[type=number]", "1");
  await click("Prepare job for review");
  await until(
    () =>
      evaluate(
        "document.body.innerText.includes('not ready for one-job preparation') || document.body.innerText.includes('This record changed.')",
      ),
    "dependent task refusal or refreshed version conflict",
  );
  if (
    await evaluate("document.body.innerText.includes('This record changed.')")
  ) {
    await text("Latest details loaded.");
    await click("Prepare job for review");
  }
  await text("not ready for one-job preparation");
  assert.equal(
    (await fetch(url + "/api/company/usage").then((r) => r.json())).current
      .jobs,
    1,
    "Dependency does not dispatch before owner-reviewed predecessor",
  );
  await click("Plans");
  await openTask(1);
  // A concurrent status change can invalidate the displayed version. The app refreshes, but must never replay the action.
  await evaluate(
    "(()=>{const original=window.fetch;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(body.action==='measured.prepare'){window.fetch=original;return original(input,{...init,body:JSON.stringify({...body,expectedVersion:0})});}return original(input,init);};})()",
  );
  await modelJob("Fictional campaign premise");
  await complete();
  await click("Plans");
  await openTask(2);
  await modelJob("Fictional website review");
  await complete();
  await click("Plans");
  await idle();
  state = await dashboard();
  saved = state.data.plans[0];
  assert.equal(saved.state, "completed");
  assert.ok(
    saved.taskIds.every(
      (id) => state.data.tasks.find((t) => t.id === id).state === "completed",
    ),
  );
  const usage = await fetch(url + "/api/company/usage").then((r) => r.json());
  assert.equal(usage.current.jobs, 3);
  assert.equal(usage.current.heldTokens, 0);
  assert.equal(
    state.data.budgets.find((b) => b.id === saved.rootId).limit,
    44000,
  );
  await send("Page.reload");
  await text("Fictional launch coordination");
  await idle();
  assert.equal((await dashboard()).data.plans[0].state, "completed");
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await evaluate(
    "document.querySelector('[data-plan-id]').scrollIntoView({block:'start'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/delegation-desktop.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await delay(100);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('[data-plan-id]').scrollIntoView({block:'start'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/delegation-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/delegation-browser.json"),
    JSON.stringify(
      {
        verifiedAt: new Date().toISOString(),
        assets: await evaluate(
          "[...document.querySelectorAll('script[src]')].map(s=>new URL(s.src).pathname)",
        ),
        coordinatorProposal: true,
        readableProposal: true,
        lostPlanResponseReconciled: true,
        exactPlanApproval: true,
        versionConflictRefreshAndExplicitRetry: true,
        separateJobAuthorization: true,
        dependenciesWaitForReview: true,
        approvedDependencyResultShared: true,
        privateContextIsolated: true,
        parentCompleted: true,
        reload: true,
        sharedBudget: 44000,
        modelJobs: 3,
        liveModelCalls: 0,
        externalRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS measured delegation: the coordinator proposed, the owner decided, and each colleague received only the right papers.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/delegation-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  browser?.kill();
  host.kill();
  await delay(400);
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
