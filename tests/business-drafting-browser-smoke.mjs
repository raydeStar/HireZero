// Fictional pages pass through the real reader, HTTP routes, store and UI. Only a fictional model; no external requests or live model calls.
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
      HIREZERO_BROWSER_SYNTHETIC_BUSINESS_DRAFT: "success-then-unknown",
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
  await send("Page.navigate", { url });
  await until(
    () => evaluate("!!document.querySelector('.co-home')"),
    "home loaded",
  );
  const click = clicker(evaluate, until);
  const field = async (label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll('.co-business label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing field '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(60);
  };
  const snapshot = () => fetch(url + "/api/company").then((r) => r.json());
  const ownerPost = async (action, fields) => {
    const response = await fetch(url + "/api/company/commands", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Origin: url,
        "X-Dev-Request": "1",
      },
      body: JSON.stringify({
        requestId: crypto.randomUUID(),
        action,
        ...fields,
      }),
    });
    assert.equal(response.status, 200, await response.clone().text());
    return response.json();
  };
  const home = async () => {
    await evaluate(
      "document.querySelector('.co-navigation button[aria-label=\"Home\"]').click()",
    );
    await until(
      () => evaluate("!!document.querySelector('.co-home')"),
      "Home loaded",
    );
  };
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-business')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  await click("Set up my business");
  await field("Business name", "Fictional Paper Studio");
  await field(
    "What would you like help achieving?",
    "Prepare a small craft launch",
  );
  await click("Review business identity");
  await waitText("Check the name, website and location before confirming.");
  await evaluate(
    "[...document.querySelectorAll('.co-business-lookup label')].find(l=>l.textContent.includes('I authorize one request')).querySelector('input').click()",
  );
  await evaluate(
    "window.searchFetch=window.fetch;window.loseSearchReply=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseSearchReply&&body.action==='business.lookup.start'&&body.body==='search'){window.loseSearchReply=false;await window.searchFetch(input,init);throw new TypeError('Synthetic lost search reply');}return window.searchFetch(input,init);};",
  );
  await click("Find possible websites");
  await waitText("Search suggestions are not company facts.");
  let discoveryData = (await snapshot()).data;
  assert.equal(discoveryData.businessLookups.length, 1);
  assert.equal(discoveryData.businessLookups[0].candidates.length, 2);
  assert.equal(discoveryData.businessSources.length, 0);
  assert.equal(discoveryData.business.state, "unconfirmed");
  await send("Page.reload");
  await waitText("Search suggestions are not company facts.");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    "document.querySelector('.co-business-lookup-result').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/business-search-mobile.png"),
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
    "[...document.querySelectorAll('.co-business-candidates li')].find(l=>l.querySelector('a').href==='https://paper.example.invalid/').querySelector('button').click()",
  );
  await waitText("Possible website selected.");
  await click("Review business identity");
  await waitText("Check the name, website and location before confirming.");
  await click("Confirm this is my business");
  await waitText("Business identity confirmed");
  await click("Discover website pages");
  await waitText("3 saved sources available below.");
  discoveryData = (await snapshot()).data;
  assert.equal(discoveryData.businessLookups.length, 2);
  assert.deepEqual(
    discoveryData.businessSources.map((s) => new URL(s.url).pathname).sort(),
    ["/", "/about", "/services"],
  );
  assert.equal(discoveryData.knowledge.length, 0);
  await field("Page address", "https://paper.example.invalid/about");
  await click("Read public page");
  await waitText("Source saved.");
  await field("Brand voice", "Warm and patient, from my unsaved edit");
  await click("Prepare brief draft");
  await waitText("Prepared for authorization");
  let data = (await snapshot()).data;
  assert.equal(data.businessDrafts.length, 1);
  assert.equal(data.businessProfiles.length, 0);
  assert.equal(data.knowledge.length, 0);
  assert.equal(data.employees.length, 0);
  assert.equal(data.budgets.length, 0);
  await send("Page.reload");
  await waitText("Prepared for authorization");
  await click("Review draft request");
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-business-drafting label')].some(l=>l.textContent.includes('I authorize one draft'))",
      ),
    "exact request review",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting label')].find(l=>l.textContent.includes('I authorize one draft')).querySelector('input').click()",
  );
  await evaluate(
    `window.businessRunFetch=window.fetch;window.loseBusinessReply=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseBusinessReply&&body.action==='business.draft.run'){window.loseBusinessReply=false;await window.businessRunFetch(input,init);throw new TypeError('Synthetic lost run reply');}return window.businessRunFetch(input,init);};`,
  );
  await click("Authorize one brief draft");
  await waitText("Your generated brief is ready");
  data = (await snapshot()).data;
  assert.equal(data.businessDrafts.length, 1);
  assert.equal(data.businessDrafts[0].state, "completed");
  assert.equal(data.businessDrafts[0].reportedUsage, 160);
  assert.equal(data.businessProfiles.length, 1);
  assert.equal(data.knowledge.length, 0);
  assert.equal(data.employees.length, 0);
  await click("Review generated brief");
  await waitText("Generated brief loaded.");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-business label')].find(l=>l.childNodes[0]?.textContent.trim()==='Products and services').querySelector('textarea').value",
    ),
    "Paper craft kits for adult hobbyists",
    "Evidence became structured fields",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-business label')].find(l=>l.childNodes[0]?.textContent.trim()==='Brand voice').querySelector('textarea').value",
    ),
    "Warm and patient, from my unsaved edit",
    "Loading generated facts preserves unfinished owner edits",
  );
  await click("Save brief draft");
  await waitText("Brief draft saved.");
  await click("Approve brief for my team");
  await waitText("Business brief approved.");
  data = (await snapshot()).data;
  assert.match(
    data.knowledge[0].body,
    /Warm and patient, from my unsaved edit \[owner\]/,
  );
  assert.equal(
    data.businessProfiles[0].fields.find((f) => f.key === "services").citations
      .length,
    1,
  );
  assert.equal(data.budgets.find((b) => b.id === "company-total").used, 160);
  assert.equal(data.budgets.find((b) => b.id === "company-total").reserved, 0);
  await evaluate("document.querySelector('.co-business-history').open=true");
  await until(
    () =>
      evaluate(
        "document.querySelectorAll('.co-business-history ol > li').length===3",
      ),
    "Generated draft and owner review history",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-business-history').innerText.includes('Generated draft')",
    ),
    true,
  );
  assert.equal(
    await evaluate(
      "!!document.querySelector('.co-business-approved .co-business-field-evidence a')",
    ),
    true,
    "Reviewed facts expose their source address and retrieval date",
  );
  await evaluate("document.querySelector('.co-business-history').open=false");
  await evaluate(
    "document.querySelector('.co-business-drafting > details').open=true",
  );
  await click("Prepare brief draft");
  await waitText("Prepared for authorization");
  await click("Review draft request");
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-business-drafting label')].some(l=>l.textContent.includes('I authorize one draft'))",
      ),
    "second request review",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting label')].find(l=>l.textContent.includes('I authorize one draft')).querySelector('input').click()",
  );
  await click("Authorize one brief draft");
  await waitText("Provider receipt needs reconciliation");
  data = (await snapshot()).data;
  assert.equal(data.businessDrafts.length, 2);
  assert.equal(
    data.budgets.find((b) => b.id === "company-total").reserved,
    26000,
  );
  await send("Page.reload");
  await waitText("Provider receipt needs reconciliation");
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting details')].find(d=>d.querySelector('summary').textContent==='Reconcile a provider receipt').open=true",
  );
  await field("Provider receipt", "fictional-reconciled-receipt");
  await field("Confirmed input tokens", "150");
  await field("Confirmed output tokens", "50");
  await field(
    "How was this verified?",
    "Checked the fictional provider receipt and confirmed completion",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting label')].find(l=>l.textContent.includes('I verified that the provider request has stopped')).querySelector('input').click()",
  );
  await click("Record reconciled usage");
  await waitText("Provider receipt reconciled");
  data = (await snapshot()).data;
  assert.equal(data.budgets.find((b) => b.id === "company-total").reserved, 0);
  assert.equal(data.budgets.find((b) => b.id === "company-total").used, 360);
  assert.equal(
    data.businessProfiles.length,
    1,
    "Unknown output was never imported",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await delay(120);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-business-drafting').scrollIntoView({block:'start'})",
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/business-drafting-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  assert.deepEqual(errors, []);
  await click("Set up a Marketing employee");
  const bodyText = (text) =>
    until(
      () =>
        evaluate(`document.body.innerText.includes(${JSON.stringify(text)})`),
      text,
    );
  await bodyText("Suggested from your approved business brief");
  assert.match(
    await evaluate(
      "[...document.querySelectorAll('.co-onboarding label')].find(l=>l.childNodes[0]?.textContent.trim()==='Responsibility').querySelector('textarea').value",
    ),
    /Prepare a small craft launch/,
  );
  await click("Create employee & continue");
  await bodyText("Shared department context");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-onboarding form textarea')].every(t=>t.value.trim().length>0)",
    ),
    true,
    "Approved brief supplies role setup without another blank questionnaire",
  );
  await click("Save context for review");
  await bodyText("Review the saved text below");
  await evaluate(
    "[...document.querySelectorAll('.co-onboarding label')].find(l=>l.textContent.includes('I reviewed these facts')).querySelector('input').click()",
  );
  await click("Approve shared context");
  await bodyText("Your first marketing plan");
  assert.match(
    await evaluate(
      "document.querySelector('.co-first-assignment textarea').value",
    ),
    /one-week marketing starter plan/,
  );
  await evaluate(
    "document.querySelector('.co-first-assignment').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/first-assignment-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  // A prepared chat is real saved work of kind conversation, but it is not a first deliverable.
  data = (await snapshot()).data;
  const person = data.employees[0],
    privateThread = data.threads.find(
      (t) =>
        t.employeeId === person.id && (t.scope ?? "employee") === "employee",
    );
  const conversation = await ownerPost("conversation.prepare", {
    employeeId: person.id,
    threadId: privateThread.id,
    scope: "employee",
    body: "Fictional introductory question. Do not run a model.",
    creditAllowance: 1,
  });
  await ownerPost("task.cancel", {
    id: conversation.task.id,
    expectedVersion: conversation.task.version,
  });
  await ownerPost("context.draft", {
    employeeId: person.id,
    body: "Fictional source: the studio sells paper craft kits.",
    creditAllowance: 1,
  });
  const contextDraft = (await snapshot()).data.tasks.find(
    (t) => t.kind === "context-draft",
  );
  assert.ok(contextDraft);
  await ownerPost("task.cancel", {
    id: contextDraft.id,
    expectedVersion: contextDraft.version,
  });
  await home();
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-home .co-first-assignment textarea')",
      ),
    "Home resumes the suggested assignment after leaving onboarding and reloading",
  );
  assert.match(
    await evaluate(
      "document.querySelector('.co-checklist li.next').textContent",
    ),
    /Prepare a first assignment/,
  );
  assert.equal(
    (await snapshot()).data.tasks.filter((t) => t.kind === "work").length,
    0,
  );
  await evaluate(
    "(()=>{const el=document.querySelector('.co-home .co-first-assignment textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(el,el.value+' Keep the first experiment modest.');el.dispatchEvent(new Event('input',{bubbles:true}));})()",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-home .co-first-assignment textarea')?.value.includes('Keep the first experiment modest.')",
      ),
    "First-assignment edits survive a Home reload",
  );
  await evaluate(
    "document.querySelector('.co-home .co-first-assignment').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/first-result-home-mobile.png"),
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
    "document.querySelector('.co-home .co-first-assignment').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/first-result-home-desktop.png"),
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
  // A lost save response is reconciled by receipt, never by preparing a duplicate assignment.
  await evaluate(
    "window.firstPlanFetch=window.fetch;window.loseFirstPlan=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseFirstPlan&&body.action==='assignment.prepare'){window.loseFirstPlan=false;await window.firstPlanFetch(input,init);throw new TypeError('Synthetic lost first-plan reply');}return window.firstPlanFetch(input,init);};",
  );
  await click("Prepare my first plan");
  await bodyText("Review task and context");
  data = (await snapshot()).data;
  const firstTask = () =>
    data.tasks.find((t) => t.skill === "marketing-starter-plan");
  assert.equal(data.tasks.filter((t) => t.kind === "work").length, 1);
  assert.equal(firstTask().kind, "work");
  const firstMeasured = data.measured.find(
    (m) => m.job.policy.version === "measured-deliverable-v1",
  );
  assert.ok(firstMeasured);
  assert.equal(firstMeasured.job.state, "proposed");
  assert.equal(
    data.budgets.find((b) => b.id === "company-total").used,
    360,
    "Preparing first plan spends nothing",
  );
  await home();
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-checklist li.next')?.textContent.includes('Open first assignment')",
      ),
    "Home offers the saved assignment before it runs",
  );
  assert.equal(
    await evaluate(
      "!!document.querySelector('.co-home .co-first-assignment form')",
    ),
    false,
    "No duplicate preparation form after a saved assignment",
  );
  await click("Open first assignment");
  await click("Review task and context");
  await until(
    () => evaluate("!!document.querySelector('.co-job-card .co-packet')"),
    "First plan packet",
  );
  const firstPacket = await evaluate(
    "document.querySelector('.co-job-card .co-packet').textContent",
  );
  assert.match(firstPacket, /Paper craft kits for adult hobbyists/);
  assert.match(firstPacket, /Warm and patient, from my unsaved edit/);
  assert.match(firstPacket, /businessBrief/);
  assert.match(firstPacket, /https:\/\/paper.example.invalid\//);
  assert.match(firstPacket, /retrievedAt/);
  assert.match(firstPacket, /at most 500 words/);
  assert.doesNotMatch(firstPacket, /at most 80 words/);
  await evaluate(
    "document.querySelector('.co-job-card .co-check input').click()",
  );
  await click("Authorize and run one job");
  await bodyText("DRAFT FOR INTERNAL REVIEW");
  await bodyText("Hypothesis: a short folding demonstration may help.");
  // Home already opened focused work mode; the completed draft is readable without another navigation step.
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-work-window') && !document.querySelector('.fe-composer')",
      ),
    "Focused mobile result view without the chat composer",
  );
  data = (await snapshot()).data;
  assert.equal(firstTask().state, "awaiting_review");
  assert.equal(data.artifacts.length, 1);
  assert.equal(data.artifacts[0].state, "needs_review");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Complete after review').disabled",
    ),
    true,
  );
  await click("Approve result");
  await home();
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-checklist li.next')?.textContent.includes('Review first result')",
      ),
    "Approving a result alone does not finish the first assignment",
  );
  await click("Review first result");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-work-window') && !document.querySelector('.fe-composer')",
      ),
    "Home opens the exact result in focused work mode",
  );
  await click("Complete after review");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-job-card')?.dataset.taskState==='completed'",
      ),
    "Completed plan is confirmed in the workspace",
  );
  data = (await snapshot()).data;
  assert.equal(firstTask().state, "completed");
  assert.equal(data.artifacts[0].state, "approved");
  assert.equal(data.budgets.find((b) => b.id === "company-total").used, 600);
  await send("Page.reload");
  await bodyText("DRAFT FOR INTERNAL REVIEW");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-job-card')?.dataset.taskState==='completed'",
      ),
    "Reviewed first assignment survives reload",
  );
  assert.equal(
    await evaluate("document.querySelector('.co-job-request').open"),
    false,
  );
  assert.equal(
    await evaluate("document.querySelector('.co-job-receipt').open"),
    false,
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-job-card').innerText.includes('Run one internal draft')",
    ),
    false,
  );
  await evaluate("document.querySelector('.co-job-receipt summary').click()");
  await bodyText("Result approved and task complete");
  await evaluate("document.querySelector('.co-job-receipt summary').click()");
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-job-card').scrollIntoView({block:'start'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/first-deliverable-mobile.png"),
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
  await delay(100);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/first-deliverable-desktop.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  // Progress comes from the same persisted journey; a refresh cannot invent another completion or a lower cost.
  await click("Company settings");
  await click("Progress");
  await bodyText("Average recorded model cost per completed task");
  const progress = await fetch(url + "/api/company/progress").then((r) =>
    r.json(),
  );
  assert.equal(progress.schema, "company-progress-v1");
  assert.equal(progress.work.tasks, 1);
  assert.equal(progress.work.completed, 1);
  assert.equal(progress.work.firstAssignment.taskId, firstTask().id);
  assert.equal(progress.work.firstAssignment.state, "completed");
  assert.equal(progress.work.memberRequestDays, 1);
  assert.equal(progress.onboarding.generatedBriefs, 1);
  assert.equal(progress.onboarding.editedGeneratedBriefs, 1);
  assert.ok(progress.onboarding.ownerFieldEdits > 0);
  assert.equal(progress.onboarding.approvedBriefs, 1);
  assert.equal(progress.averageRecordedModelUsdPerCompletion, 0.002);
  assert.equal(progress.modelCosts.jobs, 3);
  assert.ok(
    progress.modelCosts.unpricedJobs > 0,
    "Unpriced earlier draft remains visible instead of becoming free",
  );
  await send("Browser.setDownloadBehavior", {
    behavior: "allow",
    downloadPath: path.join(temporary, "downloads"),
  });
  await click("Download metrics");
  const exported = await until(
    async () =>
      JSON.parse(
        await fs.readFile(
          path.join(temporary, "downloads/hirezero-progress.json"),
          "utf8",
        ),
      ),
    "Progress metrics downloaded",
  );
  assert.deepEqual(exported.work, progress.work);
  assert.deepEqual(exported.onboarding, progress.onboarding);
  assert.deepEqual(exported.modelCosts, progress.modelCosts);
  assert.doesNotMatch(
    JSON.stringify(exported),
    /owner@example|Paper Studio|Warm and patient|DRAFT FOR INTERNAL REVIEW/,
  );
  await evaluate(
    "window.progressFetch=window.fetch;window.failProgress=true;window.fetch=(input,init)=>window.failProgress&&String(input).includes('/company/progress')?Promise.reject(new TypeError('Synthetic progress read failed')):window.progressFetch(input,init)",
  );
  await click("Refresh progress");
  await bodyText("Showing the last checked snapshot.");
  await evaluate("window.failProgress=false");
  await click("Refresh progress");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-progress')?.getAttribute('aria-busy')==='false' && !document.querySelector('.co-progress [role=alert]')",
      ),
    "Progress refresh recovered",
  );
  await evaluate(
    "document.querySelector('.co-progress').scrollIntoView({block:'start'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/company-progress-desktop.png"),
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
  await fs.writeFile(
    path.join(repo, "test-results/company-progress-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await until(
    () =>
      evaluate(
        "(()=>{const n=document.querySelector('.co-hub-tabs'),a=n?.querySelector('[aria-current=page]');if(!a)return false;const nr=n.getBoundingClientRect(),ar=a.getBoundingClientRect();return nr.height<=60&&ar.left>=nr.left&&ar.right<=nr.right;})()",
      ),
    "Selected company section visible in a single mobile navigation row",
  );
  await send("Page.reload");
  await bodyText("Average recorded model cost per completed task");
  await until(
    () =>
      evaluate(
        "(()=>{const n=document.querySelector('.co-hub-tabs'),a=n?.querySelector('[aria-current=page]');if(!a)return false;const nr=n.getBoundingClientRect(),ar=a.getBoundingClientRect();return nr.height<=60&&ar.left>=nr.left&&ar.right<=nr.right;})()",
      ),
    "Mobile section restored after reload",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-progress-section')].find(e=>e.querySelector('h3')?.textContent==='Recorded model costs').scrollIntoView({block:'start'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/company-progress-costs-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await click("Open first assignment");
  await bodyText("DRAFT FOR INTERNAL REVIEW");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-work-window .co-job-card')?.dataset.taskState",
    ),
    "completed",
  );
  assert.deepEqual(errors, []);
  await home();
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-home') && !document.querySelector('[aria-label=\"Getting started\"]')",
      ),
    "Getting started finishes only after approved result and task completion",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-home') && !document.querySelector('[aria-label=\"Getting started\"]')",
      ),
    "Completed Home progress survives reload",
  );
  await fs.writeFile(
    path.join(repo, "test-results/business-drafting-browser.json"),
    JSON.stringify(
      {
        preHireDraft: true,
        exactAuthorization: true,
        correctionHistory: true,
        employeeSourceProvenance: true,
        sourceCitations: true,
        unsavedOwnerEditsPreserved: true,
        lostReplyReconciledWithoutReplay: true,
        unknownUsageHeld: true,
        explicitReceiptReconciliation: true,
        firstMarketingPlan: true,
        homeResumesFirstAssignment: true,
        chatDoesNotCompleteAssignmentMilestone: true,
        contextDraftDoesNotCompleteAssignmentMilestone: true,
        homeDraftSurvivesReload: true,
        homeOpensPendingAssignment: true,
        homeRequiresReviewedCompletedWork: true,
        homeResultUsesFocusedWorkView: true,
        firstPlanLostSaveReconciled: true,
        approvedBriefInFirstPlan: true,
        firstPlanSurvivesReload: true,
        firstPlanIsTrackedWork: true,
        firstPlanNeedsResultApproval: true,
        firstPlanCompletedAfterReview: true,
        boundedDeliverablePolicy: true,
        resultBeforeCollapsedRequestAndReceipt: true,
        mobileResultUsesWorkView: true,
        progressUsesSavedMilestones: true,
        progressExportContainsNoContent: true,
        progressRefreshRecovers: true,
        progressOpensCompletedWork: true,
        mobileCompanyNavigationIsCompact: true,
        candidateSelectionRequiresConfirmation: true,
        searchLostReplyNeverRepeated: true,
        boundedAutomaticDiscovery: true,
        modelCalls: 3,
        liveModelCalls: 0,
        externalRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS business drafting: pre-hire generation, exact consent, owner edits and uncertain receipts; no second bill hidden beneath the silverware.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/business-drafting-browser-host.log"),
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
