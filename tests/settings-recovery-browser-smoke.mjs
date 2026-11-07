// Runs an installed headless Chrome against a disposable company-local host. No user browser profile or live provider.
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
    },
    stdio: ["ignore", "pipe", "pipe"],
  },
);
let hostLog = "";
host.stdout.on("data", (b) => (hostLog += b));
host.stderr.on("data", (b) => (hostLog += b));
let browser, ws;
const until = waiter();
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "this test host owns the listener",
  );
  await until(async () => {
    const r = await fetch(url + "/health");
    return r.ok;
  }, "fresh host health");
  const health = await fetch(url + "/health").then((r) => r.json());
  assert.equal(health.modelExecution, false);
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
  const port = await until(async () => {
    const text = await fs.readFile(
      path.join(temporary, "browser", "DevToolsActivePort"),
      "utf8",
    );
    return Number(text.split("\n")[0]);
  }, "isolated browser debugger");
  const targets = await fetch(`http://127.0.0.1:${port}/json/list`).then((r) =>
    r.json(),
  );
  const target = targets.find((t) => t.type === "page");
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
    height: 1100,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.addScriptToEvaluateOnNewDocument", { source: fetchTracker });
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: "localStorage.setItem('dev-ui:onboarding','\"form\"')",
  });
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("document.body.innerText.includes('Company conversation')"),
    "company UI",
  );
  const idle = pageIdle(evaluate, until);
  // Intercept only this disposable fixture's browser requests. Receipts and counts contain no submitted values.
  const faults = `(()=>{
    const original=window.fetch.bind(window);
    window.__settingsFault=null;window.__settingsBlockReceipts=false;
    window.fetch=async(input,options={})=>{
      const pathname=new URL(typeof input==='string'?input:input.url,location.href).pathname;
      const method=(options.method||'GET').toUpperCase();
      if(pathname.startsWith('/api/company/operations/')&&window.__settingsBlockReceipts)
        return new Response(JSON.stringify({error:'Fictional receipt temporarily unavailable'}),{status:503,headers:{'Content-Type':'application/json'}});
      if(pathname.startsWith('/api/companies/creation/')&&sessionStorage.getItem('settings-block-company-creation'))
        return new Response(JSON.stringify({error:'Fictional creation receipt temporarily unavailable'}),{status:503,headers:{'Content-Type':'application/json'}});
      let action='',requestId;try{const body=JSON.parse(options.body||'{}');action=body.action||'';requestId=body.requestId}catch{}
      if(method!=='GET'){
        const all=JSON.parse(sessionStorage.getItem('settings-smoke-requests')||'[]');
        all.push({path:pathname,method,action,requestId});sessionStorage.setItem('settings-smoke-requests',JSON.stringify(all));
      }
      const fault=window.__settingsFault;
      if(fault&&fault.path===pathname&&(!fault.action||fault.action===action)&&method!=='GET'){
        window.__settingsFault=null;
        if(fault.kind==='reject')return new Response(JSON.stringify({error:'Fictional settings refusal'}),{status:422,headers:{'Content-Type':'application/json'}});
        if(fault.kind==='not-sent')throw new TypeError('Fictional connection failed before dispatch');
        const response=await original(input,options);
        if(fault.kind==='hold')return await new Promise(resolve=>{window.__settingsRelease=()=>{window.__settingsRelease=null;resolve(response)}});
        if(fault.kind==='uncertain'){
          if(pathname==='/api/company/commands')window.__settingsBlockReceipts=true;
          if(pathname==='/api/companies')sessionStorage.setItem('settings-block-company-creation','1');
          throw new TypeError('Fictional response lost after saving');
        }
        return response;
      }
      return original(input,options);
    };
  })()`;
  await evaluate(faults);
  await send("Page.addScriptToEvaluateOnNewDocument", { source: faults });
  const field = async (selector, value) => {
    await until(
      () => evaluate(`!!document.querySelector(${JSON.stringify(selector)})`),
      "field " + selector,
    );
    await evaluate(
      `(()=>{const el=document.querySelector(${JSON.stringify(selector)});const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(60);
  };
  const value = (selector) =>
    evaluate(`document.querySelector(${JSON.stringify(selector)})?.value`);
  const submit = async (selector, twice = false) => {
    await idle();
    await evaluate(
      `(()=>{const form=document.querySelector(${JSON.stringify(selector)});form.requestSubmit();${twice ? "form.requestSubmit();" : ""}})()`,
    );
  };
  const fault = (path, kind, action = "") =>
    evaluate(
      `window.__settingsFault=${JSON.stringify({ path, kind, action })}`,
    );
  const count = (path, action = "") =>
    evaluate(
      `JSON.parse(sessionStorage.getItem('settings-smoke-requests')||'[]').filter(r=>r.path===${JSON.stringify(path)}&&(!${JSON.stringify(action)}||r.action===${JSON.stringify(action)})).length`,
    );
  const section = async (label) => {
    await evaluate(
      "document.querySelector('.co-navigation button[aria-label=\"Company settings\"]').click()",
    );
    await until(
      () => evaluate("!!document.querySelector('.co-advanced-settings')"),
      "settings sections",
    );
    await evaluate("document.querySelector('.co-advanced-settings').open=true");
    await evaluate(
      `[...document.querySelectorAll('.co-hub-tabs button')].find(b=>b.textContent===${JSON.stringify(label)}).click()`,
    );
    await idle();
  };
  const alert = (part) =>
    until(
      () =>
        evaluate(`document.body.innerText.includes(${JSON.stringify(part)})`),
      "visible " + part,
    );
  const members = () =>
    fetch(url + "/api/company/members").then((r) => r.json());
  const companies = () => fetch(url + "/api/companies").then((r) => r.json());

  await section("Members");
  const email = ".co-members input[type=email]",
    inviteForm = ".co-members form";
  await field(email, "owner@example.invalid");
  await submit(inviteForm);
  await alert("already a member");
  await idle();
  assert.equal(
    await value(email),
    "owner@example.invalid",
    "A rejected invitation preserves its email",
  );
  await field(email, "first@example.invalid");
  await submit(inviteForm);
  await until(
    async () =>
      (await members()).some((m) => m.email === "first@example.invalid"),
    "confirmed invitation saved",
  );
  await idle();
  assert.equal(
    await value(email),
    "",
    "A confirmed invitation clears the submitted email",
  );
  // A newer edit is never erased by the response to an earlier submission.
  await field(email, "held@example.invalid");
  await fault("/api/company/commands", "hold", "member.invite");
  await submit(inviteForm);
  await until(
    () => evaluate("typeof window.__settingsRelease==='function'"),
    "invitation response held",
  );
  await field(email, "newer@example.invalid");
  await evaluate("window.__settingsRelease()");
  await idle();
  assert.equal(
    await value(email),
    "newer@example.invalid",
    "Newer invitation text survives an earlier successful response",
  );
  await field(email, "uncertain@example.invalid");
  const invitesBefore = await count("/api/company/commands", "member.invite");
  await fault("/api/company/commands", "uncertain", "member.invite");
  await submit(inviteForm, true);
  await alert("unconfirmed");
  await idle();
  assert.equal(
    await value(email),
    "uncertain@example.invalid",
    "An unconfirmed invitation preserves its email",
  );
  assert.equal(
    await count("/api/company/commands", "member.invite"),
    invitesBefore + 1,
    "Uncertainty and double submit do not duplicate an invitation POST",
  );
  await evaluate("window.__settingsBlockReceipts=false");
  await submit(inviteForm);
  await alert("previous operation is confirmed");
  await idle();
  assert.equal(
    await count("/api/company/commands", "member.invite"),
    invitesBefore + 1,
    "Checking an uncertain invitation only reads its receipt",
  );

  await section("Budgets");
  const limit = 'input[aria-label="Limit for Company"]';
  await evaluate(
    "document.querySelector('table[aria-label=\"Limits\"] tbody tr button').click()",
  );
  await field(limit, "100000");
  await fault("/api/company/commands", "reject", "budget.configure");
  await submit('table[aria-label="Limits"] tbody tr form');
  await alert("Fictional settings refusal");
  await idle();
  assert.equal(
    await value(limit),
    "100000",
    "A rejected budget stays open with the edited amount",
  );
  await submit('table[aria-label="Limits"] tbody tr form');
  await until(
    () =>
      evaluate(
        "!document.querySelector('input[aria-label=\"Limit for Company\"]')",
      ),
    "confirmed budget closes the editor",
  );
  await evaluate(
    "document.querySelector('table[aria-label=\"Limits\"] tbody tr button').click()",
  );
  await field(limit, "110000");
  const budgetsBefore = await count(
    "/api/company/commands",
    "budget.configure",
  );
  await fault("/api/company/commands", "uncertain", "budget.configure");
  await submit('table[aria-label="Limits"] tbody tr form', true);
  await alert("unconfirmed");
  await idle();
  assert.equal(
    await value(limit),
    "110000",
    "An unconfirmed budget keeps the editor and amount",
  );
  assert.equal(
    await count("/api/company/commands", "budget.configure"),
    budgetsBefore + 1,
    "An uncertain budget has only one POST",
  );
  await evaluate("window.__settingsBlockReceipts=false");
  await submit('table[aria-label="Limits"] tbody tr form');
  await alert("previous operation is confirmed");
  await idle();
  assert.equal(
    await count("/api/company/commands", "budget.configure"),
    budgetsBefore + 1,
    "Budget reconciliation does not resubmit",
  );
  await evaluate(
    "[...document.querySelectorAll('table[aria-label=\"Limits\"] tbody tr form button')].find(b=>b.textContent==='Cancel').click()",
  );

  await section("Integrations");
  const setup = 'details[aria-label="OpenRouter setup"]',
    secret = setup + " input[type=password]",
    secretForm = setup + " .co-setup-secret form";
  await until(
    () => evaluate(`!!document.querySelector(${JSON.stringify(setup)})`),
    "OpenRouter setup",
  );
  await evaluate(`document.querySelector(${JSON.stringify(setup)}).open=true`);
  await field(secret, "fictional-settings-key");
  await fault("/api/company/integrations/openrouter/secrets", "reject");
  await submit(secretForm);
  await alert("Fictional settings refusal");
  await idle();
  assert.equal(
    await value(secret),
    "fictional-settings-key",
    "A refused secret stays in its masked field",
  );
  await submit(secretForm);
  await until(
    async () => (await value(secret)) === "",
    "Confirmed secret save clears the submitted value",
  );
  await field(secret, "fictional-held-key");
  await fault("/api/company/integrations/openrouter/secrets", "hold");
  await submit(secretForm, true);
  await until(
    () => evaluate("typeof window.__settingsRelease==='function'"),
    "secret response held",
  );
  await field(secret, "fictional-newer-key");
  await evaluate("window.__settingsRelease()");
  await idle();
  assert.equal(
    await value(secret),
    "fictional-newer-key",
    "Newer secret input survives an earlier successful response",
  );
  const secretsBefore = await count(
    "/api/company/integrations/openrouter/secrets",
  );
  await fault("/api/company/integrations/openrouter/secrets", "uncertain");
  await submit(secretForm, true);
  await alert("response was lost");
  await idle();
  assert.equal(
    await value(secret),
    "fictional-newer-key",
    "An uncertain secret save preserves the masked input",
  );
  assert.equal(
    await count("/api/company/integrations/openrouter/secrets"),
    secretsBefore + 1,
    "An uncertain secret write is never automatically repeated",
  );
  await submit(secretForm);
  await until(
    async () => (await value(secret)) === "",
    "A later explicit confirmed save clears the field",
  );

  await section("Companies");
  const rename = 'form[aria-label="Rename company"]',
    renameInput = rename + " input",
    create = 'form[aria-label="Create company"]',
    createInput = create + " input";
  const initialCompanies = await companies();
  await until(
    async () => (await value(renameInput)) === initialCompanies[0].name,
    "rename starts from the saved company name",
  );
  await field(renameInput, "Fictional Studio");
  await evaluate(
    `document.querySelector(${JSON.stringify(renameInput)}).focus()`,
  );
  await send("Input.dispatchKeyEvent", {
    type: "keyDown",
    key: "Enter",
    code: "Enter",
    text: "\r",
    windowsVirtualKeyCode: 13,
  });
  await send("Input.dispatchKeyEvent", {
    type: "keyUp",
    key: "Enter",
    code: "Enter",
    windowsVirtualKeyCode: 13,
  });
  await until(
    async () => (await companies())[0].name === "Fictional Studio",
    "Enter renames the current company",
  );
  await idle();
  assert.equal(
    (await companies()).length,
    initialCompanies.length,
    "Renaming does not create a company",
  );
  assert.equal(
    await count("/api/companies"),
    0,
    "Enter in Rename never POSTs Create",
  );
  await field(renameInput, "Preserved rename");
  await fault("/api/company/name", "reject");
  await submit(rename);
  await alert("Fictional settings refusal");
  await idle();
  assert.equal(
    await value(renameInput),
    "Preserved rename",
    "Rejected rename preserves the typed name",
  );
  await field(renameInput, "Fictional Studio");
  await field(createInput, "Second fictional studio");
  await fault("/api/companies", "reject");
  await submit(create, true);
  await alert("Fictional settings refusal");
  await idle();
  assert.equal(
    await value(createInput),
    "Second fictional studio",
    "Rejected creation preserves its separate name",
  );
  assert.equal(
    await value(renameInput),
    "Fictional Studio",
    "Creating a company never repurposes the rename field",
  );
  const createsBefore = await count("/api/companies");
  await submit(create, true);
  await until(
    async () => (await companies()).length === initialCompanies.length + 1,
    "one new fictional company created",
  );
  await until(
    () =>
      evaluate(
        "new URLSearchParams(location.search).get('view')===null&&!!document.querySelector('.co-home')",
      ),
    "new company opens Home",
  );
  assert.equal(
    await count("/api/companies"),
    createsBefore + 1,
    "Double submission creates only one company",
  );
  await section("Companies");
  await until(
    async () => (await value(renameInput)) === "Second fictional studio",
    "new company rename field shows its own name",
  );

  for (const [width, height, mobile] of [
    [1440, 1100, false],
    [390, 844, true],
  ]) {
    await send("Emulation.setDeviceMetricsOverride", {
      width,
      height,
      deviceScaleFactor: 1,
      mobile,
    });
    const before = (await companies()).length,
      posts = await count("/api/companies");
    const name = mobile
      ? "Recovered mobile studio"
      : "Recovered desktop studio";
    await field(createInput, name);
    await fault("/api/companies", "uncertain");
    await submit(create, true);
    await alert("creation result is unconfirmed");
    await idle();
    assert.equal(
      (await companies()).length,
      before + 1,
      "Lost reply still created only one company",
    );
    assert.equal(
      await evaluate(
        `document.querySelector(${JSON.stringify(create + " button")}).textContent.trim()`,
      ),
      "Check saved company",
    );
    assert.equal(
      await evaluate(
        `document.querySelector(${JSON.stringify(createInput)})===null`,
      ),
      true,
      "Unconfirmed creation offers recovery instead of a misleading empty name field",
    );
    await submit(create, true);
    await alert("Fictional creation receipt temporarily unavailable");
    await idle();
    assert.equal(
      await count("/api/companies"),
      posts + 1,
      "Checking an unavailable receipt never repeats creation",
    );
    await send("Page.reload");
    await until(
      () => evaluate("!!document.querySelector('.co-navigation')"),
      "workspace after reload",
    );
    await idle();
    await section("Companies");
    await until(
      () =>
        evaluate(
          `document.querySelector(${JSON.stringify(create + " button")})?.textContent.trim()==='Check saved company'`,
        ),
      "creation receipt survives reload",
    );
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth"),
      true,
      "Pending creation fits viewport",
    );
    if (mobile) {
      await evaluate(
        `document.querySelector(${JSON.stringify(create)}).scrollIntoView({block:'center'})`,
      );
      const capture = await send("Page.captureScreenshot", {
        format: "png",
        captureBeyondViewport: false,
      });
      await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
      await fs.writeFile(
        path.join(repo, "test-results/company-creation-recovery-mobile.png"),
        Buffer.from(capture.data, "base64"),
      );
    }
    await evaluate(
      "sessionStorage.removeItem('settings-block-company-creation')",
    );
    await submit(create, true);
    await until(
      () => evaluate("!!document.querySelector('.co-home')"),
      "recovered company opens Home",
    );
    assert.equal(
      await count("/api/companies"),
      posts + 1,
      "Recovery after reload only reads saved state",
    );
    assert.equal((await companies()).length, before + 1);
    await section("Companies");
    await until(
      async () => (await value(renameInput)) === name,
      "recovered company has its saved name",
    );

    const finishName = mobile
      ? "Unsent mobile studio"
      : "Unsent desktop studio";
    const beforeUnsent = (await companies()).length;
    await field(createInput, finishName);
    await fault("/api/companies", "not-sent");
    await submit(create, true);
    await alert("Finish this creation");
    await idle();
    assert.equal(
      (await companies()).length,
      beforeUnsent,
      "Failed transport did not reach the server",
    );
    const originalId = await evaluate(
      "JSON.parse(sessionStorage.getItem('settings-smoke-requests')).filter(r=>r.path==='/api/companies').at(-1).requestId",
    );
    await send("Page.reload");
    await until(
      () => evaluate("!!document.querySelector('.co-navigation')"),
      "workspace after unsent request reload",
    );
    await idle();
    await section("Companies");
    await submit(create);
    await alert("Finish this creation");
    await idle();
    assert.equal(
      (await companies()).length,
      beforeUnsent,
      "Read-only check does not finish automatically",
    );
    await evaluate(
      `(()=>{const button=[...document.querySelectorAll(${JSON.stringify(create + " button")})].find(b=>b.textContent==='Finish this creation');button.click();button.click();})()`,
    );
    await until(
      () => evaluate("!!document.querySelector('.co-home')"),
      "explicit completion opens company",
    );
    assert.equal((await companies()).length, beforeUnsent + 1);
    const lastPosts = await evaluate(
      "JSON.parse(sessionStorage.getItem('settings-smoke-requests')).filter(r=>r.path==='/api/companies').slice(-2)",
    );
    assert.equal(lastPosts[0].requestId, originalId);
    assert.equal(
      lastPosts[1].requestId,
      originalId,
      "Explicit finish retains the exact request identity",
    );
    await section("Companies");
    await until(
      async () => (await value(renameInput)) === finishName,
      "explicit finish retains the original name",
    );
  }

  // Pause is a durable company admission gate. Lost replies are reconciled rather than toggled again.
  const workButton = () =>
    evaluate(
      "document.querySelector('.co-work-control button')?.textContent.trim()",
    );
  for (const [width, height, mobile] of [
    [1440, 1100, false],
    [390, 844, true],
  ]) {
    await send("Emulation.setDeviceMetricsOverride", {
      width,
      height,
      deviceScaleFactor: 1,
      mobile,
    });
    await fault("/api/company/commands", "hold", "company.work.configure");
    const before = await count(
      "/api/company/commands",
      "company.work.configure",
    );
    await evaluate(
      "document.querySelector('.co-work-control button').click();document.querySelector('.co-work-control button').click()",
    );
    await until(
      () => evaluate("typeof window.__settingsRelease==='function'"),
      "held pause response",
    );
    assert.equal(
      await count("/api/company/commands", "company.work.configure"),
      before + 1,
    );
    assert.equal(
      await evaluate(
        "document.querySelector('.co-work-control button').disabled",
      ),
      true,
    );
    await evaluate("window.__settingsRelease()");
    await until(
      async () => (await workButton()) === "Resume new work",
      "saved company pause",
    );
    assert.equal(
      await evaluate("!!document.querySelector('.co-work-paused')"),
      true,
      "Pause visible outside settings controls",
    );
    await section("Today");
    assert.equal(
      await evaluate("!!document.querySelector('.co-work-paused')"),
      true,
      "Pause banner remains visible away from Companies",
    );
    await section("Companies");
    assert.equal(
      await workButton(),
      "Resume new work",
      "Returning to Companies keeps the saved pause",
    );
    if (mobile) {
      await evaluate(
        "document.querySelector('.co-work-control').scrollIntoView({block:'start'})",
      );
      await delay(150);
      await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
      const workImage = await send("Page.captureScreenshot", {
        format: "png",
        captureBeyondViewport: false,
      });
      await fs.writeFile(
        path.join(repo, "test-results/work-control-mobile.png"),
        Buffer.from(workImage.data, "base64"),
      );
    }
    assert.equal(
      await evaluate(
        "document.querySelector('.co-work-control button').disabled",
      ),
      true,
      "Resume requires explicit consent",
    );
    await evaluate("document.querySelector('.co-work-control input').click()");
    await fault("/api/company/commands", "reject", "company.work.configure");
    await evaluate("document.querySelector('.co-work-control button').click()");
    await until(
      () =>
        evaluate(
          "document.querySelector('.co-work-control [role=alert]')?.textContent.includes('Fictional settings refusal')",
        ),
      "resume refusal visible",
    );
    await evaluate(
      "document.querySelectorAll('.co-work-control button')[1].click()",
    );
    await until(
      () =>
        evaluate(
          "document.querySelector('.co-work-control [role=status]')?.textContent==='Current work setting loaded.'",
        ),
      "current pause refreshed",
    );
    await fault("/api/company/commands", "uncertain", "company.work.configure");
    await evaluate("document.querySelector('.co-work-control button').click()");
    await until(
      () =>
        evaluate(
          "document.querySelector('.co-work-control [role=alert]')?.textContent.includes('unconfirmed')",
        ),
      "lost resume response recorded",
    );
    const uncertainPosts = await count(
      "/api/company/commands",
      "company.work.configure",
    );
    assert.equal(
      await evaluate(
        "document.querySelector('.co-work-control button').disabled",
      ),
      true,
      "Unconfirmed change cannot toggle again",
    );
    await evaluate(
      "window.__settingsBlockReceipts=false;document.querySelectorAll('.co-work-control button')[1].click()",
    );
    await until(
      async () => (await workButton()) === "Pause new work",
      "resume reconciled",
    );
    assert.equal(
      await count("/api/company/commands", "company.work.configure"),
      uncertainPosts,
      "Reconciliation never resubmits the pause command",
    );
    assert.equal(
      await evaluate("!!document.querySelector('.co-work-paused')"),
      false,
    );
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth"),
      true,
      "Work controls fit viewport",
    );
  }

  // Closure review reads saved company records only, including failure/retry and navigation during a slow read.
  const reviewResponse = await fetch(url + "/api/company/retirement-review");
  assert.equal(reviewResponse.headers.get("cache-control"), "no-store");
  assert.equal((await reviewResponse.json()).erasureAvailable, false);
  await evaluate(`(()=>{
    const original=window.fetch.bind(window);window.__retirementReads=0;window.__retirementFault=null;
    window.fetch=async(input,options={})=>{
      const pathname=new URL(typeof input==='string'?input:input.url,location.href).pathname;
      if(pathname!=='/api/company/retirement-review')return original(input,options);
      window.__retirementReads++;const fault=window.__retirementFault;window.__retirementFault=null;
      if(fault==='reject')return new Response(JSON.stringify({error:'Fictional review unavailable'}),{status:503,headers:{'Content-Type':'application/json'}});
      const result=await original(input,options);
      if(fault==='hold')return await new Promise(resolve=>{window.__retirementRelease=()=>{window.__retirementRelease=null;resolve(result)}});
      return result;
    };
  })()`);
  const reviewText = () =>
    evaluate("document.querySelector('.co-retirement').innerText");
  for (const [width, height, mobile] of [
    [1440, 1100, false],
    [390, 844, true],
  ]) {
    await send("Emulation.setDeviceMetricsOverride", {
      width,
      height,
      deviceScaleFactor: 1,
      mobile,
    });
    await evaluate(
      "window.__retirementFault='hold';document.querySelector('.co-retirement button').click();document.querySelector('.co-retirement button').click()",
    );
    await until(
      () => evaluate("typeof window.__retirementRelease==='function'"),
      "held review response",
    );
    assert.equal(
      await evaluate(
        "document.querySelector('.co-retirement button').disabled",
      ),
      true,
    );
    assert.match(await reviewText(), /Checking company records/);
    const reads = await evaluate("window.__retirementReads");
    await evaluate("document.querySelector('.co-retirement button').click()");
    assert.equal(
      await evaluate("window.__retirementReads"),
      reads,
      "Duplicate review clicks stay single",
    );
    await evaluate("window.__retirementRelease()");
    await until(
      async () => /saved database records inventoried/.test(await reviewText()),
      "review shown",
    );
    assert.match(await reviewText(), /No outstanding items/);
    assert.match(await reviewText(), /does not confirm.*ready for deletion/);
    await evaluate("document.querySelector('.co-retirement summary').click()");
    assert.match(await reviewText(), /Backups/);
    await evaluate(
      "window.__retirementFault='reject';document.querySelector('.co-retirement button').click()",
    );
    await until(
      () =>
        evaluate(
          "document.querySelector('.co-retirement [role=alert]')?.textContent==='Fictional review unavailable'",
        ),
      "current failure visible",
    );
    assert.doesNotMatch(
      await reviewText(),
      /saved database records inventoried/,
      "Failed refresh does not retain an old inventory",
    );
    await evaluate("document.querySelector('.co-retirement button').click()");
    await until(
      async () => /saved database records inventoried/.test(await reviewText()),
      "review retry succeeds",
    );
    assert.equal(
      await evaluate("!!document.querySelector('.co-retirement [role=alert]')"),
      false,
    );
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth"),
      true,
      "Retirement review fits the viewport",
    );
  }
  await evaluate(
    "document.querySelector('.co-retirement').scrollIntoView({block:'start'})",
  );
  await delay(150);
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  const retirementImage = await send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  await fs.writeFile(
    path.join(repo, "test-results/retirement-mobile.png"),
    Buffer.from(retirementImage.data, "base64"),
  );
  await evaluate(
    "window.__retirementFault='hold';document.querySelector('.co-retirement button').click()",
  );
  await until(
    () => evaluate("typeof window.__retirementRelease==='function'"),
    "second held review",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-hub-tabs button')].find(b=>b.textContent==='Today').click()",
  );
  await until(
    () => evaluate("!document.querySelector('.co-retirement')"),
    "review unmounted",
  );
  await evaluate("window.__retirementRelease()");
  await idle();
  await section("Companies");
  assert.doesNotMatch(
    await reviewText(),
    /saved database records inventoried/,
    "An old response cannot populate a remounted review",
  );

  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth"),
    true,
    "Mobile Companies has no horizontal overflow",
  );
  await evaluate(
    `document.querySelector(${JSON.stringify(rename + " button")}).scrollIntoView({block:'center'})`,
  );
  assert.equal(
    await evaluate(
      `(()=>{const r=document.querySelector(${JSON.stringify(rename + " button")}).getBoundingClientRect(),nav=document.querySelector('.co-navigation').getBoundingClientRect();return r.top>=0&&r.bottom<=nav.top&&r.left>=0&&r.right<=innerWidth})()`,
    ),
    true,
    "Rename submit is reachable above mobile navigation",
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  const screenshot = await send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  await fs.writeFile(
    path.join(repo, "test-results/settings-companies-mobile.png"),
    Buffer.from(screenshot.data, "base64"),
  );
  await fs.writeFile(
    path.join(repo, "test-results/settings-recovery-browser.json"),
    JSON.stringify(
      {
        provider: "unavailable fixture",
        liveModelCalls: 0,
        rejectedInputsRetained: true,
        uncertainInputsRetained: true,
        newerEditsRetained: true,
        confirmedSavesClear: true,
        renameEnterDoesNotCreate: true,
        duplicatePostsPrevented: true,
        creationLostReplyAndReloadRecovery: true,
        creationUnsentExplicitFinish: true,
        mobileCompaniesFits: true,
      },
      null,
      2,
    ),
  );
  assert.equal(errors.length, 0, "No uncaught UI exceptions");
  console.log(
    "PASS settings recovery: desktop/mobile company creation survives lost replies and reloads; pause and retirement checks retain recovery. One company per request, as ordered.",
  );
  await send("Browser.close").catch(() => {});
} catch (e) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/settings-recovery-browser-host.log"),
    hostLog,
  );
  throw e;
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
