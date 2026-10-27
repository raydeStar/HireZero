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
  const importFile = (name, contents) =>
    evaluate(
      `(()=>{const transfer=new DataTransfer();transfer.items.add(new File([${contents}],${JSON.stringify(name)},{type:'text/plain'}));const el=document.querySelector('.co-documents input[type=file]');el.files=transfer.files;el.dispatchEvent(new Event('change',{bubbles:true}));})()`,
    );
  const text =
    "Cobalt campaign reference.\n" +
    "Use patient language for fictional paper kits and consent-based demonstrations.\n".repeat(
      400,
    );
  await click("Add a document");
  await importFile("fictional-brand-guide.md", JSON.stringify(text));
  await waitText("File loaded for review");
  await field("Document title", "Fictional cobalt brand guide");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-editor textarea').value.length>16384",
    ),
    true,
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-document-editor textarea')?.value.length>16384",
      ),
    "Imported unsaved text survives reload",
  );
  await evaluate(
    "window.originalDocumentFetch=window.fetch;window.loseDocumentSave=true;window.fetch=async(input,init)=>{if(window.loseDocumentSave&&String(input).includes('/documents/save')){window.loseDocumentSave=false;await window.originalDocumentFetch(input,init);throw new TypeError('Fictional lost save reply');}return window.originalDocumentFetch(input,init);};",
  );
  await click("Save document draft");
  await waitText("Document draft saved.");
  let records = await docs();
  assert.equal(records.length, 1);
  assert.equal(records[0].state, "draft");
  assert.equal(
    await evaluate(
      `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:'document.save',body:JSON.stringify({title:'Too large for ordinary commands',text:${JSON.stringify(text)}})})}).then(r=>r.status)`,
    ),
    413,
    "Unrelated route keeps its existing size limit",
  );
  assert.equal(
    await evaluate(
      "fetch('/api/company/documents/save',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:'department.create',name:'Unwanted department'})}).then(r=>r.status)",
    ),
    422,
    "Large upload route cannot dispatch other commands",
  );
  assert.equal(records[0].bytes, Buffer.byteLength(text.trim()));
  assert.equal(
    "text" in records[0],
    false,
    "List does not transfer complete documents",
  );
  const sharedId = records[0].id;
  await field("Task or topic", "Cobalt campaign");
  await click("Find relevant excerpts");
  await waitText("No matching approved documents");
  await evaluate("document.querySelector('.co-document-check input').click()");
  await click("Approve document for this audience");
  await waitText("Document approved.");
  await click("Find relevant excerpts");
  await waitText("Keyword matching may miss paraphrases");
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-document-results article').length===2",
    ),
    true,
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-results').innerText.length<4000",
    ),
    true,
  );
  const dept = await post("department.create", {
    name: "Fictional campaign team",
  });
  const employee = await post("employee.hire", {
    departmentId: dept.id,
    roleId: "marketing",
    name: "Fictional document reader",
    responsibility: "Use approved campaign reference material",
  });
  const knowledge = await post("knowledge.save", {
    departmentId: dept.id,
    body: JSON.stringify({
      purpose: "Fictional paper kits",
      audience: "Adult hobbyists",
      positioning: "Paper craft materials",
      voice: "Patient",
      claims: "Paper and printed guides",
      constraints: "Draft only; no publication",
    }),
  });
  await post("knowledge.review", {
    id: knowledge.id,
    expectedVersion: knowledge.version,
    digest: knowledge.digest,
    decision: "approve",
  });
  await send("Page.reload");
  await until(
    () => evaluate("!!document.querySelector('.co-documents')"),
    "Employee audience loaded",
  );
  await click("Add a document");
  await field("Document title", "Fictional private campaign notes");
  await field("Available to employees", "employee:" + employee.id);
  await field(
    "Document text",
    "Cobalt launch uses the fictional codeword willow. This employee-only reference is not shared company context.",
  );
  await click("Save document draft");
  await waitText("Document draft saved.");
  await evaluate("document.querySelector('.co-document-check input').click()");
  await click("Approve document for this audience");
  await waitText("Document approved.");
  const privateDoc = (await docs()).find((d) => d.employeeId === employee.id);
  await field("Task or topic", "willow");
  await click("Find relevant excerpts");
  await waitText("No matching approved documents");
  await field("Employee context", employee.id);
  await click("Find relevant excerpts");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-document-results')?.innerText.includes('codeword willow')",
      ),
    "Employee-scoped retrieval",
  );
  const snapshot = await fetch(url + "/api/company").then((r) => r.json());
  const thread = snapshot.data.threads.find(
    (t) => t.employeeId === employee.id,
  );
  const prepared = await post("conversation.prepare", {
    employeeId: employee.id,
    threadId: thread.id,
    scope: "employee",
    body: "Draft a cobalt note using the willow campaign reference.",
    creditAllowance: 1,
  });
  const packet = await fetch(
    url + "/api/company/measured/" + prepared.attempt.id,
  ).then((r) => r.json());
  assert.equal(
    packet.call.context.documents.excerpts.some(
      (e) => e.documentId === privateDoc.id,
    ),
    true,
  );
  assert.equal(packet.call.context.documents.excerpts.length <= 3, true);
  await click("Revoke document access");
  await waitText("Document revoked.");
  const refused = await evaluate(
    `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:'measured.run',id:${JSON.stringify(prepared.attempt.id)},expectedVersion:${prepared.attempt.version},digest:${JSON.stringify(prepared.attempt.measured.digest)},decision:'accept-soft-limits'})}).then(r=>r.status)`,
  );
  assert.equal(
    refused,
    409,
    "Revoked document prevents dispatch of a prepared packet",
  );
  await click("Find relevant excerpts");
  await waitText("No matching approved documents");
  const neighbor = await evaluate(
    "fetch('/api/companies',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({name:'Fictional neighboring company'})}).then(r=>r.json())",
  );
  assert.deepEqual(
    await fetch(url + "/api/company/documents", {
      headers: { "X-HireZero-Company": neighbor.id },
    }).then((r) => r.json()),
    [],
  );
  assert.equal(
    (
      await fetch(url + "/api/company/documents/" + sharedId, {
        headers: { "X-HireZero-Company": neighbor.id },
      })
    ).status,
    404,
  );
  await click("Add a document");
  await field("Document title", "Unsaved text stays safe");
  await field("Document text", "Keep this fictional local draft.");
  await importFile("malformed.txt", "new Uint8Array([255,254,0])");
  await waitText("not valid UTF-8");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-editor textarea').value",
    ),
    "Keep this fictional local draft.",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-document-editor textarea')?.value==='Keep this fictional local draft.'",
      ),
    "Local draft retained after failed import",
  );
  assert.equal(
    (await docs()).length,
    2,
    "Lost response created no duplicate document",
  );
  await evaluate(
    "window.pendingDocumentFetch=window.fetch;window.dropDocumentReply=true;window.fetch=async(input,init)=>{if(String(input).includes('/company/operations/'))return new Response('{}',{status:404,headers:{'Content-Type':'application/json'}});if(window.dropDocumentReply&&String(input).includes('/documents/save')){window.dropDocumentReply=false;await window.pendingDocumentFetch(input,init);throw new TypeError('Fictional unreadable receipt');}return window.pendingDocumentFetch(input,init);};",
  );
  await click("Save document draft");
  await waitText("A document change needs confirmation");
  assert.equal((await docs()).length, 3);
  await send("Page.reload");
  await waitText("A document change needs confirmation");
  const savedDraftReceipt = await evaluate(
    "JSON.parse(sessionStorage.getItem('hirezero-documents-v1:local-companypending')).id",
  );
  await click("Check document receipt");
  await waitText("Document draft saved.");
  assert.equal(
    (await docs()).length,
    3,
    "Reopened receipt check never repeats the save",
  );
  const unapproved = (await docs()).find((d) => d.state === "draft");
  await click("Edit this draft");
  await evaluate(
    "document.querySelector('.co-document-delete summary').click()",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Permanently delete unapproved document').disabled",
    ),
    true,
  );
  await evaluate("document.querySelector('.co-document-delete input').click()");
  await evaluate(
    "window.beforeDeleteFetch=window.fetch;window.loseDelete=true;window.fetch=async(input,init)=>{if(String(input).includes('/company/operations/'))return new Response('{}',{status:404,headers:{'Content-Type':'application/json'}});if(window.loseDelete&&init?.body&&JSON.parse(init.body).action==='document.delete'){window.loseDelete=false;await window.beforeDeleteFetch(input,init);throw new TypeError('Fictional lost deletion receipt');}return window.beforeDeleteFetch(input,init);};",
  );
  await click("Permanently delete unapproved document");
  await waitText("A document change needs confirmation");
  assert.equal((await docs()).length, 2);
  await send("Page.reload");
  await waitText("A document change needs confirmation");
  await click("Check document receipt");
  await waitText("Unapproved document deleted.");
  assert.equal(
    await evaluate(
      "sessionStorage.getItem('hirezero-documents-v1:local-companydraft')",
    ),
    JSON.stringify({ title: "", text: "", audience: "company" }),
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-document-editor')"),
    false,
  );
  const recoveredSave = await fetch(
    url + "/api/company/operations/" + savedDraftReceipt,
  ).then((r) => r.json());
  assert.equal(recoveredSave.id, unapproved.id);
  assert.equal(recoveredSave.state, "deleted");
  assert.equal(recoveredSave.text, "");
  assert.equal(recoveredSave.title, "Deleted unapproved document");
  await click("Change retention setting");
  await field("Keep discarded drafts", "7");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Save retention setting').disabled",
    ),
    true,
  );
  await evaluate(
    "document.querySelector('.co-document-retention input[type=checkbox]').click()",
  );
  await evaluate(
    "window.beforeRetentionFetch=window.fetch;window.loseRetention=true;window.fetch=async(input,init)=>{if(window.loseRetention&&init?.body&&JSON.parse(init.body).action==='document.retention'){window.loseRetention=false;await window.beforeRetentionFetch(input,init);throw new TypeError('Fictional lost policy reply');}return window.beforeRetentionFetch(input,init);};",
  );
  await click("Save retention setting");
  await waitText("Discarded-draft retention saved.");
  assert.equal(
    (
      await fetch(url + "/api/company/documents/retention").then((r) =>
        r.json(),
      )
    ).discardedDays,
    7,
  );
  assert.equal(
    (
      await fetch(url + "/api/company/documents/retention", {
        headers: { "X-HireZero-Company": neighbor.id },
      }).then((r) => r.json())
    ).discardedDays,
    0,
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-retention-current')?.innerText.includes('deleted after 7 days')",
      ),
    "Retention setting persists after reload",
  );
  assert.equal(
    (await docs()).length,
    2,
    "Reviewed and revoked documents survive draft retention",
  );
  assert.equal(
    await evaluate("!!document.querySelector('.co-document-retention select')"),
    false,
    "Saved rule is shown without an unfinished edit form",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-document-retention').scrollIntoView({block:'start'})",
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/documents-retention-desktop.png"),
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
  await evaluate(
    "document.querySelector('.co-document-retention').scrollIntoView({block:'start'})",
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/documents-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await evaluate(
    "[...document.querySelectorAll('.co-document-library button')].find(b=>b.querySelector('b')?.textContent==='Fictional private campaign notes').click()",
  );
  await until(
    () => evaluate("!!document.querySelector('.co-document-source-delete')"),
    "revoked library copy deletion offered",
  );
  await evaluate(
    "document.querySelector('.co-document-source-delete summary').click()",
  );
  await waitText("Earlier task excerpts and results");
  assert.equal(
    await evaluate(
      "document.querySelector('.co-document-source-delete button').disabled",
    ),
    true,
    "Deleting a reviewed source needs its own consent",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-document-source-delete').scrollIntoView({block:'start'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/document-source-deletion-mobile.png"),
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
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1100,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await evaluate(
    "document.querySelector('.co-document-source-delete').scrollIntoView({block:'center'})",
  );
  await fs.writeFile(
    path.join(repo, "test-results/document-source-deletion-desktop.png"),
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
  await evaluate(
    "document.querySelector('.co-document-source-delete input').click()",
  );
  await evaluate(
    "window.beforeSourceDelete=window.fetch;window.sourceDeleteCalls=0;window.fetch=async(input,init)=>{if(String(input).includes('/company/operations/'))return new Response('{}',{status:404,headers:{'Content-Type':'application/json'}});if(init?.body&&JSON.parse(init.body).action==='document.source.delete'){window.sourceDeleteCalls++;await window.beforeSourceDelete(input,init);throw new TypeError('Fictional lost library deletion receipt');}return window.beforeSourceDelete(input,init);};",
  );
  await evaluate(
    "const button=document.querySelector('.co-document-source-delete button');button.click();button.click()",
  );
  await waitText("A document change needs confirmation");
  assert.equal(
    await evaluate("window.sourceDeleteCalls"),
    1,
    "A double click dispatches one deletion command",
  );
  await until(
    async () => !(await docs()).some((d) => d.id === privateDoc.id),
    "library deletion committed before reload",
  );
  await send("Page.reload");
  await waitText("A document change needs confirmation");
  await click("Check document receipt");
  await waitText("Library copy deleted.");
  await waitText(
    "Earlier task excerpts, other drafts, downloads and backups have not been erased.",
  );
  const deletedSource = await fetch(
    url + "/api/company/documents/" + privateDoc.id,
  ).then((r) => r.json());
  assert.equal(deletedSource.state, "deleted");
  assert.equal(deletedSource.deletionScope, "revoked-source");
  assert.equal(deletedSource.text, "");
  assert.equal(deletedSource.sourceName, null);
  const retainedPacket = await fetch(
    url + "/api/company/measured/" + prepared.attempt.id,
  ).then((r) => r.json());
  assert.deepEqual(
    retainedPacket.call.context.documents.excerpts,
    packet.call.context.documents.excerpts,
    "The receipt honestly preserves earlier task copies",
  );
  const stillRefused = await evaluate(
    `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:'measured.run',id:${JSON.stringify(prepared.attempt.id)},expectedVersion:${prepared.attempt.version},digest:${JSON.stringify(prepared.attempt.measured.digest)},decision:'accept-soft-limits'})}).then(r=>r.status)`,
  );
  assert.equal(
    stillRefused,
    409,
    "Source deletion cannot revive the revoked packet",
  );
  const remainingSource = (await docs()).find((d) => d.id === sharedId);
  await evaluate(
    `[...document.querySelectorAll('.co-document-library button')].find(b=>b.querySelector('b')?.textContent===${JSON.stringify(remainingSource.title)}).click()`,
  );
  await until(
    () => evaluate("!!document.querySelector('.co-document-review')"),
    "remaining source opened",
  );
  if (remainingSource.state === "approved") {
    await click("Revoke document access");
    await waitText("Document revoked.");
  }
  await evaluate(
    "document.querySelector('.co-document-source-delete summary').click()",
  );
  await evaluate(
    "document.querySelector('.co-document-source-delete input').click()",
  );
  await evaluate(`(()=>{const original=window.fetch;window.fetch=async(input,init)=>{
    if(String(input).endsWith('/company/documents')&&(!init?.method||init.method==='GET')){
      window.fetch=original;
      const response=await original(input,init),body=await response.text();
      return new Promise(resolve=>{window.releaseStaleLibrary=()=>resolve(new Response(body,{status:response.status,headers:response.headers}));});
    }
    return original(input,init);
  };})()`);
  await click("Refresh documents");
  await until(
    () => evaluate("typeof window.releaseStaleLibrary==='function'"),
    "older library snapshot held",
  );
  await click("Permanently delete library copy");
  await waitText("Library copy deleted.");
  await until(
    () =>
      evaluate(
        "document.querySelectorAll('.co-document-library li').length===0",
      ),
    "deleted sources absent from the latest library",
  );
  await evaluate(
    "window.releaseStaleLibrary();delete window.releaseStaleLibrary",
  );
  await delay(250);
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-document-library li').length",
    ),
    0,
    "A late library snapshot cannot restore deleted source titles",
  );
  assert.deepEqual(errors, []);
  const ending = await fetch(url + "/api/company").then((r) => r.json());
  assert.equal(
    ending.data.budgets.every((b) => b.used === 0 && b.reserved === 0),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/documents-browser.json"),
    JSON.stringify(
      {
        utf8FileImport: true,
        largeDocumentSave: true,
        draftSurvivesReload: true,
        exactApproval: true,
        lostSaveReconciled: true,
        pendingReceiptSurvivesReload: true,
        deletedDraftReceiptRecovery: true,
        deletedSaveCannotRestoreContent: true,
        deletedEditorCleared: true,
        revokedLibraryCopyDeleted: true,
        deletedLibraryReceiptRecoveredAfterReload: true,
        earlierTaskCopiesExplicitlyRetained: true,
        staleLibraryReadCannotRestoreDeletedSource: true,
        retentionOptInAndReload: true,
        retentionCompanyIsolation: true,
        uploadRouteBounded: true,
        boundedRetrieval: true,
        employeeAudience: true,
        taskPacketProvenance: true,
        revokedPacketRefused: true,
        companyIsolation: true,
        invalidImportPreservesDraft: true,
        mobileOverflow: false,
        liveModelCalls: 0,
        externalRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS documents: imported, reviewed, retrieved, revoked and deleted with recoverable retention; the butler kept every receipt in its proper company.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/documents-browser-host.log"),
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
