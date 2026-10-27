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
      HIREZERO_BROWSER_SOURCE_FRESHNESS: "1",
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
    url: url + "/?view=departments&section=business",
  });
  await until(
    () => evaluate("!!document.querySelector('.co-business')"),
    "Business brief page",
  );
  const click = clicker(evaluate, until);
  const post = (action, fields = {}) =>
    evaluate(
      `fetch('/api/company/commands',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({requestId:crypto.randomUUID(),action:${JSON.stringify(action)},...${JSON.stringify(fields)}})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})`,
    );
  const snapshot = () => fetch(url + "/api/company").then((r) => r.json());
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-business')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const field = async (label, value, scope = ".co-business-profile") => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll(${JSON.stringify(scope + " label")})].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing field');const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(50);
  };
  let identity = await post("business.identify", {
    body: JSON.stringify({
      name: "Fictional Paper Studio",
      website: "https://paper.example.invalid/",
      goal: "Plan a modest launch",
    }),
  });
  identity = await post("business.confirm", {
    expectedVersion: identity.version,
    digest: identity.digest,
  });
  const source = await evaluate(
    `fetch('/api/company/business/research',{method:'POST',headers:{'Content-Type':'application/json','X-Dev-Request':'1'},body:JSON.stringify({businessVersion:${identity.version},digest:${JSON.stringify(identity.digest)},url:'https://paper.example.invalid/about'})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json();})`,
  );
  const keys = [
    "business",
    "services",
    "customers",
    "voice",
    "market",
    "competitors",
    "opportunities",
    "goals",
    "boundaries",
  ];
  const fields = keys.map((key) =>
    key === "services"
      ? {
          key,
          value: "Paper craft kits for adult hobbyists",
          verification: "sourced",
          citations: [
            {
              sourceId: source.id,
              quote: "Paper craft kits for adult hobbyists",
            },
          ],
        }
      : {
          key,
          value: key === "business" ? "Fictional Paper Studio" : "Unknown",
          verification: key === "business" ? "owner" : "unknown",
          citations: [],
        },
  );
  let profile = await post("business.profile.save", {
    body: JSON.stringify({ businessVersion: identity.version, fields }),
  });
  profile = await post("business.profile.review", {
    id: profile.id,
    expectedVersion: profile.version,
    digest: profile.digest,
    decision: "approve",
  });
  const freshness = () =>
    fetch(url + "/api/company/business/freshness/" + profile.id).then((r) =>
      r.json(),
    );
  await send("Page.reload");
  await waitText("Keep your business brief current");
  await click("Correct the brief");
  await field("What you want to achieve", "Keep this unsaved owner correction");
  await evaluate(
    "document.querySelector('.co-business-freshness details').open=true",
  );
  // A lost HTTP reply and an unavailable receipt must survive reload without repeating the refresh.
  await evaluate(
    "window.originalFreshnessFetch=window.fetch;window.loseFreshness=true;window.blockFreshnessReceipt=true;window.fetch=async(input,init)=>{const path=String(input);const body=init?.body?JSON.parse(init.body):{};if(window.loseFreshness&&body.action==='business.refresh.start'){window.loseFreshness=false;await window.originalFreshnessFetch(input,init);throw new TypeError('Fictional lost refresh reply');}if(window.blockFreshnessReceipt&&path.includes('/operations/'))throw new TypeError('Fictional unavailable receipt');return window.originalFreshnessFetch(input,init);};",
  );
  await click("Refresh selected sources");
  await waitText("A request needs confirmation");
  await send("Page.reload");
  await waitText("A request needs confirmation");
  await click("Check freshness receipt");
  await waitText("Latest refresh: completed");
  assert.equal((await freshness()).runs.length, 1);
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-business-profile label')].find(l=>l.childNodes[0]?.textContent==='What you want to achieve').querySelector('textarea').value",
    ),
    "Keep this unsaved owner correction",
  );
  const state = await freshness();
  assert.equal(
    state.fields.find((f) => f.field === "services").status,
    "possible",
  );
  assert.deepEqual((await snapshot()).data.businessProfiles[0], profile);
  await evaluate("document.querySelector('.co-source-comparison').open=true");
  await click("Show source comparison");
  await waitText("not found in the checked text");
  await waitText("We now offer studio workshops only.");
  await evaluate(
    "document.querySelector('.co-business-fact-reviews').open=true;[...document.querySelectorAll('.co-business-fact-reviews > details')].find(d=>d.querySelector('summary').textContent.startsWith('Products and services')).open=true",
  );
  const reviewScope = ".co-business-fact-reviews > details:nth-of-type(2)";
  await field("Assessment", "outdated", reviewScope);
  await field("Reason", "The checked page no longer offers kits.", reviewScope);
  await evaluate(
    `document.querySelector(${JSON.stringify(reviewScope + " form")}).requestSubmit()`,
  );
  await waitText("Work using this brief is paused");
  assert.equal((await freshness()).blocked, true);
  assert.equal((await snapshot()).data.businessProfiles[0].state, "approved");
  const today = await fetch(url + "/api/company/today").then((r) => r.json());
  assert.ok(today.needsYou.some((n) => n.kind === "business-freshness"));
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    "document.querySelector('.co-business-freshness').scrollIntoView()",
  );
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/freshness-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await click("Correct the brief");
  await field(
    "Products and services",
    "Owner corrected: studio workshops only",
  );
  await click("Save brief draft");
  await waitText("Brief draft saved.");
  await click("Approve brief for my team");
  await waitText("explicitly revoke the previous approved brief");
  await evaluate(
    "[...document.querySelectorAll('.co-business-recovery')].find(d=>d.querySelector('summary')?.textContent.includes('Replace or withdraw')).open=true",
  );
  await click("Revoke this approved brief");
  await waitText("The selected brief was revoked");
  await click("Approve brief for my team");
  await waitText("Business brief approved.");
  const final = (await snapshot()).data;
  const replacement = final.businessProfiles.find(
    (p) => p.state === "approved",
  );
  assert.notEqual(replacement.id, profile.id);
  assert.equal(
    replacement.fields.find((f) => f.key === "services").verification,
    "owner",
  );
  assert.equal(
    replacement.fields.find((f) => f.key === "goals").value,
    "Keep this unsaved owner correction",
  );
  assert.equal(
    (
      await fetch(
        url + "/api/company/business/freshness/" + replacement.id,
      ).then((r) => r.json())
    ).blocked,
    false,
  );
  assert.deepEqual(errors, []);
  assert.ok(final.budgets.every((b) => b.used === 0 && b.reserved === 0));
  await fs.writeFile(
    path.join(repo, "test-results/freshness-browser.json"),
    JSON.stringify(
      {
        changedEvidenceCompared: true,
        lostRefreshReconciledOnce: true,
        unsavedCorrectionPreserved: true,
        ownerAssessmentPausedBrief: true,
        todayNeedsReview: true,
        explicitReplacement: true,
        mobileOverflow: false,
        liveModelCalls: 0,
        externalRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS freshness: checked, compared, paused and corrected; the butler did not rewrite yesterday's facts.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/freshness-browser-host.log"),
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
