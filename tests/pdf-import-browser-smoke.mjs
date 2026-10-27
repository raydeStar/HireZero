// Fictional PDF bytes pass through the real importer, owner review and scoped retrieval. No external calls.
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
      HIREZERO_BROWSER_WORD_IMPORT: "1",
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
  const fixture = await fetch(url + "/__fixture/word-document").then((r) =>
    r.json(),
  );
  const importPdf = (base64, name = "fictional-brand-guide.pdf") =>
    evaluate(
      `(()=>{const transfer=new DataTransfer();transfer.items.add(new File([Uint8Array.from(atob(${JSON.stringify(base64)}),c=>c.charCodeAt(0))],${JSON.stringify(name)},{type:'application/pdf'}));const el=document.querySelector('.co-documents input[type=file]');el.files=transfer.files;el.dispatchEvent(new Event('change',{bubbles:true}));})()`,
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
  await click("Add a document");
  await importPdf(fixture.pdfMixedBase64);
  await waitText("PDF text loaded for review");
  assert.match(
    await editorText(),
    /Page 2\n\[No selectable text on this page.\]/,
  );
  await importPdf(fixture.pdfContentBase64);
  await waitText("PDF text loaded for review");
  const extracted = await editorText();
  assert.equal(
    extracted,
    "Page 1\nCobalt Paper Studio brand guide\nUse patient demonstrations for beginner paper artists.\nNewsletter cadence: weekly.\n\nPage 2\nOwner review\nThese fictional guidelines are for internal drafts only.",
  );
  assert.deepEqual(
    await docs(),
    [],
    "Extraction does not save or approve a document",
  );
  assert.match(
    await evaluate(
      "document.querySelector('.co-pdf-import-notice').textContent",
    ),
    /original PDF is not retained/,
  );
  await importPdf(fixture.pdfScannedBase64);
  await waitText("No selectable text");
  assert.equal(
    await editorText(),
    extracted,
    "Invalid PDF import preserves editor",
  );
  await evaluate(
    "window.wordOriginalFetch=window.fetch;window.fetch=async(input,init)=>{if(String(input).includes('/documents/extract-pdf'))throw new TypeError('Fictional connection failure');return window.wordOriginalFetch(input,init);}",
  );
  await importPdf(fixture.pdfContentBase64);
  await waitText("PDF import did not finish. Your draft is unchanged");
  assert.equal(await editorText(), extracted);
  await evaluate(
    "window.fetch=async(input,init)=>{const response=await window.wordOriginalFetch(input,init);if(String(input).includes('/documents/extract-pdf')){window.wordReplyReady=true;await new Promise(resolve=>window.releaseWordReply=resolve);}return response;}",
  );
  await importPdf(fixture.pdfContentBase64);
  await until(
    () => evaluate("window.wordReplyReady===true"),
    "PDF response held for concurrent edit",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Save document draft').disabled",
    ),
    true,
  );
  await field("Document text", "Keep the owner's newer text.");
  await evaluate("window.releaseWordReply()");
  await waitText("The editor changed during import");
  assert.equal(await editorText(), "Keep the owner's newer text.");
  await evaluate("window.fetch=window.wordOriginalFetch");
  await importPdf(fixture.pdfContentBase64);
  await waitText("PDF text loaded for review");
  await field("Document title", "Fictional PDF brand guide");
  await field("Available to employees", "employee:" + fixture.employeeId);
  await evaluate(
    "document.querySelector('.co-document-editor').scrollIntoView()",
  );
  await capture("pdf-import-desktop.png");
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-document-editor textarea')?.value.includes('patient demonstrations')",
      ),
    "Unsaved PDF extraction survives reload",
  );
  assert.equal(await editorText(), extracted);
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-editor select').value",
    ),
    "employee:" + fixture.employeeId,
  );
  await waitText("The original PDF is not retained");
  await waitText("Source filename: fictional-brand-guide.pdf");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    "document.querySelector('.co-document-editor').scrollIntoView()",
  );
  await delay(150);
  await evaluate(
    "document.querySelector('.co-pdf-import-notice summary').click()",
  );
  await waitText("Reading order, columns and tables may differ");
  await evaluate(
    "document.querySelector('.co-pdf-import-notice summary').click()",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await capture("pdf-import-mobile-editor.png");
  await evaluate(
    "window.wordSaveFetch=window.fetch;window.loseWordSave=true;window.fetch=async(input,init)=>{if(window.loseWordSave&&String(input).includes('/documents/save')){window.loseWordSave=false;await window.wordSaveFetch(input,init);throw new TypeError('Fictional lost save reply');}return window.wordSaveFetch(input,init);}",
  );
  await click("Save document draft");
  await waitText("Document draft saved.");
  let records = await docs();
  assert.equal(records.length, 1, "Lost save response creates only one draft");
  assert.equal(records[0].state, "draft");
  assert.equal(records[0].employeeId, fixture.employeeId);
  const saved = await fetch(
    url + "/api/company/documents/" + records[0].id,
  ).then((r) => r.json());
  assert.equal(saved.sourceName, "fictional-brand-guide.pdf");
  assert.equal(saved.text, extracted);
  assert.equal("contentBase64" in saved, false);
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Approve document for this audience').disabled",
    ),
    true,
  );
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
    "Approved PDF text available to selected employee",
  );
  await evaluate(
    "document.querySelector('.co-document-review').scrollIntoView()",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await capture("pdf-import-mobile-review.png");
  await field("Employee context", "");
  await click("Find relevant excerpts");
  await waitText("No matching approved documents");
  await click("Revoke document access");
  await waitText("Document revoked.");
  await field("Employee context", fixture.employeeId);
  await click("Find relevant excerpts");
  await waitText("No matching approved documents");
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/pdf-import-browser.json"),
    JSON.stringify(
      {
        pdfTextAndPageLabels: true,
        extractionStateless: true,
        omissionNotice: true,
        sourceFilenameVisible: true,
        mobileImportDetailsExpandable: true,
        invalidImportPreservesDraft: true,
        failedRequestPreservesDraft: true,
        concurrentEditPreserved: true,
        draftSurvivesReload: true,
        mobileSaveAndApproval: true,
        lostSaveReceiptRecovered: true,
        separateApprovalRequired: true,
        employeeScopeEnforced: true,
        revokedTextExcluded: true,
        originalBinaryNotInDocument: true,
        mobileOverflow: false,
        liveModelCalls: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS PDF import: the text is served; the owner's approval remains the main course.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/pdf-import-browser-host.log"),
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
