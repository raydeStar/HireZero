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
      HIREZERO_BROWSER_SCHEDULE_CLOCK: "1",
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
  until.diagnose = async () =>
    (await pageDiagnosis(evaluate)()) +
    "\n" +
    JSON.stringify(
      await evaluate(
        "({schedule:document.querySelector('.co-schedule-review')?.innerText,check:document.querySelector('.co-schedule-controls input')?.checked,disabled:document.querySelector('.co-schedule-controls input')?.disabled})",
      ),
    );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.navigate", {
    url: url + "/?view=departments&section=schedules",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-schedules')"),
    "Recurring work page",
  );
  const click = clicker(evaluate, until);
  const post = (action, fields = {}) =>
    evaluate(
      `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:${JSON.stringify(action)},...${JSON.stringify(fields)}})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})`,
    );
  const snapshot = () => fetch(url + "/api/company").then((r) => r.json());
  const schedules = () =>
    fetch(url + "/api/company/schedules").then((r) => r.json());
  const detail = (id) =>
    fetch(url + "/api/company/schedules/" + id).then((r) => r.json());
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-schedules')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const consent = async () => {
    await until(
      () =>
        evaluate(
          "(() => {const input=document.querySelector('.co-schedule-controls input[type=checkbox]');return input && !input.disabled && window.__smokeIdle();})()",
        ),
      "schedule review is ready for consent",
    );
    await evaluate(
      "document.querySelector('.co-schedule-controls input[type=checkbox]').click()",
    );
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
  const field = async (label, value, scope = ".co-schedule-editor") => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll(${JSON.stringify(scope + " label")})].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing form field '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(50);
  };
  let identity = await post("business.identify", {
    body: JSON.stringify({
      name: "Fictional Paper Studio",
      website: "https://paper.example.invalid/",
      goal: "Plan recurring useful work",
    }),
  });
  identity = await post("business.confirm", {
    expectedVersion: identity.version,
    digest: identity.digest,
  });
  const source = await evaluate(
    `fetch('/api/company/business/research',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({businessVersion:${identity.version},digest:${JSON.stringify(identity.digest)},url:'https://paper.example.invalid/about'})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})`,
  );
  const fields = [
    "business",
    "services",
    "customers",
    "voice",
    "market",
    "competitors",
    "opportunities",
    "goals",
    "boundaries",
  ].map((key) =>
    key === "services"
      ? {
          key,
          value: "Paper craft kits for adult hobbyists",
          verification: "sourced",
          citations: [
            {
              sourceId: source.id,
              quote: "Paper craft kits for adult hobbyists",
            },
          ],
        }
      : { key, value: "Unknown", verification: "unknown", citations: [] },
  );
  let profile = await post("business.profile.save", {
    body: JSON.stringify({ businessVersion: identity.version, fields }),
  });
  profile = await post("business.profile.review", {
    id: profile.id,
    expectedVersion: profile.version,
    digest: profile.digest,
    decision: "approve",
  });
  const dept = await post("department.create", {
    name: "Fictional marketing team",
  });
  const employee = await post("employee.hire", {
    departmentId: dept.id,
    roleId: "marketing",
    name: "Recurring fixture",
    responsibility: "Prepare private content outlines from approved facts",
  });
  const knowledge = await post("knowledge.save", {
    departmentId: dept.id,
    body: JSON.stringify({
      purpose: "Fictional paper kits",
      audience: "Adult hobbyists",
      positioning: "Paper craft studio",
      voice: "Clear",
      claims: "Paper and printed guide",
      constraints: "Private drafts only; no publishing or unsupported claims",
    }),
  });
  await post("knowledge.review", {
    id: knowledge.id,
    expectedVersion: knowledge.version,
    digest: knowledge.digest,
    decision: "approve",
  });
  const firstState = (await snapshot()).data;
  const privateThread = firstState.threads.find(
    (t) => t.employeeId === employee.id,
  );
  const originalTask = await post("task.create", {
    employeeId: employee.id,
    threadId: privateThread.id,
    skill: "marketing-starter-plan",
    body: "Prepare three content themes from the approved company facts.",
  });
  await post("measured.prepare", {
    id: originalTask.id,
    expectedVersion: originalTask.version,
    creditAllowance: 1,
  });
  await send("Page.navigate", {
    url:
      url +
      `/?view=employee&employee=${employee.id}&thread=${privateThread.id}&work=${originalTask.id}&mode=work`,
  });
  await click("Review task and context");
  await until(
    () => evaluate("!!document.querySelector('.co-work-window .co-packet')"),
    "first deliverable context review",
  );
  await evaluate(
    "document.querySelector('.co-work-window .co-measured input[type=checkbox]').click()",
  );
  await click("Authorize and run one job");
  await until(
    async () =>
      (await snapshot()).data.artifacts.some(
        (a) => a.taskId === originalTask.id,
      ),
    "First deliverable saved for editing",
  );
  const generated = (await snapshot()).data;
  const originalResult = generated.artifacts.find(
    (a) => a.taskId === originalTask.id,
  );
  const earlierFeedback =
    "Include a complete folding checklist, not just content themes.";
  const fullEarlierFeedback =
    earlierFeedback +
    "\n" +
    "Keep a complete fictional folding checklist. ".repeat(55);
  const latestFeedback =
    "Keep the checklist. Replace a broad closing question with a specific reader action.";
  const firstRevision = await post("artifact.revise", {
    id: originalResult.id,
    expectedVersion: originalResult.version,
    digest: originalResult.digest,
    body: fullEarlierFeedback,
  });
  const firstRevisionResult = await post("artifact.create", {
    id: firstRevision.id,
    expectedVersion: firstRevision.version,
    body: "Fictional intermediate checklist for owner correction.",
  });
  const acceptedTask = await post("artifact.revise", {
    id: firstRevisionResult.id,
    expectedVersion: firstRevisionResult.version,
    digest: firstRevisionResult.digest,
    body: latestFeedback,
  });
  const editedResult = await post("artifact.create", {
    id: acceptedTask.id,
    expectedVersion: acceptedTask.version,
    body: "Fictional edited plan: give readers a complete folding checklist. Align the corners, crease the diagonal, and check the edge. Ask which step they used or changed.",
  });
  await send("Page.navigate", {
    url: `${url}/?view=employee&employee=${employee.id}&thread=${privateThread.id}&work=${acceptedTask.id}&mode=work`,
  });
  await until(
    () =>
      evaluate(
        `!!document.querySelector('[data-artifact-id="${editedResult.id}"]')`,
      ),
    "Edited result ready for review",
  );
  await evaluate(
    `[...document.querySelectorAll('[data-artifact-id="${editedResult.id}"] button')].find(b=>b.textContent.trim()==='Approve result').click()`,
  );
  await click("Complete after review");
  await until(
    async () =>
      (await snapshot()).data.tasks.find((t) => t.id === acceptedTask.id)
        ?.state === "completed",
    "first useful work accepted",
  );
  await evaluate(
    "sessionStorage.setItem('hirezero-schedule-draft-v1:local-company', JSON.stringify({name:'Unfinished original draft',kind:'employee-task',prompt:'Preserve these unrelated instructions'}))",
  );
  await click("Repeat this work");
  await until(
    () => evaluate("!!document.querySelector('.co-schedule-editor')"),
    "completed work becomes a separate schedule draft",
  );
  assert.ok(await evaluate("location.search.includes('repeatWork=')"));
  const referenceSection = '[aria-label="Selected result references"]';
  assert.deepEqual(
    await evaluate(
      `[...document.querySelectorAll('${referenceSection} [data-reference-id]')].map(el=>el.dataset.referenceId)`,
    ),
    [editedResult.id],
    "Only the approved edited result is included in the new recurring draft",
  );
  await evaluate(
    `document.querySelector('${referenceSection} summary').click()`,
  );
  assert.equal(
    await evaluate(
      `document.querySelector('${referenceSection} pre').innerText`,
    ),
    editedResult.body,
    "The owner can inspect the exact accepted copy before saving",
  );
  assert.equal(
    (await snapshot()).data.artifacts.find((a) => a.id === originalResult.id)
      .state,
    "changes_requested",
  );
  assert.equal(
    await evaluate(
      "JSON.parse(sessionStorage.getItem('hirezero-schedule-draft-v1:local-company')).name",
    ),
    "Unfinished original draft",
  );
  const copiedInstructions = await evaluate(
    "document.querySelector('.co-schedule-editor textarea').value",
  );
  assert.ok(copiedInstructions.startsWith(originalTask.input));
  assert.ok(
    copiedInstructions.includes(firstRevision.revision.feedback),
    "Exact saved feedback reaches the draft without truncation",
  );
  assert.ok(
    copiedInstructions.indexOf(latestFeedback) >
      copiedInstructions.indexOf(earlierFeedback),
  );
  assert.ok(
    copiedInstructions.length > 2000,
    "Oversized revision history is kept in full for explicit consolidation",
  );
  await waitText("Consolidate the request and feedback");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedule-editor button[type=submit]').disabled",
    ),
    true,
  );
  await evaluate(
    "sessionStorage.removeItem('hirezero-schedule-draft-v1:local-company')",
  );
  await field("Schedule name", "Fictional content rhythm");
  const instructionsForRuns =
    originalTask.input +
    "\n\nOwner feedback (consolidated):\n" +
    earlierFeedback +
    "\n" +
    latestFeedback +
    "\n\nUse a different fictional example each time.";
  await field("Instructions for each run", instructionsForRuns);
  await field("Repeat", "daily");
  await field("Number of occurrences", "2");
  await field("Time zone", "UTC");
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-editor textarea')?.value.includes('three content themes')",
      ),
    "Unsaved schedule survives reload",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedule-editor textarea').value",
    ),
    instructionsForRuns,
    "Restoring a saved draft never replaces owner consolidation",
  );
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
    "document.querySelector('.co-schedule-editor').scrollIntoView({behavior:'instant',block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await screenshot("recurring-deliverable-draft-mobile.png");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await evaluate(
    "window.originalScheduleFetch=window.fetch;window.loseScheduleSave=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseScheduleSave&&body.action==='schedule.save'){window.loseScheduleSave=false;await window.originalScheduleFetch(input,init);throw new TypeError('Fictional lost save reply');}return window.originalScheduleFetch(input,init);};",
  );
  await click("Save schedule draft");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review')?.textContent.includes('draft')",
      ),
    "saved copied schedule review",
  );
  let records = await schedules();
  assert.equal(records.length, 1);
  assert.equal(records[0].definition.skill, "marketing-starter-plan");
  assert.equal(records[0].definition.prompt, instructionsForRuns);
  assert.equal(
    records[0].definition.includeOccurrenceTime,
    true,
    "New recurring work includes reviewed calendar context by default",
  );
  await waitText("Each request includes its scheduled date and time");
  assert.deepEqual(records[0].definition.evidence, [editedResult.id]);
  assert.ok(
    records[0].authority.context.selectedResults.includes(editedResult.body),
  );
  assert.equal(
    records[0].authority.context.selectedResults.includes(originalResult.body),
    false,
    "Unapproved original output does not enter the schedule authority",
  );
  assert.equal(records[0].authority.policy.planningTokens, 22000);
  assert.equal(records[0].authority.policy.outputTarget, 6000);
  await waitText("6,000 observed output-token target per run");
  await waitText("Reasoning tokens count toward that target");
  await waitText("Time and token targets are soft limits");
  const id = records[0].id;
  assert.equal(
    await evaluate("new URLSearchParams(location.search).get('schedule')"),
    id,
  );
  assert.equal(
    await evaluate("new URLSearchParams(location.search).has('repeatWork')"),
    false,
  );
  await send("Page.reload");
  await until(
    () => evaluate("!!document.querySelector('.co-schedule-review')"),
    "saved schedule reloads directly",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-schedule-editor')"),
    false,
  );
  await click("Edit schedule draft");
  await field("Schedule name", "Fictional weekly outline");
  await click("Save schedule draft");
  await waitText("Schedule draft saved.");
  assert.equal((await schedules()).length, 1);
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-schedule-review button')].find(b=>b.textContent.trim()==='Authorize schedule').disabled",
    ),
    true,
  );
  await evaluate(
    "document.querySelector('.co-schedule-review').scrollIntoView({behavior:'instant',block:'start'})",
  );
  await screenshot("recurring-deliverable-review.png");
  await consent();
  await click("Authorize schedule");
  await waitText("Schedule active.");
  let approved = (await detail(id)).schedule;
  assert.equal(approved.consumed, 0);
  const advance = async (scheduleId) =>
    evaluate(
      `fetch('/__fixture/schedules/${scheduleId}/due',{method:'POST',headers:{'X-Dev-Request':'1','Content-Type':'application/json'},body:'{}'}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})`,
    );
  await advance(id);
  await until(
    async () => (await detail(id)).runs[0]?.state === "completed",
    "First real background occurrence",
  );
  await click("Refresh schedule status");
  await waitText("1 of 2 occurrences consumed.");
  let first = await detail(id);
  assert.equal(first.runs.length, 1);
  const firstAttempt = (await snapshot()).data.measured.find(
    (m) => m.taskId === first.runs[0].taskId,
  );
  const firstPacket = await fetch(
    url + "/api/company/measured/" + firstAttempt.id,
  ).then((r) => r.json());
  assert.ok(firstPacket.call.context.input.includes(earlierFeedback));
  assert.equal(firstPacket.call.context.scheduledOccurrence.runNumber, 1);
  assert.equal(
    firstPacket.call.context.scheduledOccurrence.timeZone,
    approved.definition.timeZone,
  );
  assert.equal(
    Date.parse(firstPacket.call.context.scheduledOccurrence.scheduledFor),
    Date.parse(first.runs[0].dueAt),
  );
  assert.ok(firstPacket.call.context.input.includes(latestFeedback));
  assert.equal(
    (await snapshot()).data.budgets.find((b) => b.id === "schedule:" + id).used,
    30,
  );
  await click("Open saved result");
  await until(
    () =>
      evaluate(
        "document.body.innerText.includes('Fictional weekly outline: demonstrate paper folding')",
      ),
    "Saved private scheduled result opens directly",
  );
  const beforeEdit = (await snapshot()).data;
  await evaluate("document.querySelector('.co-job-receipt > summary').click()");
  await click("View scheduled request date");
  await until(
    () =>
      evaluate(
        "document.querySelector('[aria-label=\"Scheduled date in this request\"]')?.innerText.includes('Run 1 of 2')",
      ),
    "The saved occurrence date is readable in its execution receipt",
  );
  await screenshot("recurring-request-date.png");
  await evaluate("document.querySelector('.co-job-receipt > summary').click()");
  assert.equal(
    await evaluate("!!document.querySelector('.fe-composer')"),
    false,
    "Opening a saved deliverable prioritizes the full Work view",
  );
  assert.ok(
    await evaluate(
      "document.querySelector('.co-work-window').getBoundingClientRect().width > 700",
    ),
    "The desktop result has room for readable copy",
  );
  const originalOccurrenceArtifact = beforeEdit.artifacts.find(
    (a) => a.taskId === first.runs[0].taskId,
  );
  const scheduleBeforeEdit = await detail(id);
  await click("Edit a copy");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-artifact-editor textarea').value",
    ),
    originalOccurrenceArtifact.body,
  );
  const correctedOccurrence =
    originalOccurrenceArtifact.body +
    "\n\nOwner correction: show the completed paper-folding example before asking readers to try it.";
  await evaluate(
    `(()=>{const el=document.querySelector('.co-artifact-editor textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(el,${JSON.stringify(correctedOccurrence)});el.dispatchEvent(new Event('input',{bubbles:true}));})()`,
  );
  // Split chat deliberately starts at 1600px; narrower desktops keep the copy readable in one pane.
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1700,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await until(
    () => evaluate("innerWidth >= 1600"),
    "wide desktop for explicit split chat",
  );
  await evaluate(
    "document.querySelector('[aria-label=\"Show in chat\"]').click()",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-chat-and-work') && !!document.querySelector('[aria-label=\"Expand work\"]')",
      ),
    "Split chat remains an explicit choice",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-artifact-editor textarea').value",
    ),
    correctedOccurrence,
    "Switching to split chat preserves the unsaved edit",
  );
  assert.equal(
    await evaluate(
      "(()=>{const form=document.querySelector('.co-composer-form');return form.scrollWidth<=form.clientWidth+1 && [...form.querySelectorAll('input,select,small')].every(el=>el.getBoundingClientRect().right<=form.getBoundingClientRect().right+1);})()",
    ),
    true,
    "Model selection and help text fit the narrow composer",
  );
  await screenshot("recurring-result-split-chat.png");
  await evaluate(
    "document.querySelector('[aria-label=\"Expand work\"]').click()",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-work-window') && !document.querySelector('.fe-composer')",
      ),
    "Expand work returns to focused review",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-artifact-editor textarea').value",
    ),
    correctedOccurrence,
    "Expanding work preserves the unsaved edit",
  );
  await until(
    () =>
      evaluate("document.activeElement.classList.contains('co-work-window')"),
    "Keyboard focus follows the expanded result",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        `document.querySelector('.co-artifact-editor textarea')?.value===${JSON.stringify(correctedOccurrence)}`,
      ),
    "Unfinished recurring-result edit survives reload",
  );
  assert.ok(
    await evaluate(
      "document.body.innerText.includes('Later scheduled runs keep their approved instructions and references.')",
    ),
  );
  await click("Save for review");
  await until(
    async () =>
      (await snapshot()).data.artifacts.some(
        (a) =>
          a.taskId === first.runs[0].taskId && a.body === correctedOccurrence,
      ),
    "Separate owner-edited recurring result saved",
  );
  const afterEdit = (await snapshot()).data;
  const editedOccurrence = afterEdit.artifacts.find(
    (a) => a.taskId === first.runs[0].taskId && a.body === correctedOccurrence,
  );
  assert.equal(editedOccurrence.provenance, "owner-authored");
  assert.equal(editedOccurrence.state, "needs_review");
  assert.deepEqual(
    afterEdit.artifacts.find((a) => a.id === originalOccurrenceArtifact.id),
    originalOccurrenceArtifact,
  );
  assert.deepEqual(
    afterEdit.measured,
    beforeEdit.measured,
    "An owner edit creates no model request or receipt",
  );
  assert.deepEqual(
    afterEdit.budgets,
    beforeEdit.budgets,
    "An owner edit spends no additional allowance",
  );
  assert.deepEqual(
    await detail(id),
    scheduleBeforeEdit,
    "Editing a result leaves the remaining schedule unchanged",
  );
  await until(
    () =>
      evaluate(
        `document.querySelector('.co-artifact')?.dataset.artifactId===${JSON.stringify(editedOccurrence.id)}`,
      ),
    "Edited recurring draft is shown first",
  );
  await evaluate(
    `document.querySelector('[data-artifact-id="${editedOccurrence.id}"]').scrollIntoView({behavior:'instant',block:'start'})`,
  );
  await screenshot("recurring-deliverable-owner-edit.png");
  await evaluate(
    `(()=>{const a=document.querySelector('[data-artifact-id="${editedOccurrence.id}"]');[...a.querySelectorAll('button')].find(b=>b.textContent.trim()==='Approve result').click();})()`,
  );
  await until(
    async () =>
      (await snapshot()).data.artifacts.find(
        (a) => a.id === editedOccurrence.id,
      )?.state === "approved",
    "Edited recurring draft approved separately",
  );
  await click("Complete after review");
  await until(
    async () =>
      (await snapshot()).data.tasks.find((t) => t.id === first.runs[0].taskId)
        ?.state === "completed",
    "recurring deliverable accepted separately",
  );
  const beforeFeedback = await detail(id);
  await click("Remember feedback for future work");
  await click("Capture feedback from this result");
  const learnedFeedback =
    "The fictional owner used the paper-folding content plan. Include numbered steps in the next weekly outline.";
  await field("Title", "Weekly content plan feedback", ".co-memory-editor");
  await field("Statement", learnedFeedback, ".co-memory-editor");
  await click("Save memory draft");
  const memories = () =>
    fetch(url + "/api/company/memory").then((r) => r.json());
  await until(
    async () =>
      (await memories()).some(
        (row) => row.memory.definition.statement === learnedFeedback,
      ),
    "Recurring-result feedback draft saved",
  );
  let feedbackRow = (await memories()).find(
    (row) => row.memory.definition.statement === learnedFeedback,
  );
  assert.equal(
    feedbackRow.usable,
    false,
    "Unapproved feedback cannot enter future model context",
  );
  assert.equal(feedbackRow.memory.origin.artifactId, editedOccurrence.id);
  assert.equal(
    feedbackRow.memory.origin.quote,
    correctedOccurrence.slice(0, 600).trim(),
  );
  assert.equal(
    feedbackRow.memory.definition.employeeId,
    editedOccurrence.employeeId,
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-memory-check input') && window.__smokeIdle()",
      ),
    "Feedback ready for separate approval",
  );
  await evaluate("document.querySelector('.co-memory-check input').click()");
  await click("Approve memory");
  await until(
    async () =>
      (await memories()).find((row) => row.memory.id === feedbackRow.memory.id)
        ?.usable,
    "Approved feedback available to its employee",
  );
  feedbackRow = (await memories()).find(
    (row) => row.memory.id === feedbackRow.memory.id,
  );
  assert.deepEqual(
    await detail(id),
    beforeFeedback,
    "Feedback approval preserves the exact remaining schedule",
  );
  await until(
    () =>
      evaluate(
        "window.__smokeIdle() && !document.querySelector('.co-memory-review button')?.disabled && !document.body.innerText.includes('Review memory: Weekly content plan feedback')",
      ),
    "Approved feedback clears its attention item after refresh",
  );
  await screenshot("recurring-result-feedback.png");
  await send("Page.navigate", {
    url: url + "/?view=departments&section=schedules",
  });
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-list button')?.textContent.includes('Fictional weekly outline')",
      ),
    "Saved schedule list",
  );
  await evaluate("document.querySelector('.co-schedule-list button').click()");
  await waitText("1 of 2 occurrences consumed.");
  await click("Pause schedule");
  await waitText("Schedule paused.");
  await advance(id);
  await delay(1500);
  assert.equal((await detail(id)).runs.length, 1);
  await consent();
  await click("Resume remaining occurrences");
  await until(
    async () => (await detail(id)).schedule.state === "completed",
    "Final authorized occurrence",
  );
  await click("Refresh schedule status");
  await waitText("No further slots remain.");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedules').innerText.includes('Schedule active.')",
    ),
    false,
  );
  assert.equal((await detail(id)).runs.length, 2);
  const secondRun = (await detail(id)).runs.find(
    (run) => run.taskId !== first.runs[0].taskId,
  );
  const secondAttempt = (await snapshot()).data.measured.find(
    (attempt) => attempt.taskId === secondRun.taskId,
  );
  const secondPacket = await fetch(
    url + "/api/company/measured/" + secondAttempt.id,
  ).then((r) => r.json());
  assert.equal(secondPacket.call.context.scheduledOccurrence.runNumber, 2);
  assert.equal(
    Date.parse(secondPacket.call.context.scheduledOccurrence.scheduledFor),
    Date.parse(secondRun.dueAt),
  );
  assert.notEqual(
    secondPacket.call.context.scheduledOccurrence.scheduledFor,
    firstPacket.call.context.scheduledOccurrence.scheduledFor,
  );
  const rememberedFeedback = secondPacket.call.context.memory.items.find(
    (item) => item.memoryId === feedbackRow.memory.id,
  );
  assert.equal(
    rememberedFeedback.statement,
    learnedFeedback,
    "Later scheduled work receives the owner's actual guidance",
  );
  assert.equal(rememberedFeedback.origin.artifactId, editedOccurrence.id);
  assert.equal(
    rememberedFeedback.origin.quote,
    correctedOccurrence.slice(0, 600).trim(),
  );
  assert.equal(
    (await snapshot()).data.budgets.find((b) => b.id === "schedule:" + id).used,
    60,
  );
  await click("New schedule");
  await field("Schedule name", "Fictional source watch");
  await field("Work to repeat", "source-refresh");
  await field("Number of occurrences", "1");
  await field("Time zone", "UTC");
  const after = new Date(first.schedule.nextRunAt);
  after.setUTCDate(after.getUTCDate() + 1);
  await field("First date", after.toISOString().slice(0, 10));
  await click("Save schedule draft");
  await waitText("Schedule draft saved.");
  records = await schedules();
  const sourceSchedule = records.find(
    (s) => s.definition.kind === "source-refresh",
  );
  assert.ok(sourceSchedule);
  await consent();
  await click("Authorize schedule");
  await waitText("Schedule active.");
  await advance(sourceSchedule.id);
  await until(
    async () =>
      (await detail(sourceSchedule.id)).schedule.state === "completed",
    "Scheduled source refresh through guarded reader",
  );
  await click("Refresh schedule status");
  await waitText("No further slots remain.");
  const freshness = await fetch(
    url + "/api/company/business/freshness/" + profile.id,
  ).then((r) => r.json());
  assert.equal(
    freshness.fields.find((f) => f.field === "services").status,
    "possible",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    "document.querySelector('.co-schedule-review').scrollIntoView()",
  );
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/schedules-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await evaluate("document.querySelector('.co-inbox-toggle').click()");
  await until(
    () => evaluate("!!document.querySelector('.co-inbox-items button')"),
    "Recurring result in inbox",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-inbox-items li')].find(li=>li.textContent.includes('Fictional source watch')).querySelector('button').click()",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review')?.innerText.includes('Fictional source watch')",
      ),
    "Inbox opens the exact recurring source schedule",
  );
  await click("Review checked sources");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-business-freshness')?.innerText.includes('Source text changed')",
      ),
    "Scheduled source result opens evidence review",
  );
  await send("Page.navigate", {
    url: `${url}/?view=departments&section=schedules&schedule=${id}`,
  });
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review h3')?.textContent==='Fictional weekly outline'",
      ),
    "Original completed schedule opens",
  );
  const beforeNavigation = (await snapshot()).data.measured;
  await click("Copy into a new draft");
  await field("Schedule name", "Fictional revised rhythm");
  const copyStart = new Date(after);
  copyStart.setUTCDate(copyStart.getUTCDate() + 1);
  await field("First date", copyStart.toISOString().slice(0, 10));
  await click("Save schedule draft");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review h3')?.textContent==='Fictional revised rhythm'",
      ),
    "Copied schedule saved for review",
  );
  const copied = (await schedules()).find(
    (s) => s.definition.name === "Fictional revised rhythm",
  );
  assert.equal(copied.state, "draft");
  assert.equal(
    await evaluate("new URLSearchParams(location.search).get('schedule')"),
    copied.id,
    "The copied draft owns the address, not the earlier approval",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review h3')?.textContent==='Fictional revised rhythm'",
      ),
    "Reload keeps the copied review",
  );
  await consent();
  await evaluate(
    "[...document.querySelectorAll('.co-schedule-list button')].find(b=>b.textContent.includes('Fictional weekly outline')).click()",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review h3')?.textContent==='Fictional weekly outline'",
      ),
    "Selection opens the original schedule",
  );
  assert.equal(
    await evaluate("new URLSearchParams(location.search).get('schedule')"),
    id,
  );
  await evaluate("history.back()");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review h3')?.textContent==='Fictional revised rhythm'",
      ),
    "Back restores the copied review",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-schedule-review input[type=checkbox]').checked",
    ),
    false,
    "Returning to a review never carries consent from a different selection",
  );
  await evaluate("history.forward()");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review h3')?.textContent==='Fictional weekly outline'",
      ),
    "Forward restores the original review",
  );
  await evaluate("history.back()");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-schedule-review h3')?.textContent==='Fictional revised rhythm'",
      ),
    "Copied review remains reachable on mobile",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-schedule-review').scrollIntoView({block:'start'})",
  );
  await screenshot("recurring-review-navigation-mobile.png");
  assert.deepEqual(
    (await snapshot()).data.measured,
    beforeNavigation,
    "Review navigation never dispatches a model",
  );
  assert.equal((await detail(copied.id)).schedule.state, "draft");
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/schedules-browser.json"),
    JSON.stringify(
      {
        firstDeliverableAccepted: true,
        completedWorkStartsSeparateDraft: true,
        unrelatedDraftPreserved: true,
        selectedSkillRetained: true,
        approvedEditorialCopyRetained: true,
        orderedRevisionFeedbackRetained: true,
        oversizedFeedbackRequiresExplicitConsolidation: true,
        approvedFeedbackReachesScheduledModelContext: true,
        referencePreviewMatchesExactBody: true,
        unapprovedOriginalExcluded: true,
        recurringResultApproved: true,
        draftSurvivesReload: true,
        lostSaveReconciled: true,
        draftEditedInPlace: true,
        exactRecurringConsent: true,
        reviewedOccurrenceDatesReachSeparateRequests: true,
        savedOccurrenceDateReadable: true,
        backgroundEmployeeRuns: 2,
        savedResultOpened: true,
        fullWorkReviewByDefault: true,
        splitComposerFits: true,
        expandPreservesOwnerEdit: true,
        recurringFeedbackSeparatelyApproved: true,
        recurringFeedbackPreservesSchedule: true,
        recurringFeedbackReachesNextOccurrence: true,
        pauseRespected: true,
        finiteAllowanceSettled: true,
        backgroundSourceRefresh: true,
        inboxScheduleLink: true,
        copiedReviewAddress: true,
        reviewSelectionBackForward: true,
        consentClearedOnReviewReturn: true,
        sourceFactsUnchanged: true,
        mobileOverflow: false,
        liveModelCalls: 0,
        externalRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS schedules: approved, repeated, paused and finished; the butler kept time and every receipt.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/schedules-browser-host.log"),
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
        "Owned fixture process is still running; its temporary files were preserved.",
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
