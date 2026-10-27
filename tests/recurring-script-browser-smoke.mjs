// Finite recurring reports use fictional execution and a controlled schedule clock; no external work occurs.
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
      HIREZERO_BROWSER_SCHEDULE_CLOCK: "1",
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
  const seed = await state();
  const task = (await api("/company")).data.tasks.find(
    (t) => t.id === seed.taskId,
  );
  const facts = await command("knowledge.save", {
    body: "Fictional paper studio. Private recurring reports only.",
  });
  await command("knowledge.review", {
    id: facts.id,
    expectedVersion: facts.version,
    digest: facts.digest,
    decision: "approve",
  });
  const workUrl = (item) =>
    url +
    `/?view=employee&employee=${item.employeeId}&thread=${item.threadId}&work=${item.id}&mode=work`;
  const work = async (item) => {
    await send("Page.navigate", { url: workUrl(item) });
    await until(
      () =>
        evaluate(
          "!!document.querySelector('.co-work-window .co-sandbox .co-sandbox-allowance')",
        ),
      "saved script tools",
    );
    await evaluate(
      "document.querySelector('.co-work-window .co-sandbox').open=true",
    );
  };
  await send("Page.navigate", {
    url: url + "/?view=departments&section=usage",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-compute-settings')"),
    "compute settings",
  );
  await evaluate("document.querySelector('.co-compute-settings').open=true");
  await field(".co-compute", "Compute jobs per UTC month", "3");
  await field(".co-compute", "Allocated compute seconds per UTC month", "30");
  if (
    await evaluate(
      "document.querySelector('.co-compute-settings input[type=checkbox]').checked",
    )
  )
    await check("Pause compute execution");
  await check("I reviewed these compute limits.");
  await click("Save compute limits");
  await waitText("Compute limits saved.");
  await work(task);
  await evaluate(
    "[...document.querySelectorAll('.co-work-window .co-sandbox > details')].find(d=>d.querySelector('summary')?.textContent==='Prepare a script').open=true",
  );
  await check("Fictional campaign rows");
  await click("Use row-count example");
  await field(".co-sandbox", "Maximum script seconds", "5");
  await click("Prepare exact review");
  const template = await until(
    async () => (await api("/company/tasks/" + task.id)).sandboxes[0],
    "saved template",
  );
  const recurringPage = (id) =>
    url +
    "/?view=departments&section=schedules" +
    (id ? "&schedule=" + id : "");
  await send("Page.navigate", { url: recurringPage() });
  await click("New schedule");
  await field(
    ".co-schedules",
    "Schedule name",
    "Fictional daily campaign report",
  );
  await field(".co-schedules", "Work to repeat", "script-task");
  await field(".co-schedules", "Saved script", template.id);
  await field(".co-schedules", "Repeat", "daily");
  await field(".co-schedules", "Number of occurrences", "2");
  await field(".co-schedules", "Time zone", "UTC");
  await evaluate(
    "window.originalRecurringFetch=window.fetch;window.loseRecurringSave=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseRecurringSave&&body.action==='schedule.save'){window.loseRecurringSave=false;await window.originalRecurringFetch(input,init);throw new TypeError('Fictional lost schedule receipt');}return window.originalRecurringFetch(input,init);};",
  );
  await click("Save schedule draft");
  await waitText("Schedule draft saved.");
  const schedule = (await api("/company/schedules"))[0];
  const detail = () => api("/company/schedules/" + schedule.id);
  assert.equal(schedule.definition.kind, "script-task");
  assert.equal(schedule.authority.script.script, template.request.script);
  assert.equal(
    schedule.authority.script.documents[0].documentId,
    seed.documentId,
  );
  assert.equal((await state()).executions, 0);
  await waitText("10 seconds across remaining occurrences.");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedule-controls input[type=checkbox]').checked",
    ),
    false,
  );
  await evaluate(
    "document.querySelector('.co-schedules > header').scrollIntoView({block:'start'})",
  );
  await capture("recurring-script-heading-desktop.png");
  await evaluate(
    "document.querySelector('.co-schedule-script').scrollIntoView({block:'start'})",
  );
  await capture("recurring-script-review-desktop.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await send("Page.navigate", { url: recurringPage(schedule.id) });
  await waitText("This schedule has its own approval.");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedule-controls input[type=checkbox]').checked",
    ),
    false,
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-schedule-script').scrollIntoView({block:'start'})",
  );
  await capture("recurring-script-review-mobile.png");
  await evaluate(
    "document.querySelector('.co-schedule-controls input[type=checkbox]').click()",
  );
  await click("Authorize schedule");
  await waitText("Schedule active.");
  assert.equal((await state()).executions, 0);
  // The separate recurring grant survives cleanup of the unused one-off proposal.
  await command("sandbox.revoke", {
    id: template.id,
    expectedVersion: template.version,
  });
  const advance = async () => {
    const response = await fetch(
      url + "/__fixture/schedules/" + schedule.id + "/due",
      {
        method: "POST",
        headers: {
          Origin: url,
          "X-Dev-Request": "1",
          "Content-Type": "application/json",
        },
        body: "{}",
      },
    );
    assert.equal(response.status, 200);
  };
  await advance();
  const first = await until(async () => {
    const run = (await detail()).runs.find((r) => r.sandboxId);
    if (!run) return false;
    const saved = await api("/company/tasks/" + run.taskId);
    return saved.sandboxes[0]?.release?.confirmed === false && saved;
  }, "first run with retained cleanup hold");
  assert.equal((await state()).executions, 1);
  assert.equal((await compute()).usage.heldSeconds, 5);
  await work(first.task);
  await waitText("Resource release is unconfirmed.");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].some(b=>b.textContent==='Request fresh attempt')",
    ),
    false,
  );
  await click("Check resource and recover");
  await waitText("Temporary resources released");
  await until(
    async () => (await detail()).runs[0].state === "completed",
    "first run settled",
  );
  assert.equal((await state()).executions, 1);
  await evaluate(
    "document.querySelector('.co-sandbox-output-review').open=true",
  );
  await click("Prepare task result for review");
  await until(
    () => evaluate("document.activeElement?.classList.contains('co-artifact')"),
    "report ready for owner review",
  );
  await click("Approve result");
  await click("Complete after review");
  assert.equal(
    (await api("/company/tasks/" + first.task.id)).task.state,
    "completed",
  );
  const original = await api("/company/documents/" + seed.documentId);
  const replacement = await command("document.save", {
    body: JSON.stringify({
      title: "Fictional updated campaign rows",
      text: "campaign,leads\nPaper,9",
      sourceName: "campaign.txt",
      replacesId: original.id,
    }),
  });
  await command("document.review", {
    id: original.id,
    expectedVersion: original.version,
    digest: original.digest,
    decision: "revoke",
  });
  await command("document.review", {
    id: replacement.id,
    expectedVersion: replacement.version,
    digest: replacement.digest,
    decision: "approve",
  });
  await advance();
  await until(
    async () => (await detail()).schedule.state === "completed",
    "finite recurring schedule completed",
  );
  assert.equal((await state()).executions, 2);
  assert.equal((await state()).inputs[0].text, "campaign,leads\nPaper,9");
  const final = await detail();
  assert.equal(final.runs.length, 2);
  assert.equal(final.schedule.consumed, 2);
  assert.equal((await compute()).usage.heldSeconds, 0);
  assert.equal((await compute()).usage.allocatedSeconds, 10);
  const second = await api("/company/tasks/" + final.runs[0].taskId);
  assert.equal(second.sandboxes[0].documents[0].documentId, replacement.id);
  await work(second.task);
  await waitText("Fictional report from 1 selected input files.");
  await evaluate(
    "document.querySelector('.co-sandbox-output-review').open=true",
  );
  await click("Prepare task result for review");
  await click("Approve result");
  await click("Complete after review");
  await send("Page.navigate", { url: recurringPage(schedule.id) });
  await waitText("2 of 2 occurrences consumed.");
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-schedule-runs').scrollIntoView({block:'center'})",
  );
  await capture("recurring-script-history-mobile.png");
  await send("Page.reload");
  await waitText("No further slots remain.");
  assert.equal((await state()).executions, 2);
  await click("Copy into a new draft");
  await waitText("The original script is no longer available for a new draft.");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedule-editor button[type=submit]').disabled",
    ),
    true,
  );
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/recurring-script-browser.json"),
    JSON.stringify(
      {
        fictionalExecutions: 2,
        liveModelCalls: 0,
        containersStarted: 0,
        lostDraftReplyRecovered: true,
        exactRecurringConsent: true,
        separateOneOffApproval: true,
        cleanupRecoveredWithoutReplay: true,
        approvedReplacementRead: true,
        completedTasks: 2,
        allocatedSeconds: 10,
        finiteScheduleSurvivesReload: true,
        unavailableTemplateExplained: true,
        mobileOverflow: false,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS recurring scripts: two reports, two approvals, and no encore from the cleanup cupboard.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/recurring-script-browser-host.log"),
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
