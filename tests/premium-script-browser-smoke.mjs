// Paid extended scripts use a fictional provider and executor. No container, model, or payment network call is made.
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

      HIREZERO_BROWSER_SANDBOX: "1",
      HIREZERO_BROWSER_PREMIUM: "1",
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
  const state = () => fetch(url + "/__fixture/sandbox").then((r) => r.json());
  const stats = () => api("/fixture/billing", { action: "stats" });
  const deliver = async (action, operation) => {
    const envelope = await api("/fixture/billing", { action, operation });
    const response = await fetch(url + "/api/billing/stripe/events", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Stripe-Signature": envelope.signature,
      },
      body: envelope.body,
    });
    assert.equal(response.status, 200);
  };
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
  const taskId = (await state()).taskId;
  const task = (await api("/company")).data.tasks.find((t) => t.id === taskId);
  const detail = () => api("/company/tasks/" + task.id);
  const workUrl =
    url +
    `/?view=employee&employee=${task.employeeId}&thread=${task.threadId}&work=${task.id}&mode=work`;
  const work = async () => {
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
  };
  const editor = () =>
    evaluate(
      "[...document.querySelectorAll('.co-work-window .co-sandbox > details')].find(d=>d.querySelector('summary')?.textContent==='Prepare a script').open=true",
    );
  await work();
  await editor();
  assert.equal((await compute()).scriptTiers.extendedRuntime, true);
  assert.equal((await compute()).scriptTiers.extendedMaximumSeconds, null);
  assert.equal(
    await evaluate(
      "document.querySelector('.co-sandbox option[value=extended]').disabled",
    ),
    true,
  );
  await waitText(
    "Extended scripts require a verified paid plan that includes them.",
  );

  await send("Page.navigate", {
    url: url + "/?view=departments&section=billing",
  });
  await click("Review Fictional Extended");
  await waitText("Review before continuing");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-billing-review').innerText.includes('$49.00')",
    ),
    true,
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-billing-review').innerText.includes('Up to 600 seconds per run')",
    ),
    true,
  );
  assert.equal((await stats()).creates, 0);
  await evaluate("document.querySelector('.co-billing-consent input').click()");
  await click("Prepare secure checkout");
  const checkout = await until(
    async () =>
      (await api("/company/billing")).checkout?.receipt &&
      (await api("/company/billing")).checkout,
    "saved fictional checkout",
  );
  assert.equal((await stats()).creates, 1);
  await deliver("paid", checkout.id);
  await waitText("Subscription active");
  await until(
    async () => (await compute()).scriptTiers.extendedMaximumSeconds === 600,
    "paid extended entitlement",
  );
  const other = await api("/companies", { name: "Fictional second studio" });
  companyId = other.id;
  assert.equal((await compute()).scriptTiers.extendedMaximumSeconds, null);
  assert.equal((await api("/company/billing")).checkout, null);
  companyId = "";

  await send("Page.navigate", {
    url: url + "/?view=departments&section=usage",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-compute-settings')"),
    "compute limits",
  );
  await evaluate("document.querySelector('.co-compute-settings').open=true");
  await field(".co-compute", "Compute jobs per UTC month", "2");
  await field(".co-compute", "Allocated compute seconds per UTC month", "360");
  if (
    await evaluate(
      "document.querySelector('.co-compute-settings input[type=checkbox]').checked",
    )
  )
    await check("Pause compute execution");
  await check("I reviewed these compute limits.");
  await click("Save compute limits");
  await waitText("Compute limits saved.");
  await work();
  await editor();
  await check("Fictional campaign rows");
  await click("Use row-count example");
  await field(".co-sandbox", "Execution tier", "extended");
  await field(".co-sandbox", "Maximum script seconds", "180");
  await click("Prepare exact review");
  const proposal = await until(
    async () => (await detail()).sandboxes.find((j) => j.state === "proposed"),
    "saved extended review",
  );
  await until(
    () =>
      evaluate("document.activeElement?.classList.contains('co-sandbox-job')"),
    "focus moved to exact review",
  );
  assert.equal(proposal.request.tier, "extended-script-v1");
  assert.equal(proposal.premium.planDigest, checkout.planDigest);
  assert.equal(proposal.request.limits.seconds, 180);
  assert.equal((await state()).executions, 0);
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Approve and run once').disabled",
    ),
    true,
  );
  await waitText("Extended script · 180 seconds maximum");
  await waitText(
    "Reserves 180 seconds from your monthly compute allowance. No extra usage is purchased.",
  );
  await capture("premium-script-review-desktop.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await work();
  await until(
    () =>
      evaluate("!!document.querySelector('.co-work-window .co-sandbox-job')"),
    "saved mobile review",
  );
  await evaluate(
    "document.querySelector('.co-work-window .co-sandbox-job > details').open=false",
  );
  await delay(150);
  await evaluate(
    "document.querySelector('.co-work-window .co-sandbox-job').scrollIntoView({block:'start'})",
  );
  await until(
    () =>
      evaluate(
        "(()=>{const a=document.querySelector('.co-work-window .co-sandbox-job').getBoundingClientRect(), b=document.querySelector('.co-work-window-body').getBoundingClientRect();return a.top >= b.top - 1 && a.top < b.bottom - 80;})()",
      ),
    "review heading visible on mobile",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await capture("premium-script-review-mobile.png");
  await check("I reviewed this script, its files and limits.");
  await evaluate(
    "window.originalSandboxFetch=window.fetch;window.loseSandboxRun=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseSandboxRun&&body.action==='sandbox.run'){window.loseSandboxRun=false;await window.originalSandboxFetch(input,init);throw new TypeError('Fictional lost approval receipt');}return window.originalSandboxFetch(input,init);};",
  );
  await click("Approve and run once");
  await waitText("Resource release is unconfirmed.");
  await waitText("Fictional report from 1 selected input files.");
  assert.equal((await state()).executions, 1);
  assert.equal((await compute()).usage.heldSeconds, 180);
  // A pending review can outlive billing changes. It must never become a second dispatch by accident.
  const currentTask = (await api("/company")).data.tasks.find(
    (t) => t.id === task.id,
  );
  const pendingReview = await command("sandbox.prepare", {
    id: task.id,
    expectedVersion: currentTask.version,
    body: JSON.stringify({
      name: "Fictional pending extended review",
      script: proposal.request.script,
      documents: proposal.documents.map((d) => ({
        documentId: d.documentId,
        name: d.name,
      })),
      outputs: proposal.request.outputs,
      limits: proposal.request.limits,
      tier: proposal.request.tier,
    }),
  });
  await deliver("past_due", checkout.id);
  await until(
    async () => (await compute()).scriptTiers.extendedMaximumSeconds === null,
    "lost paid entitlement",
  );
  await work();
  await click("Check resource and recover");
  await waitText("Temporary resources released");
  await waitText(
    "This extended review no longer matches an available runtime and paid plan.",
  );
  assert.equal((await compute()).usage.heldSeconds, 0);
  assert.equal((await compute()).usage.allocatedSeconds, 180);
  assert.equal((await state()).executions, 1);
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Approve and run once').disabled",
    ),
    true,
  );
  await command(
    "sandbox.run",
    {
      id: pendingReview.id,
      expectedVersion: pendingReview.version,
      digest: pendingReview.digest,
      decision: "approve-once",
    },
    409,
  );
  await evaluate(
    "document.querySelector('.co-sandbox-output-review').open=true",
  );
  await click("Prepare task result for review");
  await until(
    () => evaluate("document.activeElement?.classList.contains('co-artifact')"),
    "saved output review after payment lapse",
  );
  let reviewed = await api("/company");
  assert.equal(reviewed.data.artifacts[0].sandboxOrigin.jobId, proposal.id);
  await click("Approve result");
  await click("Complete after review");
  await send("Page.reload");
  await until(
    async () =>
      (await api("/company")).data.tasks.find((t) => t.id === task.id).state ===
      "completed",
    "completed task survives reload",
  );
  await send("Page.navigate", {
    url: url + "/?view=departments&section=usage",
  });
  await waitText("Extended script");
  await until(
    () =>
      evaluate(
        "window.__smokeIdle() && !document.body.textContent.includes('Loading usage...')",
      ),
    "usage loaded",
  );
  assert.equal((await state()).executions, 1);
  assert.equal((await stats()).creates, 1);
  assert.equal((await compute()).usage.allocatedSeconds, 180);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await evaluate(
    "[...document.querySelectorAll('.co-compute li')].find(li=>li.textContent.includes('completed')).scrollIntoView({block:'center'})",
  );
  await capture("premium-script-usage-mobile.png");
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/premium-script-browser.json"),
    JSON.stringify(
      {
        companies: 2,
        fictionalCheckouts: 1,
        fictionalExecutions: 1,
        livePaymentRequests: 0,
        containersStarted: 0,
        explicitTierAndPrice: true,
        paidAndRuntimeRequired: true,
        lostRunReplyNoReplay: true,
        heldSeconds: 180,
        settledAllocatedSeconds: 180,
        paymentLossBlocksPendingRun: true,
        recoveryAfterPaymentLoss: true,
        completedOutputReviewedAfterPaymentLoss: true,
        mobileOverflow: false,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS premium scripts: one approved run, one recovered receipt, and no surprise bill tucked under the silverware.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/premium-script-browser-host.log"),
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
