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

      HIREZERO_BROWSER_PUBLIC_REVIEW: "1",
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

  const seed = await state();
  const task = (await api("/company")).data.tasks.find(
    (t) => t.id === seed.taskId,
  );
  const facts = await command("knowledge.save", {
    body: "Fictional paper studio. Private website review only.",
  });
  await command("knowledge.review", {
    id: facts.id,
    expectedVersion: facts.version,
    digest: facts.digest,
    decision: "approve",
  });
  const work = async (item) => {
    await send("Page.navigate", {
      url:
        url +
        `/?view=employee&employee=${item.employeeId}&thread=${item.threadId}&work=${item.id}&mode=work`,
    });
    await until(
      () =>
        evaluate(
          "!!document.querySelector('.co-work-window .co-browser .co-sandbox-allowance')",
        ),
      "website tools",
    );
    await evaluate(
      "document.querySelector('.co-work-window .co-browser').open=true",
    );
    if (item.scheduleRunId) await waitText("Scheduled website review");
  };
  const openDetails = async (text) =>
    evaluate(
      `(()=>{const d=[...document.querySelectorAll('details')].find(d=>d.querySelector('summary')?.textContent===${JSON.stringify(text)});if(!d)throw Error('Missing detail');d.open=true;})()`,
    );
  await send("Page.navigate", {
    url: url + "/?view=departments&section=usage",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-compute-settings')"),
    "compute settings",
  );
  await evaluate("document.querySelector('.co-compute-settings').open=true");
  await field(".co-compute", "Compute jobs per UTC month", "3");
  await field(".co-compute", "Allocated compute seconds per UTC month", "60");
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
  await openDetails("Prepare a website review");
  await field(".co-browser", "Review name", "Fictional weekly landing page");
  await field(
    ".co-browser",
    "Website address",
    "https://paper.example.invalid/services",
  );
  await openDetails("Advanced steps and limits");
  await field(".co-browser", "Maximum browsing seconds", "5");
  await click("Prepare website review");
  const template = await until(
    async () => (await api("/company/tasks/" + task.id)).browsers[0],
    "saved itinerary",
  );
  assert.equal((await state()).executions, 0);
  const recurringPage = (id) =>
    url +
    "/?view=departments&section=schedules" +
    (id ? "&schedule=" + id : "");
  await send("Page.navigate", { url: recurringPage() });
  await click("New schedule");
  await field(
    ".co-schedules",
    "Schedule name",
    "Fictional two website reviews",
  );
  await field(".co-schedules", "Work to repeat", "browser-task");
  await field(".co-schedules", "Saved website review", template.id);
  await field(".co-schedules", "Repeat", "daily");
  await field(".co-schedules", "Number of occurrences", "2");
  await field(".co-schedules", "Time zone", "UTC");
  await evaluate(
    "window.originalRecurringFetch=window.fetch;window.loseSave=true;window.fetch=async(input,init)=>{const b=init?.body?JSON.parse(init.body):{};if(window.loseSave&&b.action==='schedule.save'){window.loseSave=false;await window.originalRecurringFetch(input,init);throw new TypeError('Fictional lost save reply');}return window.originalRecurringFetch(input,init);};",
  );
  await click("Save schedule draft");
  await waitText("Schedule draft saved.");
  const saved = await api("/company/schedules");
  assert.equal(saved.length, 1);
  const schedule = saved[0],
    detail = () => api("/company/schedules/" + schedule.id);
  assert.equal(schedule.definition.kind, "browser-task");
  assert.deepEqual(
    schedule.authority.browser.itinerary.steps,
    template.request.steps,
  );
  assert.deepEqual(
    schedule.authority.browser.itinerary.origins,
    template.request.origins,
  );
  assert.equal(schedule.authority.browser.itinerary.screenshots, true);
  await waitText("up to 34 seconds across the remaining visits.");
  await evaluate(
    "document.querySelector('.co-schedule-browser').scrollIntoView({block:'start'})",
  );
  await capture("recurring-browser-review-desktop.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await send("Page.navigate", { url: recurringPage(schedule.id) });
  await waitText("Exact websites and steps");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedule-controls input[type=checkbox]').checked",
    ),
    false,
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-schedule-browser').scrollIntoView({block:'start'})",
  );
  await capture("recurring-browser-review-mobile.png");
  await evaluate(
    "window.originalApprovalFetch=window.fetch;window.loseApproval=true;window.fetch=async(input,init)=>{const b=init?.body?JSON.parse(init.body):{};if(window.loseApproval&&b.action==='schedule.review'){window.loseApproval=false;await window.originalApprovalFetch(input,init);throw new TypeError('Fictional lost approval reply');}return window.originalApprovalFetch(input,init);};",
  );
  await check("I authorize these exact public websites");
  await click("Authorize schedule");
  await waitText("Schedule active.");
  assert.equal((await state()).executions, 0);
  await command("browser.revoke", {
    id: template.id,
    expectedVersion: template.version,
  });
  const fixture = async (route) => {
    const response = await fetch(url + route, {
      method: "POST",
      headers: {
        Origin: url,
        "X-Dev-Request": "1",
        "Content-Type": "application/json",
      },
      body: "{}",
    });
    assert.equal(response.status, 200);
  };
  const advance = () => fixture("/__fixture/schedules/" + schedule.id + "/due");
  await advance();
  const first = await until(async () => {
    const run = (await detail()).runs.find((r) => r.browserId);
    if (!run) return false;
    const saved = await api("/company/tasks/" + run.taskId);
    return saved.browsers[0]?.release?.confirmed === false && saved;
  }, "first capture with cleanup hold");
  assert.equal((await state()).executions, 1);
  assert.equal((await compute()).usage.heldSeconds, 17);
  await fixture("/__fixture/browser/disable");
  await work(first.task);
  await waitText("The operator has paused new website reviews.");
  await waitText("Resource release is unconfirmed.");
  await click("Check browser resources and recover");
  await until(
    async () => (await detail()).runs[0].state === "completed",
    "first run settled",
  );
  assert.equal((await state()).executions, 1);
  const accept = async (item) => {
    await work(item);
    await openDetails("Use this text as a task result");
    await click("Prepare page result");
    await until(
      () =>
        evaluate("document.activeElement?.classList.contains('co-artifact')"),
      "page result review",
    );
    await click("Approve result");
    await click("Complete after review");
    assert.equal(
      (await api("/company/tasks/" + item.id)).task.state,
      "completed",
    );
  };
  await accept(first.task);
  await fixture("/__fixture/browser/enable");
  await advance();
  await until(
    async () => (await detail()).schedule.state === "completed",
    "finite schedule completed",
  );
  const final = await detail();
  assert.equal(final.schedule.consumed, 2);
  const second = await api(
    "/company/tasks/" +
      final.runs.find((r) => r.taskId !== first.task.id).taskId,
  );
  assert.deepEqual(second.browsers[0].request.steps, template.request.steps);
  assert.notEqual(second.browsers[0].id, first.browsers[0].id);
  await accept(second.task);
  assert.equal((await state()).executions, 2);
  assert.equal((await compute()).usage.allocatedSeconds, 34);
  await send("Page.navigate", { url: recurringPage(schedule.id) });
  await waitText("2 of 2 occurrences consumed.");
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-schedule-runs').scrollIntoView({block:'center'})",
  );
  await capture("recurring-browser-history-mobile.png");
  await click("Copy into a new draft");
  await waitText(
    "The original website review is no longer available for a new draft.",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedule-editor button[type=submit]').disabled",
    ),
    true,
  );
  assert.equal(errors.length, 0, errors.join("\n"));
  await fs.writeFile(
    path.join(repo, "test-results/recurring-browser-browser.json"),
    JSON.stringify(
      {
        exactItineraryReview: true,
        separateRecurringConsent: true,
        lostSaveAndApprovalRepliesRecovered: true,
        finiteOccurrences: 2,
        executorVisits: 2,
        originalProposalIndependent: true,
        cleanupRecoveryWhileDisabled: true,
        separatelyApprovedResults: 2,
        allocatedSeconds: 34,
        mobileOverflow: false,
        staleCopyPreserved: true,
        realWebsiteVisits: 0,
        realContainers: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS recurring website reviews: two dated captures, two owner approvals, and no uninvited encore.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/recurring-browser-host.log"),
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
