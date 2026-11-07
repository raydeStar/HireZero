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
  // The classic start, chosen as an owner can (docs/adr/0186).
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: "localStorage.setItem('dev-ui:onboarding','\"form\"')",
  });
  // Home opens on the lead employee's desk once work exists; these checks read the overview's cards.
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: "localStorage.setItem('dev-ui:home-start','\"overview\"')",
  });
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
    if (action === "artifact.create") {
      // The fixture worker may park an unqualified task between creation and seeding.
      fields = {
        ...fields,
        expectedVersion: (await snapshot()).data.tasks.find(
          (t) => t.id === fields.id,
        ).version,
      };
    }
    const post = () =>
      fetch(url + "/api/company/commands", {
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
    let response = await post();
    if (action === "artifact.create" && response.status === 409) {
      // A confirmed version refusal wrote nothing. Refresh this fictional setup once; never replay an uncertain write.
      fields = {
        ...fields,
        expectedVersion: (await snapshot()).data.tasks.find(
          (t) => t.id === fields.id,
        ).version,
      };
      response = await post();
    }
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
  await send("Page.navigate", {
    url: url + "/?view=departments&section=business",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-business')"),
    "Business brief editor",
  );
  await field("Business name", "Fictional Paper Studio");
  await field(
    "What would you like help achieving?",
    "Prepare a small craft launch with a weekly content plan.",
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
  await evaluate(
    "window.discoveryFetch=window.fetch;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(body.action==='business.lookup.start'&&body.body==='discovery'){await new Promise(resolve=>{window.releaseDiscovery=resolve;});}return window.discoveryFetch(input,init);};",
  );
  await click("Discover website pages");
  await waitText("Looking up website evidence…");
  assert.equal(
    await evaluate(
      "document.body.innerText.includes('This request needs confirmation.')",
    ),
    false,
  );
  await evaluate(
    "window.releaseDiscovery();window.fetch=window.discoveryFetch",
  );
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
  const reviewTerms = await evaluate(
    "document.querySelector('[aria-label=\"Brief request model and limits\"]').innerText",
  );
  assert.ok(reviewTerms.includes(data.businessDrafts[0].job.route));
  assert.ok(reviewTerms.includes("Reasoning effort: low"));
  assert.ok(reviewTerms.includes("Output target: 3,000 tokens"));
  assert.ok(reviewTerms.includes("One model request. No automatic retry."));
  assert.ok(reviewTerms.includes("soft limit"));
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
  await evaluate("document.querySelector('.co-business-research').open=true");
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
  assert.equal(
    await evaluate(
      "document.querySelector('.co-business-profile fieldset').hidden",
    ),
    true,
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-business-readonly').innerText.includes('Warm and patient, from my unsaved edit')",
    ),
    true,
  );
  await evaluate("document.querySelector('.co-business-research').open=true");
  await click("Review generated brief");
  await waitText("Generated brief loaded.");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-business-profile fieldset').hidden",
    ),
    true,
    "Loading without unfinished edits opens the readable review",
  );
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
    "document.querySelector('.co-business-research').open=true;document.querySelector('.co-business-drafting > details').open=true",
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
    "[...document.querySelectorAll('.co-business-drafting details')].find(d=>d.querySelector('summary').textContent==='Reconcile request outcome').open=true",
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
  await evaluate(
    "document.querySelector('.co-business-research').open=true;document.querySelector('.co-business-drafting > details').open=true",
  );
  await click("Prepare brief draft");
  await waitText("Prepared for authorization");
  await click("Review draft request");
  await waitText("I authorize one draft");
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting label')].find(l=>l.textContent.includes('I authorize one draft')).querySelector('input').click()",
  );
  await click("Authorize one brief draft");
  await waitText("Provider receipt needs reconciliation");
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting details')].find(d=>d.querySelector('summary').textContent==='Reconcile request outcome').open=true",
  );
  await evaluate(
    "const select=[...document.querySelectorAll('.co-business-drafting label')].find(l=>l.textContent.includes('Evidence type')).querySelector('select');select.value='local-refusal';select.dispatchEvent(new Event('change',{bubbles:true}));",
  );
  await waitText("Local diagnostic receipt");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-business-drafting').innerText.includes('Confirmed output tokens')",
    ),
    false,
  );
  await field("Local diagnostic receipt", "fictional-local-refusal");
  await field(
    "How was this verified?",
    "Fictional dispatch log establishes refusal before any model request.",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting label')].find(l=>l.textContent.includes('I verified from local evidence')).querySelector('input').click()",
  );
  await click("Record local refusal");
  await waitText("Local refusal recorded");
  await send("Page.reload");
  await waitText("Website sources and draft requests");
  await evaluate("document.querySelector('.co-business-research').open=true");
  await waitText("Local refusal recorded");
  data = (await snapshot()).data;
  assert.equal(data.businessDrafts.length, 3);
  const locallyResolved = data.businessDrafts.find(
    (d) => d.localRefusalReceipt,
  );
  assert.equal(locallyResolved.localRefusalReceipt, "fictional-local-refusal");
  assert.equal(locallyResolved.providerReceipt, null);
  assert.equal(locallyResolved.reportedUsage, 0);
  assert.equal(data.budgets.find((b) => b.id === "company-total").reserved, 0);
  assert.equal(data.budgets.find((b) => b.id === "company-total").used, 360);
  assert.equal(data.businessProfiles.length, 1);
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
  await bodyText(
    "Use your approved business brief to prepare the marketing work requested in your goal.",
  );
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
      // Required context comes from the brief; the optional goal-ladder fields stay empty until a ladder is approved (ADR 0170).
      "(()=>{const fields=[...document.querySelectorAll('.co-onboarding form textarea[required]')];return fields.length>0&&fields.every(t=>t.value.trim().length>0);})()",
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
    /one-week marketing plan/,
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
      evaluate("!!document.querySelector('.co-home .co-goal-start textarea')"),
    "Home resumes the suggested assignment after leaving onboarding and reloading",
  );
  assert.match(
    await evaluate("document.querySelector('.co-goal-output').textContent"),
    /Your first marketing plan/,
  );
  assert.equal(
    (await snapshot()).data.tasks.filter((t) => t.kind === "work").length,
    0,
  );
  await evaluate(
    "(()=>{const el=document.querySelector('.co-home .co-goal-start textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(el,el.value+' Keep the first experiment modest.');el.dispatchEvent(new Event('input',{bubbles:true}));})()",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-home .co-goal-start textarea')?.value.includes('Keep the first experiment modest.')",
      ),
    "First-assignment edits survive a Home reload",
  );
  await evaluate(
    "document.querySelector('.co-home .co-goal-start').scrollIntoView({block:'start'})",
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
    "document.querySelector('.co-home .co-goal-start').scrollIntoView({block:'start'})",
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
  // A transient read failure clears after recovery without losing the unfinished request.
  const unsavedFirstRequest = await evaluate(
    "document.querySelector('.co-goal-start textarea').value",
  );
  await evaluate(
    "window.refreshOriginalFetch=window.fetch;window.failWorkspaceRefresh=true;window.fetch=(input,init)=>window.failWorkspaceRefresh&&String(input)==='/api/company'?Promise.reject(new TypeError('Synthetic workspace refresh failure')):window.refreshOriginalFetch(input,init)",
  );
  await bodyText("Workspace updates are unavailable.");
  assert.equal(
    await evaluate("document.querySelector('.co-goal-start textarea').value"),
    unsavedFirstRequest,
  );
  await evaluate("window.failWorkspaceRefresh=false");
  await click("Refresh workspace");
  await until(
    () => evaluate("!document.querySelector('.co-refresh-error')"),
    "Recovered reads clear their own notice",
  );
  assert.equal(
    await evaluate("document.querySelector('.co-goal-start textarea').value"),
    unsavedFirstRequest,
  );
  // While a save is still pending, show progress. A lost reply is reconciled without a duplicate assignment.
  await evaluate(
    "window.firstPlanFetch=window.fetch;window.loseFirstPlan=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseFirstPlan&&body.action==='goal.prepare'){window.loseFirstPlan=false;await new Promise(resolve=>window.releaseFirstPlan=resolve);await window.firstPlanFetch(input,init);throw new TypeError('Synthetic lost first-plan reply');}return window.firstPlanFetch(input,init);};",
  );
  await until(
    () =>
      evaluate("!!document.querySelector('.co-goal-start .co-check input')"),
    "Home run terms",
  );
  await evaluate(
    "document.querySelector('.co-goal-start .co-check input').click()",
  );
  await click("Generate my first draft");
  await bodyText("Preparing your request…");
  assert.equal(
    await evaluate(
      "document.body.innerText.includes('The save needs confirmation')",
    ),
    false,
  );
  assert.equal(
    await evaluate(
      // The form gives way to progress while saving; no enabled button can submit it again.
      "[...document.querySelectorAll('.co-goal-start button')].every((b) => b.disabled)",
    ),
    true,
  );
  await evaluate("window.releaseFirstPlan()");
  await bodyText("DRAFT FOR INTERNAL REVIEW").catch(async (error) => {
    await fs.writeFile(
      path.join(repo, "test-results/business-drafting-browser-failure.json"),
      JSON.stringify(
        {
          text: await evaluate("document.body.innerText"),
          workspace: await snapshot(),
        },
        null,
        2,
      ),
    );
    throw error;
  });
  data = (await snapshot()).data;
  const firstTask = () =>
    data.tasks.find((t) => t.skill === "marketing-starter-plan");
  assert.equal(data.tasks.filter((t) => t.kind === "work").length, 1);
  assert.equal(firstTask().kind, "work");
  assert.equal(
    await evaluate("new URL(location.href).searchParams.get('work')"),
    firstTask().id,
    "Preparation opens the exact saved job review",
  );
  const firstMeasured = data.measured.find(
    (m) => m.job.policy.version === "measured-deliverable-v3",
  );
  assert.ok(firstMeasured);
  assert.equal(firstMeasured.job.state, "completed");
  assert.ok(
    data.budgets.find((b) => b.id === "company-total").used > 360,
    "Single Home consent dispatches the draft",
  );
  await home();
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-goal-start')?.textContent.includes('Continue my work')",
      ),
    "Home offers the saved assignment before it runs",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-home .co-goal-start form')"),
    false,
    "No duplicate preparation form after a saved assignment",
  );
  await click("Continue my work");
  const firstPacket = JSON.stringify(
    await fetch(url + "/api/company/measured/" + firstMeasured.id).then((r) =>
      r.json(),
    ),
  );
  assert.match(firstPacket, /Paper craft kits for adult hobbyists/);
  assert.match(firstPacket, /Warm and patient, from my unsaved edit/);
  assert.match(firstPacket, /businessBrief/);
  assert.match(firstPacket, /https:\/\/paper.example.invalid\//);
  assert.match(firstPacket, /retrievedAt/);
  assert.match(firstPacket, /skill's format guidance/);
  assert.doesNotMatch(firstPacket, /at most 500 words/);
  assert.doesNotMatch(firstPacket, /at most 80 words/);
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
  const savedDraft = data.artifacts[0].body;
  await evaluate(
    "Object.defineProperty(navigator,'clipboard',{configurable:true,value:{writeText:async text=>{window.copiedDraft=text}}});",
  );
  await click("Copy draft");
  await bodyText("Draft copied.");
  assert.equal(
    await evaluate("window.copiedDraft"),
    savedDraft,
    "Copy retains the exact saved draft",
  );
  await evaluate(
    "window.savedCreateObjectURL=URL.createObjectURL;window.savedAnchorClick=HTMLAnchorElement.prototype.click;URL.createObjectURL=blob=>{window.downloadedDraft=blob.text();return window.savedCreateObjectURL(blob)};HTMLAnchorElement.prototype.click=function(){window.downloadName=this.download};",
  );
  await click("Download draft");
  assert.equal(
    await evaluate("window.downloadedDraft"),
    savedDraft,
    "Download retains the exact saved draft",
  );
  assert.equal(await evaluate("window.downloadName"), "hirezero-draft.md");
  await evaluate(
    "URL.createObjectURL=window.savedCreateObjectURL;HTMLAnchorElement.prototype.click=window.savedAnchorClick;navigator.clipboard.writeText=async()=>{throw Error('Fictional blocked clipboard')};",
  );
  await click("Copy draft");
  await bodyText(
    "Clipboard unavailable. Use Download draft or select the text below.",
  );
  assert.equal(
    (await snapshot()).data.artifacts[0].state,
    "needs_review",
    "Using a draft does not approve it",
  );
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
        "!!document.querySelector('[aria-label=\"Recent results\"] .co-home-row')",
      ),
    "Approving a result alone does not finish the first assignment",
  );
  assert.equal(
    (await snapshot()).data.tasks.find((t) => t.id === firstTask().id).state,
    "awaiting_review",
  );
  await evaluate(
    "document.querySelector('[aria-label=\"Recent results\"] .co-home-row').click()",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-work-window') && !document.querySelector('.fe-composer')",
      ),
    "Home opens the exact result in focused work mode",
  );
  await evaluate(
    "window.completionFetch=window.fetch;window.refuseCompletion=true;window.readsAfterRefusal=0;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.refuseCompletion&&body.action==='task.complete'){window.refuseCompletion=false;return new Response(JSON.stringify({error:'Synthetic completion refusal'}),{status:422,headers:{'Content-Type':'application/json'}});}const response=await window.completionFetch(input,init);if(!window.refuseCompletion&&String(input)==='/api/company'&&response.ok)window.readsAfterRefusal++;return response;}",
  );
  await click("Complete after review");
  await bodyText("Synthetic completion refusal");
  await until(
    () => evaluate("window.readsAfterRefusal>0"),
    "Successful poll after a refused command",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('[role=alert]')?.textContent.includes('Synthetic completion refusal')",
    ),
    true,
    "A recovered read must not erase a command failure",
  );
  assert.equal(
    (await snapshot()).data.tasks.find((t) => t.id === firstTask().id).state,
    "awaiting_review",
  );
  await click("Dismiss");
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
  await click("Settings");
  await evaluate(
    "document.querySelector('.co-advanced-settings > summary').click()",
  );
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
  assert.equal(progress.modelCosts.jobs, 4);
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
        "(()=>{const a=document.querySelector('.co-hub-tabs [aria-current=page]'),n=a?.closest('.co-hub-tabs');if(!a)return false;const nr=n.getBoundingClientRect(),ar=a.getBoundingClientRect();return nr.height<=60&&ar.left>=nr.left&&ar.right<=nr.right;})()",
      ),
    "Selected company section visible in a single mobile navigation row",
  );
  await send("Page.reload");
  await bodyText("Average recorded model cost per completed task");
  await until(
    () =>
      evaluate(
        "(()=>{const a=document.querySelector('.co-hub-tabs [aria-current=page]'),n=a?.closest('.co-hub-tabs');if(!a)return false;const nr=n.getBoundingClientRect(),ar=a.getBoundingClientRect();return nr.height<=60&&ar.left>=nr.left&&ar.right<=nr.right;})()",
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
        "!!document.querySelector('.co-home [aria-label=\"Recent results\"] .co-home-row')",
      ),
    "The completed result stays directly available on Home",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-home [aria-label=\"Recent results\"] .co-home-row')",
      ),
    "Completed Home progress survives reload",
  );
  // A later editorial review must lead directly to its revision, including on a phone.
  const editorial = await ownerPost("task.create", {
    employeeId: firstTask().employeeId,
    threadId: firstTask().threadId,
    skill: "marketing-starter-plan",
    body: "Prepare one useful fictional paper-craft week, including the complete example.",
  });
  const editorialBody = [
    "## Monday — Prepare your folding space",
    "",
    "Place your paper and the printed guide on a clear surface.",
    "",
    "## Wednesday — Show one step",
    "",
    "Use the guide to choose a step. Describe that step without promising a finished result.",
    "",
    "## Friday — Review the explanation",
    "",
    "Name the instruction that was unclear and propose one wording change.",
    "",
    "### Complete review checklist",
    "",
    "- Can the reader identify the next step?",
    "- Is the example present?",
    "- Are product claims supported?",
    "",
    "### Owner notes",
    "",
    "Fictional internal draft only. No publication or offer.",
  ].join("\n");
  await ownerPost("artifact.create", {
    id: editorial.id,
    expectedVersion: editorial.version,
    body: editorialBody,
  });
  const editorialUrl = `${url}/?view=employee&employee=${editorial.employeeId}&thread=${editorial.threadId}&work=${editorial.id}`;
  await send("Page.navigate", { url: editorialUrl });
  await bodyText("Complete review checklist");
  assert.match(
    await evaluate(
      'document.querySelector(\'[aria-label="Conversation mode"] [aria-current="page"]\').textContent',
    ),
    /^Work/,
    "A narrow work link without an explicit mode selects the visible Work tab",
  );
  await evaluate(
    "[...document.querySelectorAll('[aria-label=\"Conversation mode\"] button')].find(button=>button.textContent.trim()==='Chat').click()",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.fe-composer') && !document.querySelector('.co-work-window')",
      ),
    "Chat exits the full-width work window instead of leaving the owner stuck",
  );
  await send("Page.navigate", { url: editorialUrl });
  await bodyText("Complete review checklist");
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-work-window .co-report.clamped').length",
    ),
    0,
    "The full multi-section draft opens without hiding the useful asset",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-work-window button')].some(b=>b.textContent.trim()==='Read result')",
    ),
    false,
    "A focused result does not offer to open itself again",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/content-result-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  const beforeCopy = (await snapshot()).data;
  const sourceCopy = beforeCopy.artifacts.find(
    (artifact) => artifact.taskId === editorial.id,
  );
  await click("Edit a copy");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-artifact-editor textarea').value",
    ),
    editorialBody,
  );
  const editedBody = editorialBody.replace(
    "Describe that step",
    "Show the fold with its printed step number, then describe it",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1600,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  const taskEditors = `.co-job-card[data-task-id="${editorial.id}"] .co-artifact-editor textarea`;
  await until(
    () =>
      evaluate(
        `document.querySelectorAll(${JSON.stringify(taskEditors)}).length===1 && !!document.querySelector('.co-work-placeholder')`,
      ),
    "Wide Chat defers to the task's one work editor",
  );
  await evaluate(
    `(()=>{const el=document.querySelector(${JSON.stringify(taskEditors)});Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(el,${JSON.stringify(editedBody)});el.dispatchEvent(new Event('input',{bubbles:true}));})()`,
  );
  await evaluate(
    "document.querySelector('.co-work-window [aria-label=\"Close work\"]').click()",
  );
  await until(
    () =>
      evaluate(
        `!document.querySelector('.co-work-window') && document.querySelector(${JSON.stringify(taskEditors)})?.value===${JSON.stringify(editedBody)}`,
      ),
    "Closing wide work restores the draft in its conversation card",
  );
  await evaluate(
    `[...document.querySelectorAll('.co-job-card[data-task-id="${editorial.id}"] button')].find(button=>button.textContent.trim()==='Read result').click()`,
  );
  await until(
    () =>
      evaluate(
        `!!document.querySelector('.co-work-window') && document.querySelector(${JSON.stringify(taskEditors)})?.value===${JSON.stringify(editedBody)}`,
      ),
    "Reopening the focused result retains unfinished edits",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await send("Page.reload");
  await bodyText("Complete review checklist");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-artifact-editor textarea').value",
    ),
    editedBody,
    "Unfinished edits survive refresh",
  );
  await click("Edit a copy");
  await bodyText("You already have an unsaved draft");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-artifact-editor textarea').value",
    ),
    editedBody,
    "Opening a copy never replaces unfinished edits",
  );
  await fs.writeFile(
    path.join(repo, "test-results/content-editor-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await click("Save for review");
  await until(
    async () =>
      (await snapshot()).data.artifacts.some(
        (artifact) =>
          artifact.taskId === editorial.id && artifact.body === editedBody,
      ),
    "The edited copy saves as its own draft",
  );
  const afterCopy = (await snapshot()).data;
  const savedCopy = afterCopy.artifacts.find(
    (artifact) =>
      artifact.taskId === editorial.id && artifact.body === editedBody,
  );
  assert.equal(savedCopy.provenance, "owner-authored");
  assert.equal(savedCopy.state, "needs_review");
  assert.deepEqual(
    afterCopy.artifacts.find((artifact) => artifact.id === sourceCopy.id),
    sourceCopy,
    "Editing preserves the original body, provenance, digest and review state",
  );
  assert.deepEqual(
    afterCopy.measured,
    beforeCopy.measured,
    "Manual editing makes no model request",
  );
  await send("Page.reload");
  await bodyText("Show the fold with its printed step number");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-work-window .co-artifact')?.dataset.artifactId",
    ),
    savedCopy.id,
    "The newest saved copy is the first result after refresh",
  );
  assert.match(
    await evaluate(
      `document.querySelector('[data-artifact-id="${savedCopy.id}"] small').innerText`,
    ),
    /Draft 2/,
  );
  assert.match(
    await evaluate(
      `document.querySelector('[data-artifact-id="${sourceCopy.id}"] small').innerText`,
    ),
    /Draft 1/,
  );
  assert.deepEqual(
    await evaluate(
      "[...document.querySelectorAll('.co-work-window .co-artifact')].map(draft => draft.open)",
    ),
    [true, false],
    "Review opens the newest copy and collapses older bodies, leaving their labels visible",
  );
  await evaluate(
    `document.querySelector('[data-artifact-id="${sourceCopy.id}"] > summary').click()`,
  );
  assert.equal(
    await evaluate(
      `document.querySelector('[data-artifact-id="${sourceCopy.id}"]').open`,
    ),
    true,
    "The earlier draft remains available for comparison",
  );
  await evaluate(
    `document.querySelector('[data-artifact-id="${sourceCopy.id}"] > summary').click()`,
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-artifact-editor textarea')"),
    false,
    "Confirmed save clears this tab's recovery draft and leaves the editor collapsed after reload",
  );
  await click("Refine draft");
  const reviewFeedback =
    "Keep the checklist. Replace the vague Wednesday instruction with the full worked example.";
  await evaluate(
    `(()=>{const el=document.querySelector('.co-revision-feedback textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(el,${JSON.stringify(reviewFeedback)});el.dispatchEvent(new Event('input',{bubbles:true}));})()`,
  );
  await send("Page.reload");
  await click("Continue requested changes");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-revision-feedback textarea').value",
    ),
    reviewFeedback,
  );
  await click("Create revision request");
  await until(async () => {
    const state = await snapshot();
    const revision = state.data.tasks.find(
      (t) => t.revision?.sourceTaskId === editorial.id,
    );
    return (
      revision &&
      (await evaluate(
        `document.querySelector('.co-work-window .co-job-card')?.dataset.taskId===${JSON.stringify(revision.id)}`,
      ))
    );
  }, "Creating a revision opens that exact request in focused work");
  const revisedState = (await snapshot()).data;
  const revision = revisedState.tasks.find(
    (t) => t.revision?.sourceTaskId === editorial.id,
  );
  assert.equal(
    revisedState.tasks.find((t) => t.id === editorial.id).state,
    "superseded",
  );
  assert.equal(revision.state, "blocked");
  await send("Page.reload");
  await bodyText(reviewFeedback);
  assert.equal(
    await evaluate(
      "document.querySelector('.co-work-window .co-job-card')?.dataset.taskId",
    ),
    revision.id,
  );
  await fs.writeFile(
    path.join(repo, "test-results/content-revision-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await send("Page.navigate", { url: editorialUrl });
  await bodyText("Draft ready");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-work-window button')].some(b=>b.textContent.trim()==='Approve result')",
    ),
    false,
    "Superseded work does not offer a review command the store refuses",
  );
  await click("Review revision request");
  await until(
    () =>
      evaluate(
        `document.querySelector('.co-work-window .co-job-card')?.dataset.taskId===${JSON.stringify(revision.id)}`,
      ),
    "History opens the exact revision instead of dropping into the chat",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.fe-composer')"),
    false,
  );
  const taskNeeds = async (taskId) =>
    (
      await fetch(url + "/api/company/today").then((r) => r.json())
    ).needsYou.filter((n) => n.taskId === taskId);
  assert.deepEqual(
    await taskNeeds(editorial.id),
    [],
    "Superseded drafts leave the active review queue",
  );
  const completion = await ownerPost("task.create", {
    employeeId: editorial.employeeId,
    threadId: editorial.threadId,
    skill: editorial.skill,
    body: "Prepare a fictional folding instruction for final review.",
  });
  const original = await ownerPost("artifact.create", {
    id: completion.id,
    expectedVersion: completion.version,
    body: "Original fictional instruction: fold the paper.",
  });
  const completionState = (await snapshot()).data;
  const corrected = await ownerPost("artifact.create", {
    id: completion.id,
    expectedVersion: completionState.tasks.find((t) => t.id === completion.id)
      .version,
    body: "Corrected fictional instruction: align opposite corners, then crease the diagonal.",
  });
  const pendingReview = await taskNeeds(completion.id);
  assert.equal(
    pendingReview.length,
    1,
    "Two drafts produce one active review request",
  );
  assert.match(pendingReview[0].detail, /2 drafts/);
  const completionUrl = `${url}/?view=employee&employee=${completion.employeeId}&thread=${completion.threadId}&work=${completion.id}`;
  await send("Page.navigate", { url: completionUrl });
  await bodyText("Corrected fictional instruction");
  await evaluate(
    `[...document.querySelectorAll('[data-artifact-id="${corrected.id}"] button')].find(button=>button.textContent.trim()==='Approve result').click()`,
  );
  await until(
    async () =>
      (await taskNeeds(completion.id)).some((n) => n.kind === "completion"),
    "Approval offers the completion step",
  );
  assert.equal((await taskNeeds(completion.id)).length, 1);
  assert.deepEqual(
    (await snapshot()).data.artifacts.find((a) => a.id === original.id),
    original,
  );
  await click("Complete after review");
  await until(
    async () =>
      (await snapshot()).data.tasks.find((t) => t.id === completion.id)
        .state === "completed",
    "Owner completes the task using the corrected copy",
  );
  await send("Page.reload");
  await bodyText("Draft ready");
  const completedState = (await snapshot()).data;
  assert.deepEqual(
    await taskNeeds(completion.id),
    [],
    "Completed work leaves no stale review request",
  );
  assert.deepEqual(
    completedState.artifacts.find((a) => a.id === original.id),
    original,
    "Completion does not approve or change the original draft",
  );
  assert.equal(
    completedState.artifacts.find((a) => a.id === corrected.id).state,
    "approved",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-work-window [data-artifact-id]')?.dataset.artifactId",
    ),
    corrected.id,
    "Completed work opens with the approved copy first",
  );
  assert.deepEqual(
    await evaluate(
      "[...document.querySelectorAll('.co-work-window .co-artifact')].map(draft => draft.open)",
    ),
    [true, false],
    "Completed work keeps the approved copy expanded and earlier alternatives collapsed",
  );
  assert.deepEqual(
    completedState.measured,
    completionState.measured,
    "Review and completion make no model request",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/content-completed-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  const revisionCopy = await ownerPost("artifact.create", {
    id: revision.id,
    expectedVersion: completedState.tasks.find((t) => t.id === revision.id)
      .version,
    body:
      "Fictional folding checklist. " +
      "Fold the fictional paper. ".repeat(130),
  });
  await send("Page.navigate", {
    url: `${url}/?view=employee&employee=${revision.employeeId}&thread=${revision.threadId}&work=${revision.id}&mode=work`,
  });
  await until(
    () =>
      evaluate(
        `!!document.querySelector('[data-artifact-id="${revisionCopy.id}"] .co-draft-checks')`,
      ),
    "Advisory checks for the latest draft",
  );
  await evaluate(
    `(()=>{const checks=document.querySelector('[data-artifact-id="${revisionCopy.id}"] .co-draft-checks');if(!checks.open)checks.querySelector('summary').click();})()`,
  );
  await bodyText("up to about 750–900");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-draft-checks').textContent.includes('Outside limit')",
    ),
    false,
    "Modest length differences are not failures",
  );
  await click("Refine draft");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].some(b=>b.textContent.trim()==='Use draft check findings')",
    ),
    false,
  );
  const repairText =
    "Make the opening more concrete and preserve earlier feedback.";
  await evaluate(
    `(()=>{const el=document.querySelector('.co-revision-feedback textarea');Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(el,${JSON.stringify(repairText)});el.dispatchEvent(new Event('input',{bubbles:true}));})()`,
  );
  const measuredBeforeRepair = (await snapshot()).data.measured;
  await send("Page.reload");
  await bodyText("Continue requested changes");
  await click("Continue requested changes");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-revision-feedback textarea').value",
    ),
    repairText,
  );
  assert.deepEqual(
    (await snapshot()).data.measured,
    measuredBeforeRepair,
    "Draft check findings never dispatch a model request",
  );
  await click("Keep draft and close");
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  const latestFeedback =
    "Keep the worked example and checklist. Remove the closing question.";
  const nextRevision = await ownerPost("artifact.revise", {
    id: revisionCopy.id,
    expectedVersion: revisionCopy.version,
    digest: revisionCopy.digest,
    body: latestFeedback,
  });
  const preparedRevision = await ownerPost("measured.prepare", {
    id: nextRevision.id,
    expectedVersion: nextRevision.version,
    creditAllowance: 1,
  });
  const revisionPacket = await fetch(
    url + "/api/company/measured/" + preparedRevision.id,
  ).then((r) => r.json());
  assert.equal(
    revisionPacket.call.context.revision.priorFeedback[0].feedback,
    reviewFeedback,
  );
  assert.equal(
    revisionPacket.call.context.revision.request.feedback,
    latestFeedback,
  );
  assert.equal(
    revisionPacket.call.context.revision.originalDraft,
    revisionCopy.body,
  );
  assert.equal(preparedRevision.measured.policy.effort, "high");
  assert.equal(preparedRevision.measured.policy.outputTarget, 6000);
  await send("Page.navigate", {
    url: `${url}/?view=employee&employee=${nextRevision.employeeId}&thread=${nextRevision.threadId}&work=${nextRevision.id}&mode=work`,
  });
  await click("Review task and context");
  await bodyText("Earlier requested changes retained");
  await bodyText(reviewFeedback);
  await bodyText(latestFeedback);
  await evaluate(
    "document.querySelector('.co-work-window .co-safety summary').click()",
  );
  await bodyText("Reasoning effort: high");
  await bodyText("6,000 observed output tokens");
  assert.equal(
    (await snapshot()).data.measured.find((m) => m.id === preparedRevision.id)
      .job.state,
    "proposed",
  );
  // Same skill, repeated requests and separate conversations must remain distinguishable before opening a result.
  const measuredBeforeLabels = (await snapshot()).data.measured;
  const labelled = [];
  for (const [name, count] of [
    ["Fictional paper-kit week one", 2],
    ["Fictional paper-kit week two", 1],
  ]) {
    const thread = await ownerPost("thread.create", {
      employeeId: nextRevision.employeeId,
      name,
    });
    for (let number = 1; number <= count; number++) {
      const task = await ownerPost("task.create", {
        employeeId: nextRevision.employeeId,
        threadId: thread.id,
        skill: "marketing-starter-plan",
        body: "Prepare the fictional weekly content plan.",
      });
      await ownerPost("artifact.create", {
        id: task.id,
        expectedVersion: task.version,
        body: `Fictional result for ${name}, request ${number}.`,
      });
      labelled.push({ task, label: `${name} · Request ${number}` });
    }
  }
  // A conversation with a single request is labelled by its title alone (WorkContext).
  for (const item of labelled) {
    const [name] = item.label.split(" · Request ");
    if (
      labelled.filter((other) => other.label.startsWith(name + " · "))
        .length === 1
    )
      item.label = name;
  }
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1600,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.navigate", {
    url: `${url}/?view=employee&employee=${nextRevision.employeeId}&thread=${labelled[0].task.threadId}&mode=work`,
  });
  for (const item of labelled) {
    await until(
      () =>
        evaluate(
          `Array.from(document.querySelectorAll('.co-cockpit .co-work-context')).some(e=>e.textContent===${JSON.stringify(item.label)})`,
        ),
      "Distinct review label appears",
    );
    await evaluate(
      `Array.from(document.querySelectorAll('.co-cockpit .co-work-context')).find(e=>e.textContent===${JSON.stringify(item.label)}).closest('button').click()`,
    );
    await until(
      () =>
        evaluate(
          `document.querySelector('.co-work-window .co-job-card')?.dataset.taskId===${JSON.stringify(item.task.id)}&&document.querySelector('.co-work-window-head .co-work-context')?.textContent===${JSON.stringify(item.label)}`,
        ),
      "Review label opens the exact task and matches its header",
    );
  }
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        `document.querySelector('.co-work-window-head .co-work-context')?.textContent===${JSON.stringify(labelled[2].label)}`,
      ),
    "Selected review label survives reload",
  );
  const firstLabelTask = (await snapshot()).data.tasks.find(
    (t) => t.id === labelled[0].task.id,
  );
  await ownerPost("task.cancel", {
    id: firstLabelTask.id,
    expectedVersion: firstLabelTask.version,
  });
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await send("Page.navigate", {
    url: `${url}/?view=departments&section=today`,
  });
  await until(
    () =>
      evaluate(
        `Array.from(document.querySelectorAll('.co-today .co-work-context')).some(e=>e.textContent===${JSON.stringify(labelled[1].label)})`,
      ),
    "Request number is stable after earlier work is canceled",
  );
  await evaluate(
    `Array.from(document.querySelectorAll('.co-today .co-work-context')).find(e=>e.textContent===${JSON.stringify(labelled[1].label)}).closest('button').click()`,
  );
  await until(
    () =>
      evaluate(
        `document.querySelector('.co-work-window .co-job-card')?.dataset.taskId===${JSON.stringify(labelled[1].task.id)}&&document.querySelector('.co-work-window-head .co-work-context')?.textContent===${JSON.stringify(labelled[1].label)}`,
      ),
    "Mobile Today opens the exact labelled request",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  assert.deepEqual(
    (await snapshot()).data.measured,
    measuredBeforeLabels,
    "Labelling and opening saved work makes no model request",
  );
  assert.deepEqual(errors, []);
  await send("Page.navigate", {
    url: `${url}/?view=departments&section=business`,
  });
  await bodyText("Website sources and draft requests");
  await evaluate(
    "document.querySelector('.co-business-research').open=true;document.querySelector('.co-business-drafting > details').open=true",
  );
  await click("Prepare brief draft");
  await waitText("Prepared for authorization");
  await click("Review draft request");
  await waitText("I authorize one draft");
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting label')].find(l=>l.textContent.includes('I authorize one draft')).querySelector('input').click()",
  );
  await click("Authorize one brief draft");
  await waitText("The response did not pass evidence checks");
  await waitText("not valid brief JSON");
  await evaluate(
    "[...document.querySelectorAll('.co-business-drafting details')].find(d=>d.querySelector('summary').textContent==='Inspect rejected model response').open=true",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-business-drafting pre')].some(p=>p.textContent==='not JSON <script>fictional()</script>')",
    ),
    true,
  );
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-business-drafting script').length",
    ),
    0,
  );
  data = (await snapshot()).data;
  assert.equal(
    data.businessProfiles.length,
    1,
    "Rejected text never became a profile",
  );
  assert.equal(
    data.businessDrafts.find((d) => d.state === "invalid").rejectedResponse,
    "not JSON <script>fictional()</script>",
  );
  await send("Page.reload");
  await waitText("Website sources and draft requests");
  await evaluate("document.querySelector('.co-business-research').open=true");
  await waitText("Inspect rejected model response");
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
        generatedBriefUsesReadableReviewWithoutHidingUnsavedEdits: true,
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
        firstPlanPreparingIsNotUncertain: true,
        firstPlanOpensExactWork: true,
        recoveredReadClearsOnlyItsOwnNotice: true,
        recoveredReadPreservesUnfinishedRequest: true,
        successfulPollPreservesCommandFailure: true,
        approvedBriefInFirstPlan: true,
        firstPlanSurvivesReload: true,
        firstPlanIsTrackedWork: true,
        firstPlanNeedsResultApproval: true,
        firstPlanCompletedAfterReview: true,
        exactDraftCopyAndDownload: true,
        clipboardFailureRecovery: true,
        fullDraftVisibleOnMobile: true,
        revisionFeedbackSurvivesRefresh: true,
        revisionOpensDirectly: true,
        revisionHistoryKeepsFocusedWork: true,
        narrowWorkLinkSelectsVisibleTab: true,
        chatLeavesNarrowWorkWindow: true,
        editableCopyPreservesOriginal: true,
        latestSavedCopyAppearsFirstWithStableDraftNumbers: true,
        earlierDraftsCollapsedAndExpandable: true,
        editorDraftSurvivesRefresh: true,
        editorNeverOverwritesUnsavedText: true,
        manualEditNeedsReviewWithoutModelCall: true,
        wideWorkKeepsSingleEditorAndDraft: true,
        multipleDraftsShareReviewNeed: true,
        approvedCopyOffersCompletion: true,
        completionPreservesEarlierDrafts: true,
        completedAndSupersededWorkLeavesReviewQueue: true,
        completedWorkShowsApprovedCopyFirst: true,
        repeatedRevisionRetainsFeedback: true,
        reviewLabelsIdentifyConversationAndRequest: true,
        reviewLabelOpensExactWorkAcrossReloadAndMobile: true,
        requestNumbersSurviveEarlierCancellation: true,
        deliverableReviewShowsHighEffortAnd6000Tokens: true,
        draftCheckFeedbackIsEditableAndNeverAutoRuns: true,
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
        modelCalls: 5,
        localRefusalUsesSeparateReceiptWithoutUsageOrReplay: true,
        rejectedResponseInspectableAsTextWithoutImport: true,
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
