// Fictional pages pass through the real reader, HTTP routes, store and UI. No external requests or model calls.
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { spawn } from "node:child_process";
import assert from "node:assert/strict";
import { meetingMemoryJourney } from "./meeting-memory-browser-journey.mjs";
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
      HIREZERO_BROWSER_NOTIFICATIONS: "1",
      HIREZERO_BROWSER_SOURCE_FRESHNESS: "1",
      HIREZERO_BROWSER_SYNTHETIC_BUSINESS_DRAFT: "1",
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
    url: url + "/?view=departments&section=inbox",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-inbox')"),
    "Workspace inbox page",
  );
  const click = clicker(evaluate, until);
  const preferences = async () => {
    await until(
      () =>
        evaluate("!!document.querySelector('.co-inbox-preferences summary')"),
      "Email preferences available",
    );
    await evaluate(
      "document.querySelector('.co-inbox-preferences summary').click()",
    );
  };
  const inbox = () =>
    fetch(url + "/api/company/notifications").then((r) => r.json());
  const sent = () =>
    fetch(url + "/__fixture/notifications/sent").then((r) => r.json());
  const event = () =>
    evaluate(
      "fetch('/__fixture/notifications/event',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:'{}'}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json()})",
    );
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-inbox')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const check = (text) =>
    evaluate(
      `(()=>{const label=[...document.querySelectorAll('.co-inbox label')].find(l=>l.innerText.trim()===${JSON.stringify(text)});if(!label)throw Error('Missing '+${JSON.stringify(text)});label.querySelector('input').click();})()`,
    );
  await preferences();
  await waitText("owner@example.invalid");
  await event();
  await click("Refresh inbox");
  await until(
    async () => (await inbox()).items.length > 0,
    "Durable alert before email consent",
  );
  assert.equal((await sent()).length, 0);
  await check("Email me about new work");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Save notification preferences').disabled",
    ),
    true,
  );
  await check(
    "I authorize these generic notices to my verified address for 30 days, within this daily limit.",
  );
  await evaluate(
    "window.originalInboxFetch=window.fetch;window.loseInboxPreference=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseInboxPreference&&body.action==='notification.preferences'){window.loseInboxPreference=false;await window.originalInboxFetch(input,init);throw new TypeError('Fictional lost preference reply');}return window.originalInboxFetch(input,init);};",
  );
  await click("Save notification preferences");
  await waitText("Notification preferences saved.");
  assert.equal((await inbox()).preferences.version, 1);
  assert.equal(
    (await sent()).length,
    0,
    "Opt-in does not send historical alerts",
  );
  await event();
  await until(
    async () => (await inbox()).deliveries.some((d) => d.state === "accepted"),
    "Background notification accepted",
  );
  assert.equal((await sent()).length, 1);
  const envelope = (await sent())[0];
  assert.equal(envelope.recipient, "owner@example.invalid");
  assert.ok(envelope.body.includes("company=local-company"));
  assert.ok(!envelope.body.includes("private result"));
  await click("Refresh inbox");
  const unreadBefore = (await inbox()).unread;
  await evaluate(
    "[...document.querySelectorAll('.co-inbox-items button')].find(b=>b.textContent==='Mark read').click()",
  );
  await until(
    async () => (await inbox()).unread === unreadBefore - 1,
    "Personal read mark saved",
  );
  await send("Page.reload", { ignoreCache: true });
  await until(
    () => evaluate("!!document.querySelector('.co-inbox-items .is-read')"),
    "Read state survives reload",
  );
  await check("Unread only");
  await until(
    () => evaluate("!document.querySelector('.co-inbox-items .is-read')"),
    "Unread filter",
  );
  await check("Unread only");
  await until(
    () => evaluate("!!document.querySelector('.co-inbox-items .is-read')"),
    "All saved alerts",
  );
  await evaluate(
    "document.querySelector('.co-inbox-items .co-primary').click()",
  );
  await until(
    () => evaluate("location.search.includes('view=employee')"),
    "Result opens its employee conversation",
  );
  await evaluate("document.querySelector('.co-inbox-toggle').click()");
  await waitText("Your workspace inbox");
  await event();
  await until(
    async () => (await inbox()).deliveries.some((d) => d.state === "unknown"),
    "Uncertain notification retained",
  );
  await event();
  await delay(1600);
  assert.equal((await sent()).length, 2, "Uncertain outcome holds later email");
  await preferences();
  await click("Refresh inbox");
  await waitText("Delivery uncertain");
  await click("Keep in inbox; do not resend");
  await until(
    async () => (await sent()).length === 3,
    "Explicit acknowledgment releases only the new alert",
  );
  assert.equal(
    new Set((await sent()).map((e) => e.id)).size,
    3,
    "No uncertain envelope is replayed",
  );
  await click("Refresh inbox");
  await check("Email me about new work");
  await click("Save notification preferences");
  await waitText("Notification preferences saved.");
  await event();
  await delay(1400);
  assert.equal(
    (await sent()).length,
    3,
    "Disabled policy prevents future delivery",
  );
  await click("Refresh inbox");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate("document.querySelector('.co-inbox').scrollIntoView()");
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  const layout = await evaluate(
    "(()=>{const header=document.querySelector('.co-inbox > header');const title=header.querySelector('h2').getBoundingClientRect();const copy=header.querySelector('p:last-child').getBoundingClientRect();const bell=document.querySelector('.co-inbox-toggle').getBoundingClientRect();const cockpit=document.querySelector('.co-cockpit-toggle').getBoundingClientRect();return {stacked:copy.top>=title.bottom,iconsTogether:Math.abs(bell.top-cockpit.top)<2};})()",
  );
  assert.deepEqual(layout, { stacked: true, iconsTogether: true });
  await fs.writeFile(
    path.join(repo, "test-results/inbox-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  const coordinated = await evaluate(
    "fetch('/__fixture/notifications/orchestration',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:'{}'}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json()})",
  );
  const snapshot = () => fetch(url + "/api/company").then((r) => r.json());
  const openInbox = async () => {
    await evaluate("document.querySelector('.co-inbox-toggle').click()");
    await until(
      () =>
        evaluate(
          "!!document.querySelector('.co-inbox') && window.__smokeIdle()",
        ),
      "inbox loaded",
    );
  };
  const openNotice = async (title) => {
    await waitText(title);
    await until(() => evaluate("window.__smokeIdle()"), "inbox ready");
    await evaluate(
      `([...document.querySelectorAll('.co-inbox-items li')].find(li=>li.querySelector('h3').textContent===${JSON.stringify(title)})).querySelector('.co-primary').click()`,
    );
  };
  const focused = async (kind, id) => {
    await until(
      () =>
        evaluate(
          `document.activeElement?.id===${JSON.stringify("co-" + kind + "-" + id)} && document.activeElement.classList.contains('co-notice-target')`,
        ),
      "exact " + kind + " card focused",
    );
    assert.equal(
      await evaluate(
        `new URLSearchParams(location.search).get(${JSON.stringify(kind === "plans" ? "plan" : "meeting")})`,
      ),
      id,
    );
  };
  await click("Refresh inbox");
  await openNotice("Coordinated work is complete");
  await focused("plans", coordinated.planId);
  await send("Page.reload");
  await focused("plans", coordinated.planId);
  await evaluate("history.back()");
  await until(
    () => evaluate("!!document.querySelector('.co-inbox')"),
    "back restores inbox",
  );
  await openNotice("A company plan needs approval");
  await focused("plans", coordinated.proposalId);
  assert.equal(
    (await snapshot()).data.plans.find((p) => p.id === coordinated.proposalId)
      .state,
    "proposed",
    "Opening an alert never approves a plan",
  );
  await openInbox();
  await openNotice("Meeting discussion is ready for minutes");
  await focused("meetings", coordinated.meetingId);
  const taskCount = (await snapshot()).data.tasks.length;
  const today = await fetch(url + "/api/company/today").then((r) => r.json());
  assert.equal(
    today.needsYou.find((n) => n.kind === "meeting").targetId,
    coordinated.meetingId,
  );
  const meetingTasks = (await snapshot()).data.tasks
    .filter((t) => t.meetingId === coordinated.meetingId)
    .map((t) => t.id);
  assert.equal(
    today.needsYou.some(
      (n) => n.kind === "review" && meetingTasks.includes(n.taskId),
    ),
    false,
    "The cockpit does not request separate approval for collected discussion turns",
  );
  await evaluate(
    `(()=>{const input=document.getElementById(${JSON.stringify("co-meetings-" + coordinated.meetingId)}).querySelector('textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(input,'Fictional decision: wait. Follow-up requires separate approval.');input.dispatchEvent(new Event('input',{bubbles:true}));})()`,
  );
  await click("Propose minutes");
  await until(
    async () =>
      (await snapshot()).data.meetings.find(
        (m) => m.id === coordinated.meetingId,
      ).state === "awaiting_owner",
    "minutes need separate approval",
  );
  await openInbox();
  await openNotice("Meeting minutes need approval");
  await focused("meetings", coordinated.meetingId);
  for (const [size, width, height] of [
    ["desktop", 1440, 1000],
    ["mobile", 390, 844],
  ]) {
    await send("Emulation.setDeviceMetricsOverride", {
      width,
      height,
      deviceScaleFactor: 1,
      mobile: size === "mobile",
    });
    await evaluate(
      "document.querySelector('.co-notice-target').scrollIntoView({block:'start'})",
    );
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
      true,
    );
    const shot = await send("Page.captureScreenshot", { format: "png" });
    await fs.writeFile(
      path.join(repo, `test-results/orchestration-inbox-${size}.png`),
      Buffer.from(shot.data, "base64"),
    );
  }
  await click("Accept minutes and close");
  await until(
    async () =>
      (await snapshot()).data.meetings.find(
        (m) => m.id === coordinated.meetingId,
      ).state === "closed",
    "minutes accepted",
  );
  assert.equal(
    (await snapshot()).data.tasks.length,
    taskCount,
    "Minutes acceptance never creates follow-up work",
  );
  await openInbox();
  await openNotice("Meeting minutes accepted");
  await focused("meetings", coordinated.meetingId);
  assert.equal(
    (await sent()).length,
    3,
    "New orchestration alerts do not bypass disabled email consent",
  );
  await meetingMemoryJourney({
    send,
    evaluate,
    until,
    click,
    url,
    repo,
    meetingId: coordinated.meetingId,
  });
  assert.equal(
    (await snapshot()).data.tasks.length,
    taskCount,
    "Remembering a decision creates no follow-up tasks",
  );
  await send("Page.navigate", {
    url: url + "/?view=plans&plan=fictional-missing-plan",
  });
  await until(
    () =>
      evaluate(
        "document.body.innerText.includes('This plan is unavailable in this company.')",
      ),
    "unavailable plan stays within the selected company",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-notice-target')"),
    false,
  );
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/inbox-browser.json"),
    JSON.stringify(
      {
        durableInbox: true,
        readSurvivesReload: true,
        unreadFilter: true,
        exactEmailConsent: true,
        lostPreferenceReceiptRecovered: true,
        noHistoricalEmails: true,
        backgroundFakeEmails: 3,
        unknownNotReplayed: true,
        disabledPreventsEmail: true,
        privateResultOpened: true,
        exactPlanAndMeetingTargets: true,
        targetSurvivesReloadAndBack: true,
        meetingOutcomeSeparatelyApproved: true,
        noFollowUpDispatched: true,
        acceptedMinutesMemory: true,
        mobileOverflow: false,
        liveEmails: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS inbox: delivered, read and recovered; the butler rang once and kept the uncertain receipt.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/inbox-browser-host.log"),
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
