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
      HIREZERO_BROWSER_PUBLIC_REVIEW: "1",
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
  const click = clicker(evaluate, until);
  const field = async (scope, label, value) => {
    await evaluate(
      `(()=>{const label=[...document.querySelectorAll(${JSON.stringify(scope + " label")})].find(l=>[...l.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join('').trim()===${JSON.stringify(label)});if(!label)throw Error('Missing field: '+${JSON.stringify(label)});const el=label.querySelector('input,textarea,select');const proto=el.tagName==='SELECT'?HTMLSelectElement.prototype:el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));})()`,
    );
    await delay(60);
  };
  const check = async (label) =>
    evaluate(
      `(()=>{const l=[...document.querySelectorAll('label')].find(l=>l.textContent.includes(${JSON.stringify(label)}));if(!l)throw Error('Missing checkbox');l.querySelector('input[type=checkbox]').click();})()`,
    );
  const detail = (id) =>
    fetch(url + "/api/company/tasks/" + id).then((r) => r.json());
  const compute = () =>
    fetch(url + "/api/company/compute").then((r) => r.json());
  const state = () => fetch(url + "/__fixture/browser").then((r) => r.json());
  const dashboard = () => fetch(url + "/api/company").then((r) => r.json());
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-browser')?.innerText.includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const snapshot = await dashboard();
  const seeded = await state();
  const task = snapshot.data.tasks.find((t) => t.id === seeded.taskId);
  const workUrl =
    url +
    `/?view=employee&employee=${task.employeeId}&thread=${task.threadId}&work=${task.id}&mode=work`;
  const fixtureMode = (mode) =>
    evaluate(
      `fetch('/__fixture/browser/${mode}',{method:'POST',headers:{'X-Dev-Request':'1','Content-Type':'application/json'},body:'{}'})`,
    );
  const openDetails = (text) =>
    evaluate(
      `[...document.querySelectorAll('.co-browser details')].find(d=>d.querySelector('summary')?.textContent===${JSON.stringify(text)}).open=true`,
    );
  async function work() {
    await send("Page.navigate", { url: workUrl });
    await until(
      () =>
        evaluate(
          "!!document.querySelector('.co-work-window .co-browser .co-sandbox-allowance')",
        ),
      "website review tools loaded",
    );
    await evaluate(
      "document.querySelector('.co-work-window .co-browser').open=true",
    );
  }
  async function limits(paused) {
    await send("Page.navigate", {
      url: url + "/?view=departments&section=usage",
    });
    await until(
      () => evaluate("!!document.querySelector('.co-compute-settings')"),
      "shared compute controls",
    );
    await evaluate("document.querySelector('.co-compute-settings').open=true");
    await field(".co-compute", "Compute jobs per UTC month", "2");
    await field(".co-compute", "Allocated compute seconds per UTC month", "60");
    const current = await evaluate(
      "document.querySelector('.co-compute-settings input[type=checkbox]').checked",
    );
    if (current !== paused) await check("Pause compute execution");
    await check("I reviewed these compute limits.");
    await click("Save compute limits");
    await until(
      () =>
        evaluate(
          "document.querySelector('.co-compute').innerText.includes('Compute limits saved.')",
        ),
      "limits saved",
    );
  }
  async function setStep(index, action, value) {
    await evaluate(
      `(()=>{const row=document.querySelectorAll('.co-browser-step')[${index}];const el=row.querySelector('select');Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(el,${JSON.stringify(action)});el.dispatchEvent(new Event('change',{bubbles:true}));})()`,
    );
    await delay(60);
    await evaluate(
      `(()=>{const el=document.querySelectorAll('.co-browser-step')[${index}].querySelector('input');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(el,${JSON.stringify(value)});el.dispatchEvent(new Event('input',{bubbles:true}));})()`,
    );
    await delay(60);
  }
  async function prepare(name) {
    await openDetails("Prepare a website review");
    await field(".co-browser", "Review name", name);
    await field(
      ".co-browser",
      "Website address",
      "https://paper.example.invalid/services",
    );
    await openDetails("Advanced steps and limits");
    await field(".co-browser", "Maximum browsing seconds", "5");
    await setStep(1, "click", "#services");
    await click("Add step");
    await setStep(2, "wait-for", "#details");
    await click("Add step");
    await setStep(3, "read", "main");
    await click("Prepare website review");
    await until(
      () =>
        evaluate(
          "document.activeElement?.classList.contains('co-browser-job')",
        ),
      "prepared browser review receives focus",
    );
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('button')].find(b=>b.textContent==='Approve website review once')?.disabled",
      ),
      true,
    );
  }
  await limits(false);
  await work();
  await fixtureMode("partial");
  await prepare("Review fictional services");
  assert.equal((await state()).executions, 0);
  const proposal = (await detail(task.id)).browsers[0];
  assert.deepEqual(proposal.request.origins, ["https://paper.example.invalid"]);
  assert.deepEqual(
    proposal.request.steps.map((s) => s.action),
    ["navigate", "click", "wait-for", "read"],
  );
  assert.equal(proposal.reservedSeconds, 17);
  assert.equal(proposal.request.screenshots, true);
  assert.equal((await compute()).usage.heldSeconds, 0);
  await waitText("17 seconds will be reserved if approved");
  await waitText("Websites and steps to approve");
  await check("I reviewed these websites, steps, screenshots and limits.");
  await evaluate(
    "window.originalBrowserFetch=window.fetch;window.loseBrowserRun=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseBrowserRun&&body.action==='browser.run'){window.loseBrowserRun=false;await window.originalBrowserFetch(input,init);throw new TypeError('Fictional lost browser approval receipt');}return window.originalBrowserFetch(input,init);};",
  );
  await click("Approve website review once");
  await waitText("Resource release is unconfirmed.");
  await waitText("Fictional website reviews and stationery.");
  await waitText("Partial evidence:");
  assert.equal((await state()).executions, 1);
  assert.equal((await compute()).usage.heldSeconds, 17);
  await openDetails("View saved screenshot");
  await until(
    () => evaluate("document.querySelector('.co-browser img')?.naturalWidth>0"),
    "saved JPEG rendered",
  );
  await openDetails("Links found on this page (2)");
  assert.equal(
    await evaluate(
      "document.querySelectorAll('.co-browser a[href^=\"javascript:\"]').length",
    ),
    0,
  );
  await send("Browser.setDownloadBehavior", {
    behavior: "allow",
    downloadPath: path.join(temporary, "downloads"),
  });
  await click("Download page text");
  const textDownload = await until(
    async () =>
      fs.readFile(
        path.join(temporary, "downloads/website-capture-4.txt"),
        "utf8",
      ),
    "plain-text capture download",
  );
  assert.ok(textDownload.includes("https://paper.example.invalid/services"));
  assert.ok(textDownload.includes("Partial evidence:"));
  assert.ok(textDownload.endsWith("Fictional website reviews and stationery."));
  await click("Download website receipt");
  const receipt = await until(
    async () =>
      JSON.parse(
        await fs.readFile(
          path.join(temporary, `downloads/website-review-${proposal.id}.json`),
          "utf8",
        ),
      ),
    "website receipt download",
  );
  assert.equal(
    receipt.execution.result.captures[0].text,
    "Fictional website reviews and stationery.",
  );
  assert.equal(receipt.execution.result.partial, true);
  await limits(true);
  await fixtureMode("disable");
  await work();
  await waitText("The operator has paused new website reviews.");
  await waitText("Fictional website reviews and stationery.");
  await click("Check browser resources and recover");
  await waitText("Temporary browser resources released.");
  assert.equal((await state()).executions, 1);
  assert.equal((await compute()).usage.heldSeconds, 0);
  await fixtureMode("enable");
  await fixtureMode("wait");
  await limits(false);
  await work();
  await prepare("Cancel fictional visit");
  await check("I reviewed these websites, steps, screenshots and limits.");
  await click("Approve website review once");
  await until(
    async () => (await state()).executions === 2,
    "second browser dispatch",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-work-window header small')?.textContent.includes('Working')",
      ),
    "parent task shows browser progress",
  );
  await click("Stop website review");
  await waitText("Captures withheld");
  await until(
    async () => (await detail(task.id)).browsers.every((j) => j.settledAt),
    "revoked visit cleanup",
  );
  const jobs = (await detail(task.id)).browsers;
  assert.equal(jobs.filter((j) => j.state === "withheld").length, 1);
  assert.equal(jobs.find((j) => j.state === "withheld").execution.result, null);
  assert.equal((await compute()).usage.allocatedSeconds, 34);
  await fixtureMode("disable");
  await work();
  await waitText("The operator has paused new website reviews.");
  await openDetails("Use this text as a task result");
  await field(".co-browser-capture", "Selected page text", "Changed page text");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Prepare page result').disabled",
    ),
    true,
  );
  await field(
    ".co-browser-capture",
    "Selected page text",
    "Fictional website reviews and stationery.",
  );
  await evaluate(
    "window.originalCaptureFetch=window.fetch;window.loseCaptureReceipt=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseCaptureReceipt&&body.action==='browser.output.prepare'){window.loseCaptureReceipt=false;await window.originalCaptureFetch(input,init);throw new TypeError('Fictional lost capture receipt');}return window.originalCaptureFetch(input,init);};",
  );
  await click("Prepare page result");
  await until(
    () => evaluate("document.activeElement?.classList.contains('co-artifact')"),
    "focus on saved page result",
  );
  const reviewed = await dashboard();
  assert.equal(reviewed.data.artifacts.length, 1);
  assert.equal(reviewed.data.artifacts[0].browserOrigin.jobId, proposal.id);
  assert.equal(reviewed.data.artifacts[0].browserOrigin.partial, true);
  assert.equal(reviewed.data.artifacts[0].state, "needs_review");
  await click("Approve result");
  await click("Complete after review");
  assert.equal(
    (await dashboard()).data.tasks.find((t) => t.id === task.id).state,
    "completed",
  );
  assert.equal((await state()).executions, 2);
  await work();
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    "document.querySelector('.co-browser-capture').scrollIntoView({block:'start'})",
  );
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/browser-review-mobile.png"),
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
    "document.querySelector('.co-browser-job').scrollIntoView({block:'start'})",
  );
  await delay(200);
  await fs.writeFile(
    path.join(repo, "test-results/browser-review-desktop.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await openDetails("Delete saved captures");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('button')].find(b=>b.textContent==='Delete these captures').disabled",
    ),
    true,
  );
  await check(
    "I understand which records remain and want to delete these captures.",
  );
  await evaluate(
    "window.originalDeleteFetch=window.fetch;window.loseCaptureDelete=true;window.fetch=async(input,init)=>{const body=init?.body?JSON.parse(init.body):{};if(window.loseCaptureDelete&&body.action==='browser.captures.delete'){window.loseCaptureDelete=false;await window.originalDeleteFetch(input,init);throw new TypeError('Fictional lost capture deletion reply');}return window.originalDeleteFetch(input,init);};",
  );
  await click("Delete these captures");
  await waitText("Saved captures deleted");
  const deleted = (await detail(task.id)).browsers.find(
    (j) => j.id === proposal.id,
  );
  assert.equal(deleted.execution.result, null);
  assert.equal(deleted.captureDeletion.artifacts.length, 1);
  assert.equal((await state()).executions, 2);
  await work();
  await waitText("Saved captures deleted");
  assert.equal(
    await evaluate("document.querySelectorAll('.co-browser-capture').length"),
    0,
  );
  await click("Review saved task result");
  await until(
    () => evaluate("document.activeElement?.classList.contains('co-artifact')"),
    "retained task result focus",
  );
  assert.equal((await dashboard()).data.artifacts[0].state, "approved");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    "document.querySelector('.co-browser-job').scrollIntoView({block:'start'})",
  );
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/browser-retention-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await limits(false);
  await evaluate("document.querySelector('.co-browser-retention').open=true");
  await field(".co-browser-retention", "Keep browser captures", "30");
  await check(
    "I understand which records remain and approve this capture retention policy.",
  );
  await click("Save capture retention");
  await until(
    async () => (await compute()).browserRetention.captureDays === 30,
    "capture retention saved",
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-browser-retention').innerText.includes('Browser capture retention saved.')",
      ),
    "retention receipt displayed",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await evaluate(
    "document.querySelector('.co-browser-retention').scrollIntoView({block:'start'})",
  );
  await delay(200);
  await fs.writeFile(
    path.join(repo, "test-results/browser-retention-settings-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  await work();
  await waitText("Saved captures deleted");
  assert.equal((await state()).executions, 2);
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/browser-review-browser.json"),
    JSON.stringify(
      {
        exactItineraryApproval: true,
        advancedSteps: 4,
        screenshotRendered: true,
        plainTextDownload: true,
        parentTaskShowsProgress: true,
        unsafeLinksSuppressed: true,
        partialEvidenceShown: true,
        lostApprovalReceiptExecutedOnce: true,
        failedReleaseHeldSeconds: 17,
        recoveryWhilePausedAndDisabled: true,
        canceledVisitWithheldCaptures: true,
        capturedTextReviewWhileDisabled: true,
        lostOutputReplyCreatedOneArtifact: true,
        completedAfterReview: true,
        lostDeletionReplyRecovered: true,
        captureDeletionSurvivesReload: true,
        approvedExcerptRemainsAfterDeletion: true,
        ownerCaptureRetentionSaved: true,
        allocatedSeconds: 34,
        mobileOverflow: false,
        executorVisitsRealWebsites: false,
        realContainerExecutions: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS website review journey: exact itinerary, dated captures and one careful visit; the butler saved the evidence.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/browser-review-host.log"),
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
