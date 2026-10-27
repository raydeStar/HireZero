// Fictional memory uses the real owner UI, HTTP routes and store with a fake model. No external requests.
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
      HIREZERO_BROWSER_MEMORY_ANALYSIS: "1",
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
  const calls = () =>
    fetch(url + "/__fixture/memory-analysis")
      .then((r) => r.json())
      .then((r) => r.calls);
  const reports = () =>
    fetch(url + "/api/company/memory/analyses").then((r) => r.json());
  const usage = () => fetch(url + "/api/company/usage").then((r) => r.json());
  for (const [title, statement] of [
    ["Workshop duration", "Workshops last sixty minutes."],
    ["Workshop planning", "Workshops last ninety minutes."],
  ]) {
    await click("New memory");
    await field("Title", title);
    await field("Statement", statement);
    await field("Why this matters", "Fictional owner guidance.");
    await click("Save memory draft");
    await waitText("Memory draft saved.");
    await evaluate(
      "document.querySelector('.co-memory-review .co-memory-check input').click()",
    );
    await click("Approve memory");
    await waitText("Memory approved.");
  }
  const original = await memories();
  await click("New memory");
  await field("Title", "Unfinished owner note");
  await field("Statement", "Preserve this unfinished note.");
  await field("Why this matters", "Owner has not finished writing.");
  const selectSources = async () => {
    await evaluate(
      "document.querySelector('.co-memory-analysis-prepare').open=true",
    );
    await evaluate(
      "[...document.querySelectorAll('.co-memory-analysis-prepare input[type=checkbox]')].forEach(c=>{if(!c.checked)c.click()})",
    );
    await delay(100);
  };
  await selectSources();
  await click("Prepare memory check");
  await waitText("Ready for your authorization");
  assert.equal(await calls(), 0, "Preparation does not invoke a model");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-editor textarea').value",
    ),
    "Preserve this unfinished note.",
  );
  await click("Review memory request");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Run memory check once').disabled",
    ),
    true,
  );
  await evaluate(
    "document.querySelector('.co-memory-analysis-report .co-memory-check input').click()",
  );
  await evaluate(
    "window.originalAnalysisFetch=window.fetch;window.analysisRuns=0;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(body.action==='memory.analysis.run'){window.analysisRuns++;await window.originalAnalysisFetch(input,init);throw new TypeError('Fictional lost report response');}if(String(input).includes('/company/operations/'))throw new TypeError('Fictional receipt outage');return window.originalAnalysisFetch(input,init)};",
  );
  await click("Run memory check once");
  await waitText("The request may have completed");
  assert.equal(await calls(), 1);
  assert.equal(await evaluate("window.analysisRuns"), 1);
  const first = (await reports())[0];
  assert.equal(first.state, "completed");
  await send("Page.reload");
  await waitText("A request receipt needs checking");
  await click("Check saved memory request");
  await waitText("Memory report ready");
  await waitText("Possible conflict");
  assert.equal(await calls(), 1, "Recovery does not repeat the paid request");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-editor textarea').value",
    ),
    "Preserve this unfinished note.",
  );
  const unchanged = await memories();
  assert.deepEqual(unchanged, original, "Analysis does not edit memory");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Review suggested revision').disabled",
    ),
    true,
    "An unfinished draft cannot be replaced by a suggested correction",
  );
  await click("Review Workshop duration");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-memory-review')?.innerText.includes('Workshops last sixty minutes.')",
      ),
    "Citation opens the reviewed record",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-editor textarea').value",
    ),
    "Preserve this unfinished note.",
  );
  const capture = async (name, selector = ".co-memory-analysis") => {
    await evaluate(
      `document.querySelector(${JSON.stringify(selector)}).scrollIntoView({block:'start',behavior:'instant'})`,
    );
    await delay(150);
    await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
    await fs.writeFile(
      path.join(repo, "test-results/" + name + ".png"),
      Buffer.from(
        (await send("Page.captureScreenshot", { format: "png" })).data,
        "base64",
      ),
    );
  };
  await capture("memory-analysis-desktop");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await capture("memory-analysis-mobile");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-memory-analysis .co-memory-controls select').getBoundingClientRect().width >= 230",
    ),
    true,
    "Saved report remains readable on mobile",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth>innerWidth+1"),
    false,
  );
  await selectSources();
  await click("Prepare memory check");
  await click("Review memory request");
  await evaluate(
    "document.querySelector('.co-memory-analysis-report .co-memory-check input').click()",
  );
  await click("Run memory check once");
  await waitText("Provider outcome needs confirmation");
  assert.equal(await calls(), 2);
  assert.equal((await usage()).current.heldTokens, 26000);
  await send("Page.reload");
  await waitText("Provider outcome needs confirmation");
  await evaluate(
    "[...document.querySelectorAll('.co-memory-analysis details')].find(d=>d.querySelector('summary')?.textContent==='Reconcile provider usage').open=true",
  );
  await field("Provider receipt", "fictional-confirmed-memory");
  await field("Confirmed input tokens", "150");
  await field("Confirmed output tokens", "50");
  await field(
    "How was this verified?",
    "Verified the fictional provider receipt and stopped request.",
  );
  await evaluate(
    "document.querySelector('.co-memory-analysis form .co-memory-check input').click()",
  );
  await click("Record memory check usage");
  await waitText("Provider usage reconciled");
  assert.equal(await calls(), 2);
  assert.equal((await usage()).current.heldTokens, 0);
  assert.equal((await usage()).current.tokens, 340);
  await field("Saved memory report", first.id);
  await waitText("Memory report ready");
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  assert.equal((await memories()).length, 3);
  await evaluate(
    "window.originalRevisionFetch=window.fetch;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(body.action==='memory.analysis.revise'){await window.originalRevisionFetch(input,init);throw new TypeError('Fictional lost revision response');}if(String(input).includes('/company/operations/'))throw new TypeError('Fictional revision receipt outage');return window.originalRevisionFetch(input,init)}",
  );
  await click("Review suggested revision");
  await waitText("Its outcome needs confirmation");
  const drafts = await memories();
  const suggested = drafts.find(
    (r) => r.memory.definition.revisionSource,
  )?.memory;
  assert.ok(suggested);
  assert.equal(suggested.state, "draft");
  assert.equal(
    drafts.find((r) => r.memory.id === suggested.replacement.id).memory.state,
    "approved",
  );
  assert.equal(drafts.length, 4);
  assert.equal(
    await calls(),
    2,
    "Opening a saved suggestion makes no new model request",
  );
  await send("Page.reload");
  await waitText("Its outcome needs confirmation");
  await click("Check memory receipt");
  await waitText("Suggested by a memory report");
  assert.equal(
    (await memories()).length,
    4,
    "Lost save recovery reuses the original revision",
  );
  await click("Edit saved memory draft");
  const editedStatement =
    suggested.definition.statement + " Ask the owner when uncertain.";
  await field("Statement", editedStatement);
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        `document.querySelector('.co-memory-editor textarea')?.value===${JSON.stringify(editedStatement)}`,
      ),
    "Edited suggestion survives reload",
  );
  await click("Save memory draft");
  await waitText("Memory draft saved.");
  await capture("memory-revision-mobile", ".co-memory-review");
  assert.equal(
    await evaluate("document.documentElement.scrollWidth>innerWidth+1"),
    false,
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await capture("memory-revision-desktop", ".co-memory-review");
  await until(
    () =>
      evaluate(
        "!document.querySelector('.co-memory-review .co-memory-check input')?.disabled && window.__smokeIdle()",
      ),
    "Revision ready for separate acceptance",
  );
  await evaluate(
    "document.querySelector('.co-memory-review .co-memory-check input').click()",
  );
  await click("Approve and replace earlier memory");
  await waitText("Memory approved.");
  const accepted = await memories();
  assert.equal(
    accepted.find((r) => r.memory.id === suggested.id).memory.definition
      .statement,
    editedStatement,
  );
  assert.equal(accepted.find((r) => r.memory.id === suggested.id).usable, true);
  assert.equal(
    accepted.find((r) => r.memory.id === suggested.replacement.id).memory.state,
    "superseded",
  );
  assert.equal(
    accepted.find((r) => r.memory.definition.title === "Unfinished owner note")
      .memory.definition.statement,
    "Preserve this unfinished note.",
  );
  await field("Saved memory report", first.id);
  await waitText("Selected memory has changed or lost approval");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Review suggested revision').disabled",
    ),
    true,
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await capture("memory-analysis-stale-mobile");
  assert.equal(
    await evaluate("document.documentElement.scrollWidth>innerWidth+1"),
    false,
  );
  assert.equal(await calls(), 2);
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/memory-analysis-browser.json"),
    JSON.stringify(
      {
        ownerSelectionAndApproval: true,
        quotedFindings: true,
        memoryUnchangedUntilSeparateApproval: true,
        suggestedRevisionEditedAndApproved: true,
        lostRevisionSaveRecoveredWithoutDuplicate: true,
        staleSuggestionDisabled: true,
        unfinishedDraftPreserved: true,
        lostResponseRecoveredWithoutReplay: true,
        unknownOutcomeReconciled: true,
        staleReportMarked: true,
        mobileOverflow: false,
        modelCalls: 2,
        liveModelCalls: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS memory checks: discrepancies served with citations; the owner keeps the red pen.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/memory-analysis-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  const stop = async (child) => {
    if (!child || child.exitCode !== null || child.signalCode !== null) return;
    const exited = new Promise((resolve) => child.once("exit", resolve));
    child.kill();
    await Promise.race([exited, delay(6000)]);
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
