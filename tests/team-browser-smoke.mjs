// Goal-based suggestions, role-owned setup and reviewed first work through the local UI; fictional providers only.
// Failed or unconfirmed saves keep the owner's work; the butler does not file a second copy behind their back.
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
      HIREZERO_BROWSER_TEAM: "1",
    },
    stdio: ["ignore", "pipe", "pipe"],
  },
);
let hostLog = "";
host.stdout.on("data", (b) => (hostLog += b));
host.stderr.on("data", (b) => (hostLog += b));
let browser, ws;
const until = waiter();
const dashboard = () => fetch(url + "/api/company").then((r) => r.json());
async function command(action, fields) {
  const response = await fetch(url + "/api/company/commands", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Origin: url,
      "X-Dev-Request": "1",
    },
    body: JSON.stringify({ requestId: crypto.randomUUID(), action, ...fields }),
  });
  assert.ok(
    response.ok,
    "Fictional fixture command " +
      action +
      " succeeds (HTTP " +
      response.status +
      ")",
  );
  return response.json();
}
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "this test host owns the listener",
  );
  await until(
    async () => (await fetch(url + "/health")).ok,
    "fresh host health",
  );
  assert.equal(
    (await fetch(url + "/health").then((r) => r.json())).modelExecution,
    false,
    "No model provider",
  );
  const goal = "Improve website accessibility";
  let identity = await command("business.identify", {
    expectedVersion: 0,
    body: JSON.stringify({
      name: "Fictional team studio",
      website: null,
      goal,
      location: "Fictional Valley",
      industry: "Paper crafts",
    }),
  });
  identity = await command("business.confirm", {
    expectedVersion: identity.version,
    digest: identity.digest,
  });
  const fields = (goal, verification = "owner") =>
    [
      "business",
      "services",
      "customers",
      "voice",
      "market",
      "competitors",
      "opportunities",
      "goals",
      "boundaries",
    ].map((key) => ({
      key,
      value:
        key === "business"
          ? "Fictional team studio"
          : key === "goals"
            ? goal
            : "Unknown",
      verification:
        key === "business"
          ? "owner"
          : key === "goals"
            ? verification
            : "unknown",
      citations: [],
    }));
  let profile = await command("business.profile.save", {
    body: JSON.stringify({
      businessVersion: identity.version,
      fields: fields(goal),
    }),
  });
  const reviewProfile = (decision) =>
    command("business.profile.review", {
      id: profile.id,
      expectedVersion: profile.version,
      digest: profile.digest,
      decision,
    });
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
  const target = (
    await fetch(`http://127.0.0.1:${port}/json/list`).then((r) => r.json())
  ).find((t) => t.type === "page");
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
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.addScriptToEvaluateOnNewDocument", { source: fetchTracker });
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("!!document.querySelector('.co-home')"),
    "company home",
  );
  const click = clicker(evaluate, until),
    idle = pageIdle(evaluate, until);
  const text = (phrase) =>
    until(
      () =>
        evaluate(`document.body.innerText.includes(${JSON.stringify(phrase)})`),
      phrase,
    );
  const recommendations = () =>
    evaluate(
      "[...document.querySelectorAll('[data-role-suggestion]')].map(e=>e.dataset.roleSuggestion)",
    );
  const openBrief = async () => {
    await click("Company");
    await click("Business brief");
    await idle();
  };
  const reload = async () => {
    await send("Page.reload");
    await until(
      () => evaluate("!!document.querySelector('.co-business')"),
      "business brief reload",
    );
    await idle();
  };
  await openBrief();
  assert.deepEqual(
    await recommendations(),
    [],
    "Draft briefs do not recommend employees",
  );
  profile = await reviewProfile("approve");
  await reload();
  await text("Set up a Developer employee");
  assert.deepEqual(
    await recommendations(),
    ["developer"],
    "Technical goal leads to the relevant role rather than a default Marketing hire",
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  const capture = async (name) =>
    fs.writeFile(
      path.join(repo, "test-results/" + name + ".png"),
      Buffer.from(
        (await send("Page.captureScreenshot", { format: "png" })).data,
        "base64",
      ),
    );
  await capture("team-suggestions-desktop");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(
    "document.querySelector('.co-business-next').scrollIntoView({block:'start'})",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await capture("team-suggestions-mobile");
  const hireAndComplete = async (
    roleId,
    roleName,
    heading,
    expected,
    goalText,
  ) => {
    const before = (await dashboard()).data.employees.length;
    await click("Set up a " + roleName + " employee");
    await until(
      () =>
        evaluate(
          `document.querySelector('.co-onboarding select')?.value===${JSON.stringify(roleId)}`,
        ),
      "requested role selected",
    );
    assert.ok(
      (
        await evaluate(
          "[...document.querySelectorAll('.co-onboarding label')].find(l=>l.childNodes[0]?.textContent.trim()==='Responsibility').querySelector('textarea').value",
        )
      ).includes(goalText),
    );
    await click("Create employee & continue");
    await text("Shared department context");
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('.co-onboarding form textarea')].every(t=>t.value.trim().length>0)",
      ),
      true,
    );
    await click("Save context for review");
    await text("Review the saved text below");
    await evaluate(
      "[...document.querySelectorAll('.co-onboarding label')].find(l=>l.textContent.includes('I reviewed these facts')).querySelector('input').click()",
    );
    await click("Approve shared context");
    await text(heading);
    assert.doesNotMatch(
      await evaluate(
        "document.querySelector('.co-first-assignment').textContent",
      ),
      /sample copy|marketing plan/i,
    );
    assert.equal((await dashboard()).data.employees.length, before + 1);
    const jobs = (await fetch(url + "/api/company/usage").then((r) => r.json()))
      .current.jobs;
    await click("Prepare my first plan");
    await click("Review task and context");
    await until(
      () => evaluate("!!document.querySelector('.co-job-card .co-packet')"),
      "first assignment context review",
    );
    assert.ok(
      await evaluate(
        "document.querySelector('.co-job-card .co-packet').textContent.includes('at most 500 words')",
      ),
    );
    assert.equal(
      (await fetch(url + "/api/company/usage").then((r) => r.json())).current
        .jobs,
      jobs,
      "Preparation does not dispatch",
    );
    await evaluate(
      "document.querySelector('.co-job-card .co-check input').click()",
    );
    await click("Authorize and run one job");
    await text(expected);
    await click("Read result");
    await click("Approve result");
    await click("Complete after review");
    await until(
      async () =>
        (await dashboard()).data.tasks.filter((t) => t.state === "completed")
          .length ===
        before + 1,
      "reviewed first assignment complete",
    );
    await idle();
    assert.equal(
      (await dashboard()).data.plans.length,
      0,
      "First advice creates no delegation authority",
    );
  };
  await hireAndComplete(
    "developer",
    "Developer",
    "Your first technical plan",
    "Fictional technical plan",
    goal,
  );
  await openBrief();
  await text("Already on your team.");
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('[data-role-suggestion=developer] button')].some(b=>b.textContent==='Set up a Developer employee')",
    ),
    false,
  );
  const developer = (await dashboard()).data.employees.find(
    (e) => e.roleId === "developer",
  );
  await click("Open " + developer.name);
  await until(
    () => evaluate(`location.search.includes(${JSON.stringify(developer.id)})`),
    "existing employee opened",
  );
  assert.equal(
    (await dashboard()).data.employees.length,
    1,
    "Using the suggestion does not create a duplicate",
  );
  profile = await reviewProfile("revoke");
  await openBrief();
  await reload();
  assert.deepEqual(
    await recommendations(),
    [],
    "Revocation removes recommendations and setup authority",
  );
  const coordinatedGoal = "Coordinate team priorities";
  profile = await command("business.profile.save", {
    body: JSON.stringify({
      businessVersion: identity.version,
      fields: fields(coordinatedGoal),
    }),
  });
  profile = await reviewProfile("approve");
  await reload();
  await text("Set up a CEO employee");
  assert.deepEqual(
    await recommendations(),
    ["ceo"],
    "Changed approved goal changes the suggestion",
  );
  await hireAndComplete(
    "ceo",
    "CEO",
    "Your first priority plan",
    "Fictional priority plan",
    coordinatedGoal,
  );
  profile = await reviewProfile("revoke");
  profile = await command("business.profile.save", {
    body: JSON.stringify({
      businessVersion: identity.version,
      fields: fields("Coordinate a website team", "unknown"),
    }),
  });
  profile = await reviewProfile("approve");
  await openBrief();
  await reload();
  await text("A starting option for a small business");
  assert.deepEqual(
    await recommendations(),
    ["marketing"],
    "Unknown goal text is not treated as an approved recommendation signal",
  );
  const state = (await dashboard()).data;
  assert.equal(state.employees.length, 2);
  assert.equal(state.tasks.filter((t) => t.state === "completed").length, 2);
  assert.equal(state.plans.length, 0);
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/team-browser.json"),
    JSON.stringify(
      {
        verifiedAt: new Date().toISOString(),
        assets: await evaluate(
          "[...document.querySelectorAll('script[src]')].map(s=>new URL(s.src).pathname)",
        ),
        approvedGoalSuggestions: true,
        unknownGoalExcluded: true,
        revocation: true,
        existingEmployeeReuse: true,
        developerFirstAssignment: true,
        ceoFirstAssignment: true,
        roleOwnedSetup: true,
        desktopAndMobile: true,
        liveModelCalls: 0,
        externalRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS team setup: the right specialist, approved context and two reviewed first plans; no extra employees slipped past the butler.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/team-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  browser?.kill();
  host.kill();
  await delay(400);
  // Delete only this script's verified temporary root after its own processes exit.
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
