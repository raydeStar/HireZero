// Real company switching and two browser tabs, with a disposable local host and no model provider.
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
    },
    stdio: ["ignore", "pipe", "pipe"],
  },
);
let hostLog = "";
host.stdout.on("data", (b) => (hostLog += b));
host.stderr.on("data", (b) => (hostLog += b));
let browser;
const sockets = [],
  errors = [];
const until = waiter();
async function connect(target) {
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  sockets.push(ws);
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
  await send("Runtime.enable");
  await send("Page.enable");
  await send("Page.addScriptToEvaluateOnNewDocument", { source: fetchTracker });
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: "localStorage.setItem('dev-ui:onboarding','\"form\"')",
  });
  return { send, evaluate };
}
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "this test host owns its listener",
  );
  await until(
    async () => (await fetch(url + "/health")).ok,
    "fresh host health",
  );
  assert.equal(
    (await fetch(url + "/health").then((r) => r.json())).modelExecution,
    false,
  );
  assert.match(hostLog, /BROWSER FIXTURE: unavailable provider/);
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
  const targets = () =>
    fetch(`http://127.0.0.1:${port}/json/list`).then((r) => r.json());
  const first = (await targets()).find((t) => t.type === "page");
  assert.ok(first);
  const { send, evaluate } = await connect(first);
  until.diagnose = pageDiagnosis(evaluate);
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("!!document.querySelector('.co-home')"),
    "first company loaded",
  );
  const click = clicker(evaluate, until);
  const name = () =>
    evaluate("document.querySelector('.co-onboarding input').value");
  const field = async (label, value, scope = ".co-main") => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll(${JSON.stringify(scope + " label")})].find(l=>l.childNodes[0]?.textContent.trim()===${JSON.stringify(label)});if(!label)throw Error('Field missing: '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(70);
  };
  const guide = async () => {
    await click("People");
    await until(
      () =>
        evaluate("!!document.querySelector('[aria-label=\"Guided hiring\"]')"),
      "hiring guide loaded",
    );
  };
  const reloadGuide = async () => {
    await send("Page.reload");
    await until(
      () =>
        evaluate("!!document.querySelector('[aria-label=\"Guided hiring\"]')"),
      "hiring guide reloaded",
    );
  };
  const switchCompany = async (id) => {
    await click("Settings");
    await evaluate("document.querySelector('.co-advanced-settings').open=true");
    await click("Companies");
    await until(
      () => evaluate("!!document.querySelector('.co-companies select')"),
      "company switcher loaded",
    );
    await field("Switch to", id, ".co-companies");
    await until(
      () => evaluate("!!document.querySelector('.co-home')"),
      "selected company loaded",
    );
    await guide();
  };
  const snapshot = (id) =>
    fetch(url + "/api/company", {
      headers: id ? { "X-HireZero-Company": id } : {},
    }).then((r) => r.json());
  const create = await evaluate(
    "fetch('/api/companies',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({name:'Fictional second studio'})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})",
  );
  const companyA = "local-company",
    companyB = create.id;
  const keyA = "hirezero-hire-draft-v2:" + companyA,
    keyB = "hirezero-hire-draft-v2:" + companyB,
    legacyKey = "hirezero-hire-draft-v1";
  await guide();
  await field("Name", "First company candidate", ".co-onboarding");
  assert.equal(
    await evaluate(
      `JSON.parse(sessionStorage.getItem(${JSON.stringify(keyA)})).name`,
    ),
    "First company candidate",
    "First draft is scoped to the first company",
  );
  await switchCompany(companyB);
  assert.notEqual(
    await name(),
    "First company candidate",
    "A second company never receives the first profile draft",
  );
  await field("Name", "Second company candidate", ".co-onboarding");
  assert.equal(
    await evaluate(
      `JSON.parse(sessionStorage.getItem(${JSON.stringify(keyB)})).name`,
    ),
    "Second company candidate",
    "Second draft has its own storage key",
  );
  await switchCompany(companyA);
  assert.equal(
    await name(),
    "First company candidate",
    "Returning to the first company restores its own draft",
  );
  await switchCompany(companyB);
  assert.equal(
    await name(),
    "Second company candidate",
    "Roundtrip preserves the second draft",
  );
  await switchCompany(companyA);

  // Another tab changes the shared selector. The first tab must keep serving the company it actually loaded.
  const createdTarget = await send("Target.createTarget", {
    url: "about:blank",
  });
  const secondTarget = await until(
    async () => (await targets()).find((t) => t.id === createdTarget.targetId),
    "second browser tab",
  );
  const second = await connect(secondTarget);
  await second.send("Page.navigate", { url: url + "/" });
  await until(
    () => second.evaluate("!!document.querySelector('.co-home')"),
    "second tab loaded",
  );
  await second.evaluate(
    `localStorage.setItem('dev-ui:company',JSON.stringify(${JSON.stringify(companyB)}))`,
  );
  await second.send("Page.reload");
  await until(
    () => second.evaluate("!!document.querySelector('.co-home')"),
    "second tab switched to second company",
  );
  assert.equal(
    await evaluate("JSON.parse(localStorage.getItem('dev-ui:company'))"),
    companyB,
    "The second tab changed shared browser storage",
  );
  await click("Settings");
  await evaluate("document.querySelector('.co-advanced-settings').open=true");
  await click("Departments");
  await field("New department", "First-tab-only department");
  await click("Create department");
  await until(
    async () =>
      (await snapshot(companyA)).data.departments.some(
        (d) => d.name === "First-tab-only department",
      ),
    "first tab writes to its loaded company",
  );
  assert.equal(
    (await snapshot(companyB)).data.departments.length,
    0,
    "Shared preference changes cannot redirect another tab’s command",
  );
  await guide();
  assert.equal(
    await name(),
    "First company candidate",
    "The original tab also keeps its draft scope",
  );
  await send("Target.closeTarget", { targetId: createdTarget.targetId });
  await evaluate("localStorage.setItem('dev-ui:company',JSON.stringify(''))");

  // A legacy profile with no server anchor needs the owner's explicit choice of company.
  const legacy = {
    role: "marketing",
    name: "Unassigned legacy candidate",
    departmentId: "new",
    departmentName: "Fictional legacy department",
    responsibility: "Fictional draft responsibility",
    employeeId: "",
    values: {},
    knowledgeId: "",
    step: "profile",
  };
  await evaluate(
    `sessionStorage.removeItem(${JSON.stringify(keyA)});sessionStorage.setItem(${JSON.stringify(legacyKey)},${JSON.stringify(JSON.stringify(legacy))})`,
  );
  await reloadGuide();
  assert.notEqual(
    await name(),
    legacy.name,
    "An unanchored legacy draft is not applied automatically",
  );
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-onboarding button')].some(b=>b.textContent==='Use saved draft in this company')",
      ),
    "explicit legacy import offered",
  );
  assert.ok(
    await evaluate(`sessionStorage.getItem(${JSON.stringify(legacyKey)})`),
    "Unassigned legacy draft is retained before consent",
  );
  // A failed scoped write must not eat the only saved copy. Even a butler needs a receipt.
  await evaluate(
    `window.fixtureStorageSetItem=Storage.prototype.setItem;Storage.prototype.setItem=function(k,v){if(k===${JSON.stringify(keyA)})throw new Error('Synthetic draft storage failure');return window.fixtureStorageSetItem.call(this,k,v);}`,
  );
  await click("Use saved draft in this company");
  assert.ok(
    await evaluate(`sessionStorage.getItem(${JSON.stringify(legacyKey)})`),
    "Legacy source survives failed scoped persistence",
  );
  await evaluate("Storage.prototype.setItem=window.fixtureStorageSetItem");
  await reloadGuide();
  await click("Use saved draft in this company");
  assert.equal(
    await name(),
    legacy.name,
    "Explicit import restores the unassigned profile",
  );
  assert.equal(
    await evaluate(`sessionStorage.getItem(${JSON.stringify(legacyKey)})`),
    null,
    "Successful scoped persistence consumes the legacy key",
  );
  assert.equal(
    await evaluate(
      `JSON.parse(sessionStorage.getItem(${JSON.stringify(keyA)})).name`,
    ),
    legacy.name,
    "Imported draft is saved only in the chosen company",
  );

  // Persistent references bind an older draft to its actual company, never whichever company opens first.
  const department = (await snapshot(companyA)).data.departments.find(
    (d) => d.name === "First-tab-only department",
  );
  const employee = await evaluate(
    `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1','X-HireZero-Company':${JSON.stringify(companyA)}},body:JSON.stringify({requestId:crypto.randomUUID(),action:'employee.hire',departmentId:${JSON.stringify(department.id)},roleId:'marketing',name:'Anchored first-company employee',responsibility:'Fictional approved boundary'})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})`,
  );
  const anchored = {
    ...legacy,
    name: employee.name,
    employeeId: employee.id,
    departmentId: department.id,
    createdDepartmentId: department.id,
    values: { purpose: "First-company legacy context sentinel" },
    step: "context",
  };
  await evaluate(
    `sessionStorage.removeItem(${JSON.stringify(keyA)});sessionStorage.removeItem(${JSON.stringify(keyB)});sessionStorage.setItem(${JSON.stringify(legacyKey)},${JSON.stringify(JSON.stringify(anchored))})`,
  );
  await switchCompany(companyB);
  assert.equal(
    await evaluate(
      "document.querySelector('.co-onboarding').innerText.includes('First-company legacy context sentinel')",
    ),
    false,
    "Wrong-company legacy context stays out of the guide",
  );
  assert.notEqual(
    await name(),
    anchored.name,
    "Wrong-company legacy employee is not selected",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-onboarding button')].some(b=>b.textContent==='Use saved draft in this company')",
    ),
    false,
    "An anchored draft cannot be imported into a different company",
  );
  assert.ok(
    await evaluate(`sessionStorage.getItem(${JSON.stringify(legacyKey)})`),
    "Wrong-company legacy draft is retained for its owner company",
  );
  await switchCompany(companyA);
  await until(
    () =>
      evaluate("!!document.querySelector('.co-onboarding fieldset textarea')"),
    "matching legacy context automatically resumed",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-onboarding fieldset textarea').value",
    ),
    "First-company legacy context sentinel",
    "Matching persistent references restore the correct context",
  );
  assert.equal(
    await evaluate(
      `JSON.parse(sessionStorage.getItem(${JSON.stringify(keyA)})).employeeId`,
    ),
    employee.id,
    "Anchored draft is migrated to its own company key",
  );
  assert.equal(
    await evaluate(`sessionStorage.getItem(${JSON.stringify(legacyKey)})`),
    null,
    "Confirmed anchored migration consumes the legacy key",
  );
  const pendingLegacy = {
    ...legacy,
    name: "Unconfirmed legacy candidate",
    pending: {
      action: "department.create",
      requestId: "00000000-0000-4000-8000-000000000001",
    },
  };
  await evaluate(
    `sessionStorage.removeItem(${JSON.stringify(keyA)});sessionStorage.setItem(${JSON.stringify(legacyKey)},${JSON.stringify(JSON.stringify(pendingLegacy))})`,
  );
  await reloadGuide();
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-onboarding button')].some(b=>b.textContent==='Check saved state in this company')",
      ),
    "unconfirmed legacy receipt can be checked",
  );
  await evaluate(
    "(()=>{const original=window.fetch.bind(window);window.fixtureRecoveryWrites=0;window.fixtureRecoveryReads=0;window.fetch=(input,options)=>{if(options?.method&&options.method!=='GET')window.fixtureRecoveryWrites++;if(String(input).includes('/operations/'))window.fixtureRecoveryReads++;return original(input,options);};})()",
  );
  await click("Check saved state in this company");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-onboarding').innerText.includes('This save could not be confirmed in this company')",
      ),
    "missing receipt explains recovery without replay",
  );
  assert.equal(
    await evaluate("window.fixtureRecoveryReads"),
    1,
    "Recovery checks the exact receipt once",
  );
  assert.equal(
    await evaluate("window.fixtureRecoveryWrites"),
    0,
    "A missing legacy receipt never causes a POST or replay",
  );
  assert.deepEqual(
    JSON.parse(
      await evaluate(`sessionStorage.getItem(${JSON.stringify(legacyKey)})`),
    ),
    pendingLegacy,
    "A missing receipt preserves the complete legacy draft",
  );
  assert.notEqual(
    await name(),
    pendingLegacy.name,
    "An unconfirmed legacy profile is not adopted",
  );
  assert.equal(
    (await snapshot(companyA)).data.departments.length,
    1,
    "Receipt recovery does not repeat department creation",
  );

  // A late receipt belongs to its old draft even if the owner has moved on and edited a new one.
  await field("Name", "Current scoped candidate", ".co-onboarding");
  const receiptId = "00000000-0000-4000-8000-000000000002";
  const savedDepartment = await evaluate(
    `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1','X-HireZero-Company':${JSON.stringify(companyA)}},body:JSON.stringify({requestId:${JSON.stringify(receiptId)},action:'department.create',name:'Fictional recovered legacy department'})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})`,
  );
  const delayedLegacy = {
    ...legacy,
    name: "Older receipt candidate",
    pending: { action: "department.create", requestId: receiptId },
  };
  await evaluate(
    `sessionStorage.setItem(${JSON.stringify(legacyKey)},${JSON.stringify(JSON.stringify(delayedLegacy))})`,
  );
  await reloadGuide();
  assert.equal(
    await name(),
    "Current scoped candidate",
    "An edited current draft survives discovery of an older pending receipt",
  );
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-onboarding button')].some(b=>b.textContent==='Check saved state in this company')",
      ),
    "legacy receipt check remains available beside an edited profile",
  );
  await evaluate(
    `(()=>{const original=window.fetch.bind(window);window.fixtureLateRecoveryWrites=0;window.fixtureLateRecoveryReads=0;window.fetch=async(input,options)=>{if(options?.method&&options.method!=='GET')window.fixtureLateRecoveryWrites++;const response=await original(input,options);if(String(input).endsWith('/operations/'+${JSON.stringify(receiptId)})){window.fixtureLateRecoveryReads++;await new Promise(resolve=>window.fixtureReleaseLegacy=resolve);}return response;};})()`,
  );
  await click("Check saved state in this company");
  await until(
    () => evaluate("typeof window.fixtureReleaseLegacy==='function'"),
    "saved legacy response held in transit",
  );
  await click("Home", { idle: false });
  await click("People", { idle: false });
  await until(
    () =>
      evaluate("!!document.querySelector('[aria-label=\"Guided hiring\"]')"),
    "new guide instance after navigation",
  );
  await field("Name", "Newer scoped candidate", ".co-onboarding");
  const newerDraft = await evaluate(
    `sessionStorage.getItem(${JSON.stringify(keyA)})`,
  );
  await evaluate("window.fixtureReleaseLegacy()");
  await until(
    () =>
      evaluate(
        `!JSON.parse(sessionStorage.getItem(${JSON.stringify(legacyKey)})).pending`,
      ),
    "late receipt updates only its legacy source",
  );
  assert.equal(
    await name(),
    "Newer scoped candidate",
    "Late recovery never overwrites the visible newer profile",
  );
  assert.equal(
    await evaluate(`sessionStorage.getItem(${JSON.stringify(keyA)})`),
    newerDraft,
    "Late recovery never overwrites the persisted newer profile",
  );
  const recoveredLegacy = JSON.parse(
    await evaluate(`sessionStorage.getItem(${JSON.stringify(legacyKey)})`),
  );
  assert.equal(
    recoveredLegacy.name,
    "Older receipt candidate",
    "Receipt retains its own older profile",
  );
  assert.equal(
    recoveredLegacy.departmentId,
    savedDepartment.id,
    "Receipt recovers the actual saved department",
  );
  assert.equal(
    recoveredLegacy.createdDepartmentId,
    savedDepartment.id,
    "Recovered department remains anchored for later import",
  );
  assert.equal(
    recoveredLegacy.pending,
    undefined,
    "Confirmed receipt clears only its own pending marker",
  );
  assert.equal(
    await evaluate("window.fixtureLateRecoveryReads"),
    1,
    "Late recovery checks the saved receipt once",
  );
  assert.equal(
    await evaluate("window.fixtureLateRecoveryWrites"),
    0,
    "Late receipt handling never repeats the saved command",
  );
  assert.equal(
    (await snapshot(companyA)).data.departments.length,
    2,
    "The receipt-created department exists exactly once",
  );
  assert.equal(
    (await snapshot(companyA)).data.tasks.length,
    0,
    "Draft recovery creates no work or inference",
  );
  assert.equal(
    (await snapshot(companyB)).data.tasks.length,
    0,
    "Second company has no unintended work",
  );
  assert.equal(
    (await snapshot(companyB)).data.employees.length,
    0,
    "No employee is hired into the wrong company",
  );
  assert.equal(errors.length, 0, "No uncaught UI exceptions");
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/company-scope-browser.json"),
    JSON.stringify(
      {
        liveModelCalls: 0,
        companies: 2,
        twoRealTabs: true,
        loadedCompanyCommandsPinned: true,
        scopedDraftRoundtrip: true,
        legacyUnanchoredExplicit: true,
        failedImportRetainsSource: true,
        legacyWrongCompanyHidden: true,
        legacyCorrectCompanyMigrated: true,
        unknownLegacyReceiptRetained: true,
        lateReceiptPreservesNewerDraft: true,
        editedProfileCanCheckOlderReceipt: true,
        legacyRecoveryWrites: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS real Chrome company isolation: two real tabs, scoped draft roundtrip, loaded-company routing, explicit legacy import, failed-write preservation, anchored migration, unknown-receipt recovery and late-receipt draft isolation. The butler serves the right company.",
  );
  await send("Browser.close").catch(() => {});
} catch (e) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/company-scope-browser-host.log"),
    hostLog,
  );
  throw e;
} finally {
  for (const ws of sockets) ws.close();
  browser?.kill();
  host.kill();
  await delay(400);
  // Delete only this script's verified mkdtemp root after its own processes have exited.
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
