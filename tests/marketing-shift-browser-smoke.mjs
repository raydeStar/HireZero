// Chip's shift (docs/adr/0171-an-owner-approved-shift.md): after a goal ladder and two weeks of numbers, one consent
// starts a finite run of morning rounds. A round reads the numbers and recent work, comes back with jobs and the first
// one drafted, and the next job starts with one click. Fictional providers and a fictional clock only.
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
      HIREZERO_BROWSER_TEAM: "1",
      HIREZERO_BROWSER_SYNTHETIC_BUSINESS: "1",
      HIREZERO_BROWSER_SCHEDULE_CLOCK: "1",
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
const advance = async (id) => {
  const due = await fetch(url + "/__fixture/schedules/" + id + "/due", {
    method: "POST",
    headers: {
      "X-Dev-Request": "1",
      "Content-Type": "application/json",
      Origin: url,
    },
    body: "{}",
  });
  assert.ok(due.ok, "fictional clock advanced");
};
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "this test host owns the listener",
  );
  await until(
    async () => (await fetch(url + "/health")).ok,
    "fresh host health",
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
  // The classic start: this check covers the form, which an owner can choose over onboarding (ADR 0186).
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: "localStorage.setItem('dev-ui:onboarding', '\"form\"')",
  });
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: "localStorage.setItem('dev-ui:home-start', '\"overview\"')",
  });
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("!!document.querySelector('.co-home')"),
    "company home",
  );
  const click = clicker(evaluate, until),
    idle = pageIdle(evaluate, until);
  const text = (phrase) =>
    until(
      () =>
        evaluate(`document.body.innerText.includes(${JSON.stringify(phrase)})`),
      phrase,
    );
  // Sets a field the way typing would, so React sees the change.
  const fill = (selector, value) =>
    evaluate(`(() => {
      const el = document.querySelector(${JSON.stringify(selector)});
      if (!el) throw new Error('Missing field: ' + ${JSON.stringify(selector)});
      const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement : el.tagName === 'SELECT' ? HTMLSelectElement : HTMLInputElement;
      Object.getOwnPropertyDescriptor(proto.prototype, 'value').set.call(el, ${JSON.stringify(value)});
      el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    })()`);
  const goalField = (label) =>
    `[...document.querySelectorAll('.co-goal-start label')].find(l => l.firstChild.textContent.trim() === ${JSON.stringify(label)})?.querySelector('input,textarea')`;
  const fillGoal = (label, value) =>
    evaluate(`(() => {
      const el = ${goalField(label)};
      if (!el) throw new Error('Goal field missing');
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(el, ${JSON.stringify(value)});
      el.dispatchEvent(new Event('input', { bubbles: true }));
    })()`);
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  const capture = async (name) =>
    fs.writeFile(
      path.join(repo, "test-results/" + name + ".png"),
      Buffer.from(
        (
          await send("Page.captureScreenshot", {
            format: "png",
            captureBeyondViewport: false,
          })
        ).data,
        "base64",
      ),
    );

  // A first result, so the Marketing employee is active.
  await fillGoal(
    "What does your business do?",
    "Fictional team studio sells paper craft kits.",
  );
  await fillGoal(
    "Your goal",
    "Write one welcome email for new paper-kit subscribers.",
  );
  await text("Your marketing draft");
  await until(
    () =>
      evaluate("!!document.querySelector('.co-goal-start .co-check input')"),
    "first-draft terms",
  );
  await evaluate(
    "document.querySelector('.co-goal-start .co-check input').click()",
  );
  await click("Generate my first draft");
  await text("Fictional welcome email");

  // Home offers the ladder; nothing runs until the owner consents.
  await send("Page.navigate", { url: url + "/" });
  await text("What should marketing grow?");
  await until(
    () => evaluate("!!document.querySelector('.co-ladder .co-check input')"),
    "ladder terms",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-ladder button')].find(b => b.textContent === 'Work out my goal ladder').disabled",
    ),
    true,
  );
  assert.equal(
    (await dashboard()).data.measured.length,
    1,
    "Showing the offer runs nothing",
  );
  await evaluate(
    "document.querySelector('.co-ladder .co-check input').click()",
  );
  await click("Work out my goal ladder");
  await text("goal ladder is ready");
  assert.deepEqual(
    await evaluate(
      "[...document.querySelectorAll('.co-ladder-list b')].map(b => b.textContent)",
    ),
    ["See us", "Visit", "Order"],
  );
  await text("Fictional ladder for a browser check.");
  let state = (await dashboard()).data;
  const ladderTask = state.tasks.find(
    (t) => t.skill === "marketing-goal-ladder",
  );
  assert.ok(ladderTask, "The ladder is its own Marketing request");
  assert.equal(state.measured.length, 2, "One model request for one consent");
  assert.equal(
    state.threads.find((t) => t.id === ladderTask.threadId).title,
    "Work out my goal ladder: what to grow, and where to start",
  );

  // The owner shortens the goal before using it.
  await evaluate("document.querySelector('.co-ladder-edit summary').click()");
  await fill(".co-ladder-edit textarea", "New paper-kit orders a week.");
  await idle();
  await click("Use this ladder");
  await text("Your goal ladder");
  await until(
    () =>
      evaluate("document.querySelectorAll('.co-ladder-steps li').length === 3"),
    "three steps",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-ladder-steps li.focus b').textContent",
    ),
    "Order",
  );
  await text("Leave alone for now");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-ladder-focus .co-muted').innerText.includes('paid ads')",
    ),
    true,
  );
  state = (await dashboard()).data;
  const department = state.departments.find(
    (d) => d.id === state.employees[0].departmentId,
  );
  const context = JSON.parse(
    state.knowledge.find((k) => k.id === department.knowledge).body,
  );
  assert.equal(
    context.goal,
    "New paper-kit orders a week.",
    "The owner's edit is what Chip works from",
  );
  assert.match(context.steps, /Visit: website visits a week/);
  assert.equal(
    context.voice !== undefined,
    true,
    "Existing department context is kept",
  );
  assert.equal(
    state.tasks.find((t) => t.id === ladderTask.id).state,
    "completed",
  );
  assert.equal(
    state.artifacts.find((a) => a.taskId === ladderTask.id).state,
    "approved",
  );

  // Two weeks of numbers: code shows the shares and the step that fell most.
  const inputs = ".co-ladder-inputs input";
  const setNumbers = (values) =>
    evaluate(`(() => {
      const fields = [...document.querySelectorAll(${JSON.stringify(inputs)})];
      ${JSON.stringify(values)}.forEach((value, i) => {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(fields[i], value);
        fields[i].dispatchEvent(new Event('input', { bubbles: true }));
      });
    })()`);
  assert.equal(
    await evaluate("document.querySelector('.co-ladder-numbers').open"),
    true,
    "The first week's form is open",
  );
  assert.match(
    await evaluate(
      "document.querySelector('.co-ladder-numbers select').selectedOptions[0].textContent",
    ),
    /^Last week/,
  );
  await setNumbers(["5,000", "1000", "20"]);
  await click("Save numbers");
  await text("1 in 50 of the step before");
  await until(
    () => evaluate("!document.querySelector('.co-ladder-numbers').open"),
    "form folds after the first week",
  );
  await evaluate(
    "document.querySelector('.co-ladder-numbers summary').click()",
  );
  assert.match(
    await evaluate(
      "document.querySelector('.co-ladder-numbers select').selectedOptions[0].textContent",
    ),
    /^This week/,
  );
  await setNumbers(["5200", "1100", "11"]);
  await click("Save numbers");
  await text(
    "Fewer people went from Visit to Order than the week before: 1 in 100, down from 1 in 50.",
  );
  state = (await dashboard()).data;
  assert.equal(state.readings.length, 2);
  assert.deepEqual(
    state.readings.at(-1).values.map((v) => v.value),
    [5200, 1100, 11],
  );
  assert.equal(state.measured.length, 2, "Numbers never call a model");
  // One consent starts a finite shift; nothing runs until its first morning.
  const shiftCard = "section[aria-label=Shift]";
  await until(
    () =>
      evaluate(
        `!!document.querySelector('${shiftCard} .co-shift-consent input')`,
      ),
    "shift offer and its terms",
  );
  assert.equal(
    await evaluate(
      `document.querySelector('${shiftCard} input[type=number]').value`,
    ),
    "14",
  );
  await fill(`${shiftCard} input[type=number]`, "3");
  await evaluate(
    `document.querySelector('${shiftCard} .co-shift-consent input').click()`,
  );
  await click("Start the shift");
  await text("is on shift");
  await text("0 of 3 morning rounds done");
  const schedules = await fetch(url + "/api/company/schedules").then((r) =>
    r.json(),
  );
  const shift = schedules.find(
    (s) => s.definition.skill === "marketing-morning",
  );
  assert.equal(shift.state, "active");
  assert.equal(shift.definition.occurrences, 3);
  assert.equal(shift.definition.includeOccurrenceTime, true);
  assert.equal(shift.definition.readContextPolicy, "marketing-site-signals-v1");
  assert.equal(
    shift.definition.cadence,
    "weekdays",
    "Shifts default to weekday mornings",
  );
  assert.equal(
    (await dashboard()).data.measured.length,
    2,
    "Starting a shift runs nothing yet",
  );
  // Once a shift exists it leads Home, right after the greeting and before earlier results and the ladder.
  assert.equal(
    await evaluate(
      "document.querySelector('.co-home > .co-home-hello + section').getAttribute('aria-label')",
    ),
    "Shift",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-shift').compareDocumentPosition(document.querySelector('.co-ladder')) & Node.DOCUMENT_POSITION_FOLLOWING ? 1 : 0",
    ),
    1,
  );

  // The first morning arrives on the fictional clock.
  await advance(shift.id);
  await text("Rewrite the shop page's opening lines");
  await text("1 of 3 morning rounds done");
  state = (await dashboard()).data;
  const round = state.tasks.find((t) => t.skill === "marketing-morning");
  assert.ok(round, "The round is its own Marketing request");
  const packet = await fetch(
    url +
      "/api/company/measured/" +
      state.measured.find((m) => m.taskId === round.id).id,
  ).then((r) => r.json());
  assert.equal(
    packet.call.context.scorecard.length,
    2,
    "The round reads both weeks of numbers",
  );
  assert.equal(
    packet.call.context.freshSignals.policy,
    "marketing-site-signals-v1",
  );
  assert.ok(
    packet.call.context.recentWork.some(
      (w) =>
        w.title === "Work out my goal ladder: what to grow, and where to start",
    ),
  );
  assert.equal(
    await evaluate("document.querySelectorAll('.co-shift-jobs li').length"),
    2,
  );
  await text("Do people need a card to order? (yes / no)");

  // The desk (2026-11-06): Home as Chip's conversation, with the shift's state in its header and today's round in the
  // thread. Collapse all folds the thread; Overview returns to the cards.
  await click("Back to chat");
  await until(
    () => evaluate("!!document.querySelector('.co-desk .co-conversation')"),
    "the desk",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-desk-shift')?.innerText.includes('On shift')",
      ),
    "shift state in the header",
  );
  assert.match(
    await evaluate("document.querySelector('.co-desk-shift').innerText"),
    /1 of 3 done/,
  );
  assert.equal(
    await evaluate(
      "!!document.querySelector('.co-desk .fe-thread .co-round-msg')",
    ),
    true,
    "Today's round is a message in the thread",
  );
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-desk .co-round-msg .co-shift-jobs li').length",
    ),
    2,
  );
  assert.match(
    await evaluate(
      "document.querySelector('.co-desk .co-round-msg').innerText",
    ),
    /Here’s what I’d do today/,
  );
  // The employee answers your approvals in the chat (ADR 0185): the ladder, then the shift, then the round.
  assert.match(
    await evaluate(
      "document.querySelector('.co-desk [aria-label$=\"on the approved goal ladder\"]')?.innerText ?? ''",
    ),
    /Thanks, that’s our plan/,
  );
  assert.match(
    await evaluate(
      "document.querySelector('.co-desk [aria-label$=\"on starting the shift\"]')?.innerText ?? ''",
    ),
    /I’m on shift\. Each morning at .* I’ll read your goal ladder/,
  );
  assert.equal(
    await evaluate(
      "(() => { const a = document.querySelector('.co-desk [aria-label$=\"on starting the shift\"]'), b = document.querySelector('.co-desk .co-round-msg'); return !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING); })()",
    ),
    true,
    "The shift is acknowledged before the round arrives",
  );
  await evaluate(
    "document.querySelector('.co-desk [aria-label$=\"on the approved goal ladder\"]').scrollIntoView({ block: 'start' })",
  );
  await capture("desk-acknowledgements");
  assert.match(
    await evaluate(
      "document.querySelector('.co-desk .co-team-note').innerText",
    ),
    /I’m on shift; my next round is/,
  );
  assert.equal(
    await evaluate(
      "getComputedStyle(document.querySelector('.co-desk .co-team-note-line.free') ?? document.body).display === 'none' || !document.querySelector('.co-desk .co-team-note-line.free')",
    ),
    true,
    "On shift, the note does not say it is free",
  );
  await evaluate(
    "document.querySelector('.co-desk .co-round-msg').scrollIntoView({ block: 'start' })",
  );
  await capture("desk-morning-round");
  // Answer puts the employee's question into the message box.
  await click("Answer");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-desk .fe-composer textarea').value.startsWith('About your question')",
      ),
    "question quoted in the message box",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-desk .co-shift-quick')"),
    false,
    "No start form in the thread",
  );
  // Personality (ADR 0184): the role's soul by default, edited per employee from Context & settings.
  await evaluate(
    "document.querySelector('.co-desk .co-private-context').open = true",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-desk .co-personality textarea')?.value.includes('You are a colleague, not a tool.')",
      ),
    "default personality",
  );
  await fill(
    ".co-desk .co-personality textarea",
    "Cheerful and brief. Always say how long a job takes.",
  );
  await click("Save personality");
  await text("Saved. Their next reply uses it.");
  state = (await dashboard()).data;
  assert.equal(
    state.employees[0].personality,
    "Cheerful and brief. Always say how long a job takes.",
  );
  await click("Use the default");
  await until(
    async () => !(await dashboard()).data.employees[0].personality,
    "personality back to the default",
  );
  // About you (ADR 0185): each person's own, beside the company everyone shares.
  await until(
    () =>
      evaluate("!!document.querySelector('.co-desk .co-about-you textarea')"),
    "about you",
  );
  assert.match(
    await evaluate(
      "document.querySelector('.co-desk .co-about-company').innerText",
    ),
    /Everyone here shares this/,
  );
  await fill(
    ".co-desk .co-about-you textarea",
    "Call me Sam. I like short updates before 9.",
  );
  await click("Save about you");
  await text("uses it from their next reply.");
  const members = await fetch(url + "/api/company/members").then((r) =>
    r.json(),
  );
  assert.equal(
    members.find((m) => m.about)?.about,
    "Call me Sam. I like short updates before 9.",
  );
  await evaluate(
    "document.querySelector('.co-desk .co-private-context').open = false",
  );
  await click("Collapse all");
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-desk .fe-thread details')].every((d) => !d.open)",
      ),
    "folded thread",
  );
  await click("Expand all");
  await click("Overview");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-home section[aria-label=Shift]')",
      ),
    "overview again",
  );

  // A failed morning lookup is not an empty schedule list or permission to offer a second shift.
  const unavailableShift = await send("Page.addScriptToEvaluateOnNewDocument", {
    source: `(()=>{
    const original=window.fetch;window.fetch=(input,options)=>{
      if(!window.allowShiftDetails&&String(input)==='/api/company/schedules'){
        return Promise.reject(new TypeError('Fictional unavailable shift'));
      }return original(input,options);
    };
  })()`,
  });
  const callsBeforeReload = (await dashboard()).data.measured.length;
  await send("Page.reload");
  await text("Retry morning work");
  assert.equal(
    await evaluate("document.body.innerText.includes('Start the shift')"),
    false,
  );
  await evaluate("window.allowShiftDetails=true");
  await click("Retry morning work");
  await text("Read the draft");
  await send("Page.removeScriptToEvaluateOnNewDocument", {
    identifier: unavailableShift.identifier,
  });
  assert.equal(
    (await dashboard()).data.measured.length,
    callsBeforeReload,
    "Retry only reads saved morning work",
  );

  // A veto disables an unstarted proposal immediately; undo does not start it.
  await idle();
  await evaluate(
    "[...document.querySelectorAll('.co-shift-jobs > li:nth-child(2) button')].find(b=>b.textContent==='Don’t suggest this again').click()",
  );
  await text("Vetoed · won’t start again");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-shift-jobs > li:nth-child(2) button')].some(b=>b.textContent==='Do it')",
    ),
    false,
  );
  await click("Undo veto");
  await text("Decision removed. Nothing has been restarted.");
  assert.equal((await dashboard()).data.measured.length, callsBeforeReload);

  // Job 2 starts with one click: one more private draft, in its own conversation.
  const unavailableTerms = await send("Page.addScriptToEvaluateOnNewDocument", {
    source: `(()=>{
    const original=window.fetch;window.fetch=(input,options)=>{
      if(!window.allowRunDetails&&String(input).includes('/company/goals/terms?')&&String(input).includes('marketing-goal-draft')){
        return Promise.reject(new TypeError('Fictional unavailable run details'));
      }return original(input,options);
    };
  })()`,
  });
  await send("Page.reload");
  await text("Reload run details");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-shift-jobs button')].find(b=>b.textContent==='Do it').disabled",
    ),
    true,
  );
  await evaluate("window.allowRunDetails=true");
  await click("Reload run details");
  await send("Page.removeScriptToEvaluateOnNewDocument", {
    identifier: unavailableTerms.identifier,
  });
  await text("It uses your plan's included usage.");
  const beforeStorageRefusal = (await dashboard()).data.tasks.length;
  await evaluate(`window.originalStorageSet=Storage.prototype.setItem;Storage.prototype.setItem=function(k,v){
    if(k.startsWith('hirezero-shift-job-v1:'))throw new Error('Fictional storage refusal');
    return window.originalStorageSet.call(this,k,v);
  };`);
  await click("Do it");
  await text("Fictional storage refusal");
  assert.equal(
    (await dashboard()).data.tasks.length,
    beforeStorageRefusal,
    "No mutation before the recovery receipt is saved",
  );
  await evaluate("Storage.prototype.setItem=window.originalStorageSet");
  await evaluate(`(()=>{const original=window.fetch;window.fetch=async(input,options)=>{
    if(String(input).includes('/company/operations/')) throw new TypeError('Fictional unavailable lookup');
    if(String(input)==='/api/company/commands'&&JSON.parse(options?.body||'{}').action==='goal.prepare'){
      await original(input,options);throw new TypeError('Fictional lost prepare reply');
    }
    return original(input,options);
  };})()`);
  await click("Do it");
  await text("Check saved request");
  await until(
    () =>
      evaluate(
        "!document.querySelector('.co-shift-recovery button')?.disabled",
      ),
    "uncertain preparation stopped",
  );
  const preparedCount = (await dashboard()).data.tasks.length;
  await send("Page.reload");
  await text("Check saved request");
  // Reconcile the preparation, then lose the run reply too. Neither uncertainty permits a second dispatch.
  await evaluate(`(()=>{const original=window.fetch;let runSent=false;window.fetch=async(input,options)=>{
    if(runSent&&String(input).includes('/company/operations/')) throw new TypeError('Fictional unavailable run lookup');
    if(String(input)==='/api/company/commands'&&JSON.parse(options?.body||'{}').action==='measured.run'){
      runSent=true;await original(input,options);throw new TypeError('Fictional lost run reply');
    }
    return original(input,options);
  };})()`);
  await click("Check saved request");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-shift-report [role=alert]') && !document.querySelector('.co-shift-recovery button')?.disabled",
      ),
    "uncertain dispatch stopped",
  );
  await send("Page.reload");
  await text("Check saved request");
  await click("Check saved request");
  await until(
    () => evaluate("!document.querySelector('.co-shift-recovery')"),
    "saved dispatch reconciled",
  );
  await text("Started · open it");
  assert.equal(
    (await dashboard()).data.tasks.length,
    preparedCount,
    "Recovery never creates another task",
  );
  state = (await dashboard()).data;
  const job = state.tasks.find(
    (t) =>
      t.skill === "marketing-goal-draft" &&
      state.threads.find((h) => h.id === t.threadId)?.title ===
        "Write one welcome email for new kit buyers",
  );
  assert.ok(job, "The job keeps its title");
  await until(
    async () =>
      (await dashboard()).data.artifacts.some((a) => a.taskId === job.id),
    "the job's draft",
  );
  assert.equal(
    (await dashboard()).data.measured.length,
    4,
    "One request for the round and one for the job",
  );

  // A lesson the owner keeps reaches every later round, without stopping the shift.
  await text("Lessons from this week");
  await evaluate(`(()=>{const original=window.fetch;window.fetch=async(input,options)=>{
    if(String(input).includes('/company/operations/')) throw new TypeError('Fictional unavailable lesson lookup');
    if(String(input)==='/api/company/commands'&&JSON.parse(options?.body||'{}').action==='memory.keep'){
      await original(input,options);throw new TypeError('Fictional lost lesson reply');
    }
    return original(input,options);
  };})()`);
  await click("Keep");
  await until(
    () => evaluate("!!document.querySelector('.co-shift-report [role=alert]')"),
    "uncertain lesson receipt",
  );
  await send("Page.reload");
  await text("Kept");
  const memory = await fetch(url + "/api/company/memory").then((r) => r.json());
  const lesson = memory.find(
    (v) =>
      v.memory.definition.statement ===
      "Posts that name the starter kit brought the most questions this week.",
  );
  assert.equal(lesson.memory.state, "approved");
  assert.equal(lesson.memory.definition.employeeId, round.employeeId);
  assert.equal(
    memory.filter(
      (v) =>
        v.memory.definition.statement === lesson.memory.definition.statement,
    ).length,
    1,
  );

  // A finished job is a change; the ladder judges the share reaching the focus step by the fixed rule, once enough weeks exist.
  state = (await dashboard()).data;
  const jobDraft = state.artifacts.find((a) => a.taskId === job.id);
  await command("artifact.review", {
    id: jobDraft.id,
    expectedVersion: jobDraft.version,
    digest: jobDraft.digest,
    decision: "approve",
  });
  const approvedJob = (await dashboard()).data.tasks.find(
    (t) => t.id === job.id,
  );
  await command("task.complete", {
    id: approvedJob.id,
    expectedVersion: approvedJob.version,
  });
  await send("Page.navigate", { url: url + "/" });
  await text("Changes being measured at Visit → Order");
  await text("Too early to judge");

  // Owner feedback is a decision, not an edited model artifact or another model call.
  const originalRound = (await dashboard()).data.artifacts.find(
    (a) => a.taskId === round.id,
  );
  const countBeforeFeedback = (await dashboard()).data.measured.length;
  await click("That’s inaccurate");
  assert.equal(
    await evaluate(
      "document.activeElement===document.querySelector('.co-shift-jobs > li:first-child textarea')",
    ),
    true,
    "Correction opens and focuses beside the selected proposal",
  );
  await evaluate(
    "document.querySelector('.co-shift-jobs > li:first-child').scrollIntoView({block:'start'})",
  );
  await capture("marketing-shift-correction-desktop");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
    "The inline correction fits on a phone",
  );
  await evaluate(
    "document.querySelector('.co-shift-jobs > li:first-child').scrollIntoView({block:'start'})",
  );
  await capture("marketing-shift-correction-mobile");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await fill(
    ".co-shift-jobs > li:first-child textarea",
    "We serve local shops only. Do not assume we sell online.",
  );
  await click("Save correction");
  await text("Correction saved for this employee’s future work.");
  await evaluate(
    "[...document.querySelectorAll('.co-shift-jobs > li:first-child button')].find(b=>b.textContent==='Don’t suggest this again').click()",
  );
  await text("Vetoed · won’t start again");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-shift-jobs > li:first-child button')].some(b=>b.textContent==='Read the draft')",
    ),
    true,
    "A veto keeps completed work readable",
  );
  await click("Read the draft");
  await until(
    () =>
      evaluate("new URLSearchParams(location.search).get('work')").then(
        (id) => id === round.id,
      ),
    "the saved draft opens directly",
  );
  await send("Page.navigate", { url: url + "/" });
  await text("Vetoed · won’t start again");
  assert.equal((await dashboard()).data.measured.length, countBeforeFeedback);
  assert.equal(
    (await dashboard()).data.artifacts.find((a) => a.id === originalRound.id)
      .body,
    originalRound.body,
  );

  // The next weekday's round reads the kept lesson.
  await advance(shift.id);
  await until(
    async () =>
      (await dashboard()).data.tasks.filter(
        (t) => t.skill === "marketing-morning",
      ).length === 2,
    "the next round",
  );
  state = (await dashboard()).data;
  const second = state.tasks
    .filter((t) => t.skill === "marketing-morning")
    .at(-1);
  await until(
    async () =>
      (await dashboard()).data.artifacts.some((a) => a.taskId === second.id),
    "the next round's report",
  );
  state = (await dashboard()).data;
  const nextPacket = await fetch(
    url +
      "/api/company/measured/" +
      state.measured.find((m) => m.taskId === second.id).id,
  ).then((r) => r.json());
  assert.ok(
    nextPacket.call.context.lessons.some(
      (l) =>
        l.statement ===
        "Posts that name the starter kit brought the most questions this week.",
    ),
  );
  assert.equal(nextPacket.call.context.ownerDirections.length, 2);
  assert.ok(
    nextPacket.call.context.ownerDirections.some(
      (d) =>
        d.kind === "veto" &&
        d.subject === "Rewrite the shop page's opening lines",
    ),
  );
  assert.ok(
    nextPacket.call.context.ownerDirections.some(
      (d) =>
        d.kind === "correction" &&
        d.subject === "Rewrite the shop page's opening lines" &&
        d.statement.includes("local shops only"),
    ),
  );
  assert.equal(
    new Date(
      nextPacket.call.context.scheduledOccurrence.scheduledFor,
    ).getDay() %
      6 !==
      0,
    true,
    "A weekday round",
  );
  await text("2 of 3 morning rounds done");
  await text("Vetoed · won’t start again");
  const callsBeforeUndo = (await dashboard()).data.measured.length;
  await click("Undo veto");
  await text("Decision removed. Nothing has been restarted.");
  assert.equal((await dashboard()).data.measured.length, callsBeforeUndo);
  await capture("marketing-shift-desktop");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
    "No sideways scroll on a phone",
  );
  await evaluate(
    "document.querySelector('.co-shift').scrollIntoView({ block: 'start' })",
  );
  await capture("marketing-shift-mobile");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });

  // Pausing stops later mornings; the round already done stays readable.
  await click("Pause");
  await text("shift is paused");
  assert.equal(
    (await fetch(url + "/api/company/schedules").then((r) => r.json())).find(
      (s) => s.id === shift.id,
    ).state,
    "paused",
  );
  await text("Rewrite the shop page's opening lines");
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/marketing-shift-browser.json"),
    JSON.stringify(
      {
        verifiedAt: new Date().toISOString(),
        oneConsentShift: true,
        nothingRunsBeforeTheMorning: true,
        roundReadsNumbersAndRecentWork: true,
        jobsAndFirstDraft: true,
        oneClickJob: true,
        lostPrepareAndRunRepliesRecoverAcrossReload: true,
        weekdayCadence: true,
        keptLessonReachesNextRound: true,
        optedInReadPlan: true,
        correctionAndVetoSurviveReloadAndNextRound: true,
        failedShiftLookupHasReadOnlyRetry: true,
        correctionTargetsSelectedProposal: true,
        vetoKeepsDraftReadableAndBlocksUnstartedJob: true,
        rawDraftUnchanged: true,
        undoStartsNothing: true,
        changeMeasuredByRule: true,
        pause: true,
        mobileWithoutSidewaysScroll: true,
        liveModelCalls: 0,
        externalRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS marketing shift: weekday rounds with numbers and recent work, a one-click job, a kept lesson in the next round, a measured change and a pause.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  try {
    const failureState = await fetch(url + "/api/company", {
      signal: AbortSignal.timeout(5000),
    }).then((r) => r.json());
    await fs.writeFile(
      path.join(repo, "test-results/marketing-shift-browser-failure.json"),
      JSON.stringify(failureState, null, 2),
    );
  } catch {
    /* Retain the original failure if the fixture is already unavailable. */
  }
  await fs.writeFile(
    path.join(repo, "test-results/marketing-shift-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  const stop = async (child) => {
    if (!child || child.exitCode !== null || child.signalCode !== null) return;
    await new Promise((resolve, reject) => {
      const timer = setTimeout(
        () =>
          reject(new Error("Owned fixture did not exit; retaining its files.")),
        10000,
      );
      child.once("exit", () => {
        clearTimeout(timer);
        resolve();
      });
      child.kill();
    });
  };
  await Promise.all([stop(browser), stop(host)]);
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
