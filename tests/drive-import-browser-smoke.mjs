// A fictional Drive service passes through real connection setup, preview and document review.
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
      HIREZERO_BROWSER_DRIVE_IMPORT: "1",
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
    url: url + "/?view=departments&section=documents",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-documents')"),
    "Documents page",
  );
  const click = clicker(evaluate, until);
  const field = async (label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll('.co-documents label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing field');const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(50);
  };
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-documents')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const docs = () =>
    fetch(url + "/api/company/documents").then((r) => r.json());
  const post = (action, fields = {}) =>
    evaluate(
      `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:${JSON.stringify(action)},...${JSON.stringify(fields)}})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})`,
    );
  const fixture = await fetch(url + "/__fixture/drive-document").then((r) =>
    r.json(),
  );
  const editorText = () =>
    evaluate("document.querySelector('.co-document-editor textarea').value");
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
  const route = async (section, id = "local-company") => {
    await send("Page.navigate", {
      url: url + "/?view=departments&section=" + section + "&company=" + id,
    });
    await until(
      () =>
        evaluate(
          `!!document.querySelector(${JSON.stringify(section === "integrations" ? ".co-integrations" : ".co-documents")})`,
        ),
      "company " + section,
    );
  };
  const setValue = async (selector, value) => {
    await evaluate(
      `(()=>{const el=document.querySelector(${JSON.stringify(selector)});if(!el)throw Error('Missing field');const proto=el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event('input',{bubbles:true}));})()`,
    );
    await delay(60);
  };
  const setup = 'details[aria-label="Google Drive documents setup"]';
  await route("integrations");
  await until(
    () => evaluate(`!!document.querySelector(${JSON.stringify(setup)})`),
    "Drive connection setup",
  );
  await evaluate(`document.querySelector(${JSON.stringify(setup)}).open=true`);
  await setValue(
    setup + ' textarea[aria-label="Allowed Drive files"]',
    "https://docs.google.com/document/d/" +
      fixture.fileId +
      "/edit?usp=sharing\n" +
      fixture.fileId,
  );
  await evaluate(
    `document.querySelector(${JSON.stringify(setup + " > form")}).requestSubmit()`,
  );
  await until(
    () =>
      evaluate(
        `document.querySelector(${JSON.stringify(setup)}).innerText.includes('Settings saved.')`,
      ),
    "Drive file allowlist saved",
  );
  const savedSetups = await fetch(url + "/api/company/integrations/setup").then(
    (r) => r.json(),
  );
  assert.deepEqual(
    savedSetups.find((s) => s.id === "google-drive").fields[0].value,
    [fixture.fileId],
    "A pasted link and duplicate ID save one exact file ID",
  );
  await setValue(
    setup + ' textarea[aria-label="Paste Drive service account key"]',
    fixture.key,
  );
  await evaluate(
    `document.querySelector(${JSON.stringify(setup + " .co-setup-secret form")}).requestSubmit()`,
  );
  await until(
    () =>
      evaluate(
        `document.querySelector(${JSON.stringify(setup)}).innerText.includes('Drive service account key saved.')`,
      ),
    "Drive connection key saved",
  );
  await until(
    () =>
      evaluate(
        `document.querySelector(${JSON.stringify(setup + ' textarea[aria-label="Paste Drive service account key"]')}).value.length===0`,
      ),
    "Saved key cleared after settings refresh",
  );
  assert.equal(
    (await fetch(url + "/__fixture/drive-document").then((r) => r.json()))
      .requests,
    0,
    "Setup performs no private reads",
  );
  await route("documents");
  await click("Add a document");
  await evaluate("document.querySelector('.co-drive-import summary').click()");
  await waitText("Configured for 1 selected files");
  await field("Document title", "Fictional connected brand guide");
  await field("Available to employees", "employee:" + fixture.employeeId);
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await click("Import selected file text");
  await waitText("Drive text loaded for review");
  const text = await editorText();
  assert.match(text, /patient demonstrations/);
  assert.match(text, /Drive version: 1/);
  assert.match(text, /Editable snapshot; not synchronized/);
  assert.deepEqual(await docs(), []);
  await evaluate(
    "fetch('/__fixture/drive-mode',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({changed:true})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})",
  );
  await click("Import selected file text");
  await waitText("changed during import");
  assert.equal(await editorText(), text, "Changed source preserves old draft");
  await evaluate(
    "fetch('/__fixture/drive-mode',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({changed:false})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})",
  );
  await evaluate(
    "window.driveFetch=window.fetch;window.fetch=async(input,init)=>{if(String(input).includes('/documents/drive-preview'))throw new TypeError('Fictional lost preview');return window.driveFetch(input,init);}",
  );
  await click("Import selected file text");
  await waitText("Drive preview did not finish. Your draft is unchanged");
  assert.equal(await editorText(), text);
  await evaluate(
    "window.fetch=async(input,init)=>{const response=await window.driveFetch(input,init);if(String(input).includes('/documents/drive-preview')){window.driveReady=true;await new Promise(resolve=>window.releaseDrive=resolve);}return response;}",
  );
  await click("Import selected file text");
  await until(
    () => evaluate("window.driveReady===true"),
    "Drive preview waiting for owner edit",
  );
  await field("Document text", "Keep the owner's newer wording.");
  await evaluate("window.releaseDrive()");
  await waitText("The editor changed during import");
  assert.equal(await editorText(), "Keep the owner's newer wording.");
  await evaluate("window.fetch=window.driveFetch");
  await click("Import selected file text");
  await waitText("Drive text loaded for review");
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-document-editor textarea')?.value.includes('Drive version: 1')",
      ),
    "Connected text draft survives reload",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-editor select').value",
    ),
    "employee:" + fixture.employeeId,
  );
  await evaluate(
    "document.querySelector('.co-drive-import summary').click();document.querySelector('.co-drive-import').scrollIntoView()",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await capture("drive-import-mobile-editor.png");
  await click("Save document draft");
  await waitText("Document draft saved.");
  await field("Employee context", fixture.employeeId);
  await field("Task or topic", "patient demonstrations");
  await click("Find relevant excerpts");
  await waitText("No matching approved documents");
  await evaluate(
    "document.querySelector('.co-document-review .co-document-check input').click()",
  );
  await click("Approve document for this audience");
  await waitText("Document approved.");
  await click("Find relevant excerpts");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-document-results blockquote')?.textContent.includes('patient demonstrations')",
      ),
    "Approved copy retrieved",
  );
  await evaluate(
    "document.querySelector('.co-document-review').scrollIntoView()",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await capture("drive-import-mobile-review.png");
  const originalCopy = (await docs())[0];
  await waitText("Saved text matches the imported copy.");
  await click("Check Drive source");
  await waitText(
    "Drive's extracted text and version match the recorded import.",
  );
  await evaluate(
    "window.sourceFetch=window.fetch;window.fetch=(input,init)=>String(input).includes('/check-source')?Promise.reject(new TypeError('Fictional source outage')):window.sourceFetch(input,init)",
  );
  await click("Check Drive source");
  await waitText(
    "The source check did not finish. Your saved copy is unchanged; try again.",
  );
  assert.equal((await docs())[0].state, "approved");
  await evaluate("window.fetch=window.sourceFetch");
  await evaluate(
    "fetch('/__fixture/drive-mode',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({version:'3',text:'Cobalt Paper Studio uses patient demonstrations with an owner-reviewed checklist.'})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json()})",
  );
  await click("Check Drive source");
  await waitText("Drive text changed. Your saved copy is unchanged.");
  await click("Add a document");
  await field("Document title", "Preserved unrelated draft");
  await field("Document text", "Keep the owner's unfinished text.");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Draft a revision from this copy').disabled",
    ),
    true,
  );
  assert.equal(await editorText(), "Keep the owner's unfinished text.");
  await click("Clear editor");
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
      "document.querySelector('.co-document-origin').scrollIntoView({block:'start'})",
    );
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
      true,
    );
    await capture(`drive-source-check-${size}.png`);
  }
  await click("Draft a revision from this copy");
  await field("Document title", "Fictional refreshed brand guide");
  await send("Page.reload");
  await until(
    () =>
      editorText().then((text) => text.includes("owner-reviewed checklist")),
    "source revision survives reload",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-editor select').value",
    ),
    "employee:" + fixture.employeeId,
  );
  await click("Save document draft");
  await waitText("Document draft saved.");
  await evaluate(
    "document.querySelector('.co-document-review .co-document-check input').click()",
  );
  await click("Approve document for this audience");
  await waitText(
    "Explicitly revoke the earlier approved document before approving its replacement.",
  );
  assert.equal(
    (await docs()).find((d) => d.id === originalCopy.id).state,
    "approved",
  );
  const openCopy = async (title) => {
    await evaluate(
      `[...document.querySelectorAll('.co-document-library button')].find(b=>b.querySelector('b')?.textContent===${JSON.stringify(title)}).click()`,
    );
    await until(
      () =>
        evaluate(
          `document.querySelector('.co-document-review h3')?.textContent===${JSON.stringify(title)}`,
        ),
      "exact saved copy",
    );
  };
  await openCopy("Fictional connected brand guide");
  await click("Revoke document access");
  await waitText("Document revoked.");
  await openCopy("Fictional refreshed brand guide");
  await evaluate(
    "document.querySelector('.co-document-review .co-document-check input').click()",
  );
  await click("Approve document for this audience");
  await waitText("Document approved.");
  assert.equal((await docs()).filter((d) => d.state === "approved").length, 1);
  await waitText("Version 3");
  const neighbor = await evaluate(
    "fetch('/api/companies',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({name:'Fictional disconnected studio'})}).then(r=>r.json())",
  );
  await route("documents", neighbor.id);
  await click("Add a document");
  await evaluate("document.querySelector('.co-drive-import summary').click()");
  await waitText("Add a dedicated Drive service account key");
  assert.equal(
    await evaluate("!!document.querySelector('.co-drive-import select')"),
    false,
  );
  assert.equal(await editorText(), "", "Neighbor has no imported draft");
  await route("integrations");
  await until(
    () => evaluate(`!!document.querySelector(${JSON.stringify(setup)})`),
    "Drive disconnection setup",
  );
  await evaluate(
    `document.querySelector(${JSON.stringify(setup)}).open=true;[...document.querySelectorAll(${JSON.stringify(setup + " .co-setup-secret button")})].find(b=>b.textContent.trim()==='Remove').click()`,
  );
  await until(
    () =>
      evaluate(
        `document.querySelector(${JSON.stringify(setup)}).innerText.includes('Drive service account key removed.')`,
      ),
    "Drive key removed",
  );
  const calls = (
    await fetch(url + "/__fixture/drive-document").then((r) => r.json())
  ).requests;
  await route("documents");
  await click("Add a document");
  await evaluate("document.querySelector('.co-drive-import summary').click()");
  await waitText("Add a dedicated Drive service account key");
  assert.equal(
    await evaluate("!!document.querySelector('.co-drive-import select')"),
    false,
  );
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-document-library button b')].some(b=>b.textContent==='Fictional refreshed brand guide')",
      ),
    "Saved imported document",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-document-library button')].find(b=>b.querySelector('b')?.textContent==='Fictional refreshed brand guide').click()",
  );
  await until(
    () => evaluate("!!document.querySelector('.co-document-review')"),
    "Saved copy opened after disconnect",
  );
  await field("Employee context", fixture.employeeId);
  await field("Task or topic", "patient demonstrations");
  await click("Find relevant excerpts");
  await until(
    () =>
      evaluate("!!document.querySelector('.co-document-results blockquote')"),
    "Approved copy survives Drive disconnection as disclosed",
  );
  await click("Revoke document access");
  await waitText("Document revoked.");
  await click("Find relevant excerpts");
  await waitText("No matching approved documents");
  assert.equal(
    (await fetch(url + "/__fixture/drive-document").then((r) => r.json()))
      .requests,
    calls,
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await evaluate(
    "document.querySelector('.co-document-review').scrollIntoView()",
  );
  await capture("drive-import-desktop.png");
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/drive-import-browser.json"),
    JSON.stringify(
      {
        ownerSetup: true,
        noReadDuringSetup: true,
        secretCleared: true,
        exactFileSelection: true,
        versionedTextCopy: true,
        protectedSourceReceipt: true,
        unchangedSourceCheck: true,
        changedSourceReview: true,
        sourceFailurePreservesCopy: true,
        sourceRevisionPreservesDraftAndAudience: true,
        replacementNeedsSeparateRevocationAndApproval: true,
        changedSourcePreservesDraft: true,
        failedReadPreservesDraft: true,
        concurrentEditPreserved: true,
        reloadRecovery: true,
        mobileSaveAndApproval: true,
        employeeScope: true,
        companyIsolation: true,
        disconnectStopsNewReads: true,
        approvedCopyIndependent: true,
        copyRevocation: true,
        mobileOverflow: false,
        liveExternalCalls: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS Drive import: one invited file, one reviewed copy; the rest of the cabinet stays closed.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/drive-import-browser-host.log"),
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
      throw new Error("Owned fixture still running; preserve temporary data.");
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
