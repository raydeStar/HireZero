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
      HIREZERO_BROWSER_MEMORY: "1",
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
    url: url + "/?view=departments&section=memory",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-memory')"),
    "Organizational memory page",
  );
  const click = clicker(evaluate, until);
  const field = async (label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll('.co-memory label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing field '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(50);
  };
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-memory')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const memories = () =>
    fetch(url + "/api/company/memory").then((r) => r.json());
  const tomorrow = new Date();
  tomorrow.setUTCDate(tomorrow.getUTCDate() + 1);
  await click("New memory");
  await field("Title", "Cobalt campaign decision");
  await field("Statement", "Use patient cobalt demonstrations for paper kits.");
  await field("Why this matters", "Owner chose an educational approach.");
  await field("Review on (UTC)", tomorrow.toISOString().slice(0, 10));
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-memory-editor textarea')?.value.includes('patient cobalt')",
      ),
    "Unsaved memory survives reload",
  );
  await evaluate(
    "window.originalMemoryFetch=window.fetch;window.memorySaves=0;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(body.action==='memory.save'){window.memorySaves++;await window.originalMemoryFetch(input,init);throw new TypeError('Fictional lost memory reply');}if(String(input).includes('/company/operations/'))throw new TypeError('Fictional receipt outage');return window.originalMemoryFetch(input,init);};",
  );
  await click("Save memory draft");
  await waitText("Its outcome needs confirmation");
  assert.equal((await memories()).length, 1);
  assert.equal(await evaluate("window.memorySaves"), 1);
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "!![...document.querySelectorAll('button')].find(b=>b.textContent==='Check memory receipt')",
      ),
    "Saved receipt survives reload",
  );
  await click("Check memory receipt");
  await waitText("Memory draft saved.");
  const first = (await memories())[0].memory;
  assert.equal(
    (await memories()).length,
    1,
    "Receipt recovery never repeats the save",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Approve memory').disabled",
    ),
    true,
  );
  await field("Task or topic", "Cobalt campaign");
  await click("Preview memory");
  await waitText("No matching current approved memory");
  await evaluate("document.querySelector('.co-memory-check input').click()");
  await click("Approve memory");
  await waitText("Memory approved.");
  await click("Preview memory");
  await until(
    () =>
      evaluate(
        "document.querySelectorAll('.co-memory-matches article').length===1",
      ),
    "Approved memory retrieval",
  );
  await click("Revise or renew memory");
  await field("Statement", "Use cobalt workshops for the next campaign.");
  await field("Review on (UTC)", tomorrow.toISOString().slice(0, 10));
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  assert.equal((await memories()).filter((row) => row.usable).length, 1);
  assert.equal(
    (await memories()).find((row) => row.usable).memory.id,
    first.id,
  );
  await waitText("Earlier record being replaced");
  await evaluate("document.querySelector('.co-memory-check input').click()");
  await click("Approve and replace earlier memory");
  await waitText("Memory approved.");
  let rows = await memories();
  const replacement = rows.find((row) => row.usable).memory;
  assert.equal(
    rows.find((row) => row.memory.id === first.id).memory.state,
    "superseded",
  );
  await field("Show", "history");
  assert.equal(
    await evaluate("document.querySelectorAll('.co-memory-list li').length"),
    1,
  );
  await field("Show", "all");
  await click("New memory");
  await field("Record type", "prior-work");
  await field("Title", "Cobalt previous campaign");
  await field(
    "Statement",
    "Our approved campaign used patient demonstrations.",
  );
  const sources = await fetch(url + "/api/company/memory/work").then((r) =>
    r.json(),
  );
  assert.equal(sources.length, 1);
  await field("Cite approved work", sources[0].id);
  await evaluate(
    "document.querySelector('.co-memory-editor details summary').click()",
  );
  await waitText("Cobalt campaign used patient paper demonstrations.");
  await field("Exact supporting quote", "patient paper demonstrations");
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  await evaluate("document.querySelector('.co-memory-check input').click()");
  await click("Approve memory");
  await waitText("Memory approved.");
  const prior = (await memories()).find(
    (row) => row.memory.definition.kind === "prior-work",
  ).memory;
  await click("Open cited result");
  await until(
    () =>
      evaluate(
        "location.search.includes('work=') && !document.querySelector('.co-memory')",
      ),
    "Cited result opens",
  );
  await send("Page.navigate", {
    url: url + "/?view=departments&section=memory&memory=" + replacement.id,
  });
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-memory-review')?.innerText.includes('cobalt workshops')",
      ),
    "Exact memory deep link",
  );
  await evaluate(
    "fetch('/__fixture/memory/expire',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:'{}'}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json()})",
  );
  await click("Refresh memory");
  await waitText("Its review date has arrived.");
  assert.equal(
    (await memories()).find((row) => row.memory.id === replacement.id).usable,
    false,
  );
  await send("Page.navigate", {
    url: url + "/?view=departments&section=inbox",
  });
  await until(
    () =>
      evaluate(
        "!![...document.querySelectorAll('.co-inbox-items button')].find(b=>b.textContent==='Review memory')",
      ),
    "Memory review inbox alert",
  );
  await click("Review memory");
  await until(
    () =>
      evaluate(
        `location.search.includes('memory=${replacement.id}') && document.querySelector('.co-memory-review')?.innerText.includes('Its review date has arrived')`,
      ),
    "Inbox opens exact expired memory",
  );
  await click("Revise or renew memory");
  await field("Review on (UTC)", "");
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  await evaluate("document.querySelector('.co-memory-check input').click()");
  await click("Approve and replace earlier memory");
  await waitText("Memory approved.");
  assert.equal((await memories()).filter((row) => row.usable).length, 2);
  // Feedback starts beside the result, but an unrelated unfinished draft keeps its chair.
  await click("New memory");
  await field("Title", "Unfinished owner decision");
  await field("Statement", "Keep this unsaved owner decision intact.");
  await click("Keep draft for later");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  const citedTask = (
    await fetch(url + "/api/company/tasks/" + sources[0].taskId).then((r) =>
      r.json(),
    )
  ).task;
  await send("Page.navigate", {
    url:
      url +
      `/?view=employee&employee=${citedTask.employeeId}&thread=${citedTask.threadId}&mode=work&work=${citedTask.id}`,
  });
  await click("Remember feedback for future work");
  await waitText("Finish your current memory draft first.");
  assert.equal(
    await evaluate("document.querySelector('.co-memory-editor input').value"),
    "Unfinished owner decision",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-feedback button').disabled",
    ),
    true,
  );
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  await click("Capture feedback from this result");
  await field("Title", "Campaign feedback on next steps");
  await field(
    "Statement",
    "Add a checklist of next steps to future campaign reports.",
  );
  const quoteOutage = await send("Page.addScriptToEvaluateOnNewDocument", {
    source:
      "window.blockMemorySource=true;const original=window.fetch;window.fetch=(input,init)=>window.blockMemorySource&&String(input).includes('/company/memory/work/')?Promise.reject(new TypeError('Fictional citation read outage')):original(input,init);",
  });
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-memory-editor textarea')?.value.includes('checklist')",
      ),
    "Feedback draft survives reload",
  );
  await waitText("The information could not be loaded. Try again.");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-editor button[type=submit]').disabled",
    ),
    true,
  );
  await send("Page.removeScriptToEvaluateOnNewDocument", {
    identifier: quoteOutage.identifier,
  });
  await evaluate("window.blockMemorySource=false");
  await click("Check cited result again");
  await until(
    () =>
      evaluate(
        "!document.querySelector('.co-memory-editor button[type=submit]').disabled",
      ),
    "Citation read recovered without discarding feedback",
  );
  assert.equal(
    await evaluate("document.querySelector('.co-memory-editor select').value"),
    "feedback",
  );
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-memory-editor select')[1].value",
    ),
    "e:" + citedTask.employeeId,
  );
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-memory-editor select')[2].value",
    ),
    sources[0].id,
  );
  await waitText("The opening excerpt may be prefilled.");
  await evaluate(
    "document.querySelector('.co-memory-editor').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/memory-feedback-mobile.png"),
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
  await delay(200);
  await fs.writeFile(
    path.join(repo, "test-results/memory-feedback-desktop.png"),
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
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  const feedback = (await memories()).find(
    (row) => row.memory.definition.kind === "feedback",
  ).memory;
  assert.equal(feedback.origin.artifactId, sources[0].id);
  assert.equal(
    feedback.origin.quote,
    "Cobalt campaign used patient paper demonstrations.",
  );
  assert.equal(feedback.definition.employeeId, citedTask.employeeId);
  assert.equal(
    (await memories()).find((row) => row.memory.id === feedback.id).usable,
    false,
  );
  await field("Task or topic", "checklist");
  await field("Retrieval audience", citedTask.employeeId);
  await click("Preview memory");
  await waitText("No matching current approved memory");
  await evaluate("document.querySelector('.co-memory-check input').click()");
  await click("Approve memory");
  await waitText("Memory approved.");
  await click("Preview memory");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-memory-matches')?.innerText.includes('Add a checklist')",
      ),
    "Approved feedback reaches employee retrieval",
  );
  await field("Retrieval audience", "");
  await click("Preview memory");
  await waitText("No matching current approved memory");
  await evaluate(
    `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:'artifact.review',id:${JSON.stringify(sources[0].id)},expectedVersion:${sources[0].version},digest:${JSON.stringify(sources[0].digest)},decision:'revoke'})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json()})`,
  );
  await send("Page.navigate", {
    url: url + "/?view=departments&section=memory&memoryWork=" + sources[0].id,
  });
  await waitText("This result or its supporting context is no longer current.");
  assert.equal(
    await evaluate(
      "![...document.querySelectorAll('.co-memory-feedback button')].some(b=>b.textContent==='Capture feedback from this result')",
    ),
    true,
  );
  assert.equal(
    (await memories()).find((row) => row.memory.id === feedback.id).usable,
    false,
  );
  await send("Page.navigate", {
    url: url + "/?view=departments&section=memory&memory=" + feedback.id,
  });
  await waitText(
    "Its cited work or the context behind that work is no longer approved/current.",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate("document.querySelector('.co-memory').scrollIntoView()");
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  assert.equal(
    await evaluate(
      "(()=>{const header=document.querySelector('.co-memory > header');return header.querySelector('p:last-child').getBoundingClientRect().top>=header.querySelector('h2').getBoundingClientRect().bottom;})()",
    ),
    true,
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/memory-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  // Delete only unused drafts; an old read and a lost reply must not restock the butler's shelf.
  assert.equal(
    await evaluate("!!document.querySelector('.co-memory-delete')"),
    false,
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await click("New memory");
  await field("Title", "Delete this fictional draft");
  await field("Statement", "Unused fictional draft statement.");
  await field("Review on (UTC)", "");
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  const unused = (await memories()).find(
    (row) => row.memory.definition.title === "Delete this fictional draft",
  ).memory;
  await click("Edit saved memory draft");
  await field("Statement", "Unsaved edits to the draft being deleted.");
  await click("Keep draft for later");
  await evaluate(`
    window.beforeMemoryDeleteFetch = window.fetch;
    window.holdMemoryRead = true;
    window.failMemoryRefresh = false;
    window.fetch = async (input, init) => {
      const requestPath = new URL(String(input), location.href).pathname;
      if (requestPath === '/api/company/memory' && (!init?.method || init.method === 'GET')) {
        if (window.holdMemoryRead) {
          window.holdMemoryRead = false;
          const response = await window.beforeMemoryDeleteFetch(input, init);
          return new Promise(resolve => { window.releaseMemoryRead = () => resolve(response); });
        }
        if (window.failMemoryRefresh) throw new TypeError('Fictional refresh outage');
      }
      const response = await window.beforeMemoryDeleteFetch(input, init);
      if (init?.body && JSON.parse(init.body).action === 'memory.delete') window.failMemoryRefresh = true;
      return response;
    };`);
  await click("Refresh memory");
  await until(
    () => evaluate("!!window.releaseMemoryRead"),
    "Old memory read held",
  );
  await evaluate("document.querySelector('.co-memory-delete summary').click()");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-delete button').disabled",
    ),
    true,
  );
  await evaluate("document.querySelector('.co-memory-delete input').click()");
  await click("Permanently delete unapproved memory");
  await waitText("Unapproved memory deleted.");
  await waitText("The information could not be loaded. Try again.");
  await evaluate(
    "window.releaseMemoryRead(); window.fetch = window.beforeMemoryDeleteFetch",
  );
  await delay(150);
  assert.equal(
    await evaluate("!!document.querySelector('.co-memory-review')"),
    false,
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-list').innerText.includes('Delete this fictional draft')",
    ),
    false,
  );
  assert.equal(
    await evaluate(
      "Object.keys(sessionStorage).filter(k => k.startsWith('hirezero-memory-v1:') && k.endsWith(':draft')).length",
    ),
    0,
  );
  assert.equal(
    (
      await fetch(url + "/api/company/memory/" + unused.id).then((r) =>
        r.json(),
      )
    ).memory.state,
    "deleted",
  );
  await click("Refresh memory");
  await click("New memory");
  await field("Title", "Discarded fictional draft");
  await field("Statement", "Unused draft for lost deletion receipt.");
  await field("Review on (UTC)", "");
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  const discardedId = (await memories()).find(
    (row) => row.memory.definition.title === "Discarded fictional draft",
  ).memory.id;
  await click("Discard draft");
  await waitText("Memory discarded.");
  await click("New memory");
  await field("Title", "Keep an unrelated unsaved decision");
  await field(
    "Statement",
    "This separate unsaved draft must survive deletion.",
  );
  await click("Keep draft for later");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate("document.querySelector('.co-memory-delete summary').click()");
  await evaluate(
    "document.querySelector('.co-memory-delete input').click(); document.querySelector('.co-memory-delete').scrollIntoView()",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/memory-delete-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await evaluate(`
    window.originalDeletionFetch = window.fetch; window.memoryDeletes = 0;
    window.fetch = async (input, init) => {
      if (init?.body && JSON.parse(init.body).action === 'memory.delete') {
        window.memoryDeletes++; await window.originalDeletionFetch(input, init);
        throw new TypeError('Fictional lost deletion reply');
      }
      if (String(input).includes('/company/operations/')) throw new TypeError('Fictional receipt outage');
      return window.originalDeletionFetch(input, init);
    };`);
  await click("Permanently delete unapproved memory");
  await waitText("Its outcome needs confirmation");
  assert.equal(await evaluate("window.memoryDeletes"), 1);
  await send("Page.reload");
  await click("Check memory receipt");
  await waitText("Unapproved memory deleted.");
  assert.equal(
    (await memories()).some((row) => row.memory.id === discardedId),
    false,
  );
  assert.equal(
    await evaluate("document.querySelector('.co-memory-editor input').value"),
    "Keep an unrelated unsaved decision",
  );
  assert.equal(
    (
      await fetch(url + "/api/company/memory/" + discardedId).then((r) =>
        r.json(),
      )
    ).memory.state,
    "deleted",
  );
  // Retired text has its own consent; removing one old record must not swallow its successor or an unfinished draft.
  const replacementBeforeDeletion = await fetch(
    url + "/api/company/memory/" + replacement.id,
  ).then((r) => r.json());
  await send("Page.navigate", {
    url: url + "/?view=departments&section=memory&memory=" + first.id,
  });
  await waitText("Delete this saved memory");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-memory-review button')].some(b=>b.textContent==='Revoke memory')",
    ),
    false,
  );
  await evaluate(
    "document.querySelector('.co-memory-source-delete summary').click()",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-source-delete button').disabled",
    ),
    true,
  );
  await evaluate(
    "document.querySelector('.co-memory-source-delete input').click();document.querySelector('.co-memory-source-delete').scrollIntoView({block:'start'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/memory-source-delete-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await evaluate(
    "document.querySelector('.co-memory-source-delete').scrollIntoView({block:'start'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/memory-source-delete-desktop.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await evaluate(`
    window.originalSourceDeletionFetch=window.fetch; window.sourceDeletes=0;
    window.fetch=async(input,init)=>{
      if(init?.body && JSON.parse(init.body).action==='memory.source.delete'){
        window.sourceDeletes++; await window.originalSourceDeletionFetch(input,init);
        throw new TypeError('Fictional lost source deletion reply');
      }
      if(String(input).includes('/company/operations/'))throw new TypeError('Fictional source receipt outage');
      return window.originalSourceDeletionFetch(input,init);
    };`);
  await click("Permanently delete saved memory");
  await waitText("Its outcome needs confirmation");
  assert.equal(await evaluate("window.sourceDeletes"), 1);
  const deletedSource = (
    await fetch(url + "/api/company/memory/" + first.id).then((r) => r.json())
  ).memory;
  assert.equal(deletedSource.state, "deleted");
  assert.equal(deletedSource.deletionScope, "retired-source");
  await send("Page.reload");
  await click("Check memory receipt");
  await waitText("Saved memory deleted. Earlier task copies");
  assert.equal(
    await evaluate("document.querySelector('.co-memory-editor input').value"),
    "Keep an unrelated unsaved decision",
  );
  assert.deepEqual(
    await fetch(url + "/api/company/memory/" + replacement.id).then((r) =>
      r.json(),
    ),
    replacementBeforeDeletion,
  );
  assert.deepEqual(
    (await fetch(url + "/api/company/memory/" + first.id).then((r) => r.json()))
      .memory,
    deletedSource,
  );
  assert.equal(
    (await memories()).some((row) => row.memory.id === first.id),
    false,
  );

  await send("Page.navigate", {
    url: url + "/?view=departments&section=memory&memory=" + feedback.id,
  });
  await until(
    () => evaluate("!!document.querySelector('.co-memory-review')"),
    "feedback record loaded",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-memory-source-delete')"),
    false,
  );
  await click("Revoke memory");
  await waitText("Memory revoked.");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-memory-review button')].some(b=>b.textContent==='Revoke memory')",
    ),
    false,
  );
  await evaluate(
    "document.querySelector('.co-memory-source-delete summary').click()",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-source-delete button').disabled",
    ),
    true,
  );
  await evaluate(
    "document.querySelector('.co-memory-source-delete input').click()",
  );
  await click("Permanently delete saved memory");
  await waitText("Saved memory deleted. Earlier task copies");
  const deletedFeedback = (
    await fetch(url + "/api/company/memory/" + feedback.id).then((r) =>
      r.json(),
    )
  ).memory;
  assert.equal(deletedFeedback.state, "deleted");
  assert.equal(deletedFeedback.origin, null);
  assert.equal(deletedFeedback.definition.statement, "");
  assert.equal(deletedFeedback.definition.employeeId, null);
  assert.equal(
    await evaluate("document.querySelector('.co-memory-editor input').value"),
    "Keep an unrelated unsaved decision",
  );
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/memory-browser.json"),
    JSON.stringify(
      {
        draftSurvivesReload: true,
        lostSaveReceiptRecovered: true,
        explicitApproval: true,
        atomicReplacement: true,
        historyPreserved: true,
        priorWorkCitation: prior.origin.quote,
        expiredMemoryExcluded: true,
        inboxOpensExactMemory: true,
        renewalExplicit: true,
        feedbackStartsFromResult: true,
        feedbackMobileJourney: true,
        unfinishedDraftPreserved: true,
        feedbackDraftSurvivesReload: true,
        citationReadRecoveredWithoutDiscard: true,
        feedbackEmployeeScoped: true,
        feedbackRequiresApproval: true,
        revokedFeedbackSourceExcluded: true,
        exactUnapprovedDeletion: true,
        staleReadCannotRestoreDeletedDraft: true,
        failedRefreshKeepsDeletionConfirmed: true,
        matchingBrowserDraftCleared: true,
        deletionReceiptSurvivesReload: true,
        unrelatedDraftPreservedAfterDeletion: true,
        replacedSourceDeletionRequiresSeparateConsent: true,
        savedSourceDeletionReceiptSurvivesReload: true,
        replacementAndUnrelatedDraftPreserved: true,
        activeFeedbackMustBeRevokedBeforeSourceDeletion: true,
        retiredMemoryDoesNotOfferInvalidRevoke: true,
        mobileOverflow: false,
        liveModelCalls: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS memory: remembered, reviewed and retired; the butler keeps his sources straight.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/memory-browser-host.log"),
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
