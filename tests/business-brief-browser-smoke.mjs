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
  await field("Website (optional)", "https://paper.example.invalid/");
  await field(
    "What would you like help achieving?",
    "Plan a small craft kit launch",
  );
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-business input')?.value==='Fictional Paper Studio'",
      ),
    "identity draft survives reload",
  );
  await click("Review business identity");
  await waitText("Check the name, website and location before confirming.");
  const unconfirmed = (await snapshot()).data.business;
  const refused = await evaluate(
    `fetch('/api/company/business/research',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({businessVersion:${unconfirmed.version},digest:${JSON.stringify(unconfirmed.digest)},url:'https://paper.example.invalid/about'})}).then(r=>r.status)`,
  );
  assert.equal(
    refused,
    409,
    "Research requires confirmation before reading anything",
  );
  assert.equal((await snapshot()).data.businessSources.length, 0);
  await click("Confirm this is my business");
  await waitText("Business identity confirmed");
  await field("Page address", "https://paper.example.invalid/blocked");
  await click("Read public page");
  await waitText("excluded by the website's crawl policy");
  assert.equal(
    (await snapshot()).data.businessSources.length,
    0,
    "A restricted page never becomes evidence",
  );
  await field("Page address", "https://paper.example.invalid/about");
  await click("Read public page");
  await waitText("Source saved.");
  const source = (await snapshot()).data.businessSources[0];
  assert.ok(source);
  assert.equal(
    source.text,
    undefined,
    "Dashboard does not copy raw evidence into every poll",
  );
  await field("Products and services", "Paper craft kits for adult hobbyists");
  await field("Evidence for products and services", "sourced");
  await field("Source for products and services", source.id);
  await field("Exact supporting quote", "Paper craft kits for adult hobbyists");
  await evaluate(
    `window.businessFetch=window.fetch;window.failBusinessSave=true;window.fetch=async(input,init)=>{const request=typeof input==='string'?input:input.url;const body=init?.body?JSON.parse(init.body):{};if(window.failBusinessSave&&request.endsWith('/company/commands')&&body.action==='business.profile.save'){window.failBusinessSave=false;return new Response(JSON.stringify({error:'Synthetic save failure'}),{status:503,headers:{'Content-Type':'application/json'}});}return window.businessFetch(input,init);};`,
  );
  await click("Save brief draft");
  await waitText("Synthetic save failure");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-business label')].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()==='Products and services').querySelector('textarea').value",
    ),
    "Paper craft kits for adult hobbyists",
  );
  assert.equal((await snapshot()).data.businessProfiles.length, 0);
  await click("Save brief draft");
  await waitText("Brief draft saved.");
  assert.equal(
    (await snapshot()).data.knowledge.length,
    0,
    "Saving never approves facts",
  );
  await field("Exact supporting quote", "Invented claim");
  await click("Save brief draft");
  await waitText("A citation must quote a source");
  await field("Exact supporting quote", "Paper craft kits for adult hobbyists");
  await click("Save brief draft");
  await waitText("Brief draft saved.");
  await click("Approve brief for my team");
  await waitText("Business brief approved.");
  let data = (await snapshot()).data;
  assert.equal(data.businessProfiles[0].state, "approved");
  assert.equal(data.knowledge.length, 1);
  assert.match(
    data.knowledge[0].body,
    /Paper craft kits for adult hobbyists \[sourced\]/,
  );
  await send("Page.reload");
  await waitText("Approved business brief");
  await click("Edit a new brief version");
  await field("Products and services", "Owner corrected: kits and workshops");
  await click("Save brief draft");
  await waitText("Brief draft saved.");
  await click("Approve brief for my team");
  await waitText("explicitly revoke the previous approved brief");
  data = (await snapshot()).data;
  assert.equal(data.knowledge.filter((k) => k.state === "approved").length, 1);
  assert.match(
    data.knowledge.find((k) => k.state === "approved").body,
    /\[sourced\]/,
    "Old approval is intact until the owner withdraws it",
  );
  await evaluate(
    "[...document.querySelectorAll('.co-business details')].find(d=>d.querySelector('summary')?.textContent==='Replace or withdraw the approved brief').open=true",
  );
  await click("Revoke this approved brief");
  await waitText("The selected brief was revoked.");
  await click("Approve brief for my team");
  await waitText("Business brief approved.");
  data = (await snapshot()).data;
  assert.equal(data.knowledge.filter((k) => k.state === "approved").length, 1);
  assert.match(
    data.knowledge.find((k) => k.state === "approved").body,
    /Owner corrected: kits and workshops \[owner\]/,
  );
  await click("Edit business identity");
  await field("Business name", "Different Fictional Studio");
  await click("Review business identity");
  await waitText(
    "explicitly revoke the approved business brief before changing this identity",
  );
  assert.equal((await snapshot()).data.business.name, "Fictional Paper Studio");
  await click("Cancel identity edit");
  await evaluate("document.querySelector('.co-business-history').open=true");
  await until(
    () =>
      evaluate(
        "document.querySelectorAll('.co-business-history ol > li').length===6",
      ),
    "All corrections and reviews are retained",
  );
  await evaluate(
    "document.querySelector('.co-business-history ol > li details').open=true",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-business-history').innerText.includes('Owner corrected: kits and workshops')",
    ),
    true,
  );
  const created = await evaluate(
    "fetch('/api/companies',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({name:'Other fictional studio'})}).then(r=>r.json())",
  );
  assert.equal(
    (
      await fetch(url + "/api/company/business/sources/" + source.id, {
        headers: { "X-HireZero-Company": created.id },
      })
    ).status,
    404,
    "Other companies cannot read a source",
  );
  assert.deepEqual(
    (
      await fetch(url + "/api/company/business/history", {
        headers: { "X-HireZero-Company": created.id },
      }).then((r) => r.json())
    ).items,
    [],
    "Other companies cannot read correction history",
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await evaluate("document.querySelector('.co-main').scrollTop=0");
  await fs.writeFile(
    path.join(repo, "test-results/business-brief-desktop.png"),
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
  await delay(150);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
    "No mobile page overflow",
  );
  assert.equal(
    await evaluate(
      "(()=>{const r=[...document.querySelectorAll('.co-business button')].find(b=>b.textContent==='Set up a Marketing employee').getBoundingClientRect();return r.top>=document.querySelector('.fe-topbar').getBoundingClientRect().bottom&&r.bottom<=innerHeight-60;})()",
    ),
    true,
    "The next step is fully visible above mobile navigation",
  );
  await fs.writeFile(
    path.join(repo, "test-results/business-brief-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await evaluate(
    "document.querySelector('.co-business-history').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/business-history-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await click("Set up a Marketing employee");
  await until(
    () =>
      evaluate("!!document.querySelector('[aria-label=\"Guided hiring\"]')"),
    "marketing handoff",
  );
  assert.equal(
    await evaluate(
      "JSON.parse(sessionStorage.getItem('hirezero-hire-draft-v2:local-company')).role",
    ),
    "marketing",
  );
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/business-brief-browser.json"),
    JSON.stringify(
      {
        identityConfirmation: true,
        robotsRefusal: true,
        sourceQuotes: true,
        draftRecovery: true,
        exactApproval: true,
        explicitRevocation: true,
        correctionHistory: true,
        tenantIsolation: true,
        mobileOverflow: false,
        marketingHandoff: true,
        liveModelCalls: 0,
        externalRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS business brief: fictional research, exact review, corrections and recovery; the butler has checked the receipts.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/business-brief-browser-host.log"),
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
