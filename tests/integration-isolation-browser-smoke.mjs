// Runs an installed headless Chrome against a disposable company-local host. No user browser profile or live provider.
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { spawn } from "node:child_process";
import assert from "node:assert/strict";
import { memberSessionJourney } from "./member-session-browser-journey.mjs";
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
      HIREZERO_BROWSER_CONNECTIONS: "1",
      HIREZERO_BROWSER_CONNECTION_MEMBERS: "1",
      HIREZERO_BROWSER_MEMBER_JOURNEY: "1",
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
    source: `(()=>{
    window.__connectionRequests=[];
    const original=window.fetch.bind(window);
    window.fetch=(input,...rest)=>{
      window.__connectionRequests.push(new URL(typeof input==='string'?input:input.url,location.href).pathname);
      return original(input,...rest);
    };
  })()`,
  });
  await send("Page.navigate", { url: url + "/" });
  await until(
    () => evaluate("document.body.innerText.includes('Company conversation')"),
    "company UI",
  );
  const idle = pageIdle(evaluate, until);

  const click = clicker(evaluate, until);
  const field = async (selector, value) => {
    await until(
      () =>
        evaluate("!!document.querySelector(" + JSON.stringify(selector) + ")"),
      "field " + selector,
    );
    await evaluate(
      "(()=>{const el=document.querySelector(" +
        JSON.stringify(selector) +
        ');const proto=el.tagName==="SELECT"?HTMLSelectElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,"value").set.call(el,' +
        JSON.stringify(value) +
        ');el.dispatchEvent(new Event(el.tagName==="SELECT"?"change":"input",{bubbles:true}));})()',
    );
    await delay(70);
  };
  const submit = async (selector) => {
    await idle();
    await evaluate(
      "document.querySelector(" +
        JSON.stringify(selector) +
        ").requestSubmit()",
    );
    await idle();
  };
  const get = (path, id = "") =>
    fetch(url + path, { headers: id ? { "X-HireZero-Company": id } : {} }).then(
      (r) => {
        assert.equal(r.status, 200);
        return r.json();
      },
    );
  const route = async (id) => {
    await send("Page.navigate", {
      url: url + "/?view=departments&section=integrations&company=" + id,
    });
    await until(
      () => evaluate('!!document.querySelector(".co-integrations")'),
      "integration page",
    );
    await idle();
  };
  const setup = 'details[aria-label="OpenRouter setup"]';
  await route("local-company");
  await until(
    () => evaluate("!!document.querySelector(" + JSON.stringify(setup) + ")"),
    "first company setup",
  );
  await evaluate(
    "document.querySelector(" + JSON.stringify(setup) + ").open=true",
  );
  await field(setup + ' input[aria-label="Model"]', "fictional/model-a");
  await submit(setup + " > form");
  await field(setup + " input[type=password]", "fictional-company-a-key");
  await submit(setup + " .co-setup-secret form");
  await until(
    async () =>
      (await get("/api/company/integrations/setup")).find(
        (s) => s.id === "openrouter",
      ).active,
    "first company connection active",
  );
  const created = await evaluate(
    'fetch("/api/companies",{method:"POST",headers:{"Content-Type":"application/json","X-Dev-Request":"1"},body:JSON.stringify({name:"Fictional connection studio"})}).then(async r=>{if(!r.ok)throw Error(await r.text());return r.json()})',
  );
  await route(created.id);
  await until(
    () => evaluate("!!document.querySelector(" + JSON.stringify(setup) + ")"),
    "second company setup",
  );
  assert.equal(
    await evaluate(
      "document.querySelector(" +
        JSON.stringify(setup + ' input[aria-label="Model"]') +
        ").value",
    ),
    "",
    "Second company starts without the first company model",
  );
  const secondSetups = await get("/api/company/integrations/setup", created.id);
  assert.equal(
    secondSetups.find((s) => s.id === "openrouter").secrets[0].origin,
    "missing",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll(" +
        JSON.stringify('[aria-label="GitHub sign-in"] button') +
        ")].every(b=>b.disabled)",
    ),
    true,
    "Machine GitHub sign-in cannot be shared",
  );
  assert.equal(
    secondSetups.some((s) => s.id === "agent-apps" || s.id === "azure-devops"),
    false,
  );

  const google =
    'details[aria-label="Google Analytics and Search Console setup"]';
  await evaluate(
    "document.querySelector(" + JSON.stringify(google) + ").open=true",
  );
  await field(
    google + ' input[aria-label="OAuth client ID"]',
    "fictional.apps.googleusercontent.com",
  );
  await submit(google + " > form");
  await field(
    google + ' input[aria-label="Paste OAuth client secret"]',
    "fictional-google-client",
  );
  await submit(
    google + ' .co-setup-secret[aria-label="OAuth client secret"] form',
  );
  await until(
    () =>
      evaluate(
        '[...document.querySelectorAll("button")].some(b=>b.textContent==="Sign in with Google")',
      ),
    "sign-in available",
  );
  // Capture the authorization URL instead of opening any external website. The real callback exchanges a code
  // with the compiled fake provider, and uses the real encrypted company credential store.
  await evaluate(
    'window.open=()=>{window.__consentWindow={location:{href:""},close(){}};return window.__consentWindow}',
  );
  await click("Sign in with Google");
  const authorization = await until(
    () => evaluate("window.__consentWindow?.location.href"),
    "authorization URL",
  );
  const state = new URL(authorization).searchParams.get("state");
  assert.ok(state);
  await send("Page.navigate", {
    url:
      url +
      "/oauth/google/callback?state=" +
      encodeURIComponent(state) +
      "&code=fictional-code",
  });
  await until(
    () =>
      evaluate(
        'new URLSearchParams(location.search).get("google")==="connected"',
      ),
    "callback receipt",
  );
  assert.equal(
    await evaluate('new URLSearchParams(location.search).get("company")'),
    created.id,
    "Callback keeps the initiating company",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector(" +
          JSON.stringify('[aria-label="Google sign-in"] select') +
          ")",
      ),
    "Google properties",
  );
  await idle();
  assert.equal(
    (await get("/api/connections/google")).signedIn,
    false,
    "Original company did not receive the Google token",
  );
  assert.equal(
    (await get("/api/connections/google", created.id)).signedIn,
    true,
  );
  await field('[aria-label="Google sign-in"] select', "123456");
  await submit('[aria-label="Google sign-in"] form');
  await until(
    async () =>
      (await get("/api/company/integrations/setup", created.id)).find(
        (s) => s.id === "google-analytics",
      ).active,
    "selected Google property active",
  );
  await click("Sign out of Google");
  await until(
    () => evaluate('!new URLSearchParams(location.search).has("google")'),
    "old connection receipt cleared after sign-out",
  );
  await until(
    () =>
      evaluate(
        'document.body.innerText.includes("Google sign-in removed for this company.")',
      ),
    "visible sign-out confirmation",
  );
  assert.equal(
    (await get("/api/connections/google", created.id)).signedIn,
    false,
  );
  assert.equal(
    (await get("/api/company/integrations/setup")).find(
      (s) => s.id === "openrouter",
    ).active,
    true,
    "Other company remains connected",
  );
  await route(created.id);
  await until(
    () =>
      evaluate(
        '[...document.querySelectorAll("button")].some(b=>b.textContent==="Sign in with Google")',
      ),
    "sign-out persists across reload",
  );
  assert.equal(
    (await get("/api/company/integrations/setup", created.id))
      .find((s) => s.id === "google-analytics")
      .fields.find((f) => f.id === "ga4Property").value,
    "123456",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await delay(150);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth"),
    true,
    "Mobile integrations fit",
  );
  await evaluate(
    "document.querySelector(" +
      JSON.stringify('[aria-label="Google sign-in"]') +
      ').scrollIntoView({block:"center"})',
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  const screenshot = await send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  await fs.writeFile(
    path.join(repo, "test-results/integration-isolation-mobile.png"),
    Buffer.from(screenshot.data, "base64"),
  );

  // Actual member roles from the disposable server: non-owners should see status without opening private setup.
  const ownerEndpoints = [
    "/api/connections/google",
    "/api/connections/microsoft",
    "/api/company/integrations/setup",
    "/api/company/github/sign-in",
    "/api/company/model-connection",
  ];
  for (const role of ["viewer", "approver"]) {
    const changed = await fetch(url + "/__fixture/connection-role/" + role, {
      method: "POST",
      headers: {
        Origin: url,
        "X-Dev-Request": "1",
        "Content-Type": "application/json",
      },
      body: "{}",
    });
    assert.equal(changed.status, 200);
    const request = await changed.json();
    await route("local-company");
    assert.equal((await get("/api/session")).role, role);
    assert.equal(
      await evaluate(
        "document.querySelector('.co-owner-label').textContent.toLowerCase()",
      ),
      role,
      "The sidebar names the current membership role",
    );
    assert.ok(
      await evaluate(
        "document.querySelector('.co-integrations').innerText.includes('Ask a company owner')",
      ),
    );
    assert.ok(
      await evaluate(
        "document.querySelector('.co-integrations').innerText.includes('OpenRouter: active')",
      ),
    );
    assert.ok(
      await evaluate(
        "document.querySelector('.co-integrations').innerText.includes('Review the approved advertising connection.')",
      ),
    );
    assert.equal(
      await evaluate(
        "document.querySelectorAll('.co-integrations input,.co-integrations button,.co-integrations form').length",
      ),
      0,
    );
    await delay(3250); // Cross the former Google polling interval as well as the initial load.
    const privateReads = (await evaluate("window.__connectionRequests")).filter(
      (path) => ownerEndpoints.some((endpoint) => path.startsWith(endpoint)),
    );
    assert.deepEqual(
      privateReads,
      [],
      role + " does not request private setup or poll sign-in",
    );
    assert.equal(
      await evaluate(
        "document.querySelectorAll('.co-integrations [role=alert]').length",
      ),
      0,
    );
    assert.equal(
      await evaluate("document.documentElement.scrollWidth<=innerWidth"),
      true,
    );
    // UI visibility is not authorization: direct calls retain the server's owner checks.
    for (const endpoint of ownerEndpoints) {
      assert.equal(
        (await fetch(url + endpoint)).status,
        403,
        role + " cannot read " + endpoint,
      );
    }
    const refused = await fetch(url + "/api/company/commands", {
      method: "POST",
      headers: {
        Origin: url,
        "X-Dev-Request": "1",
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        requestId: crypto.randomUUID(),
        action: "connection.dismiss",
        id: request.id,
        expectedVersion: 1,
      }),
    });
    assert.equal(refused.status, 403);
    assert.equal(
      await evaluate("document.querySelector('.co-integrations details').open"),
      false,
      "Unused setup choices start collapsed",
    );
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
      await delay(150);
      assert.equal(
        await evaluate("document.documentElement.scrollWidth<=innerWidth"),
        true,
        role + " " + size + " fits",
      );
      const memberShot = await send("Page.captureScreenshot", {
        format: "png",
        captureBeyondViewport: false,
      });
      await fs.writeFile(
        path.join(
          repo,
          "test-results/integration-" + role + "-" + size + ".png",
        ),
        Buffer.from(memberShot.data, "base64"),
      );
    }
    await send("Page.navigate", {
      url:
        url +
        "/?company=local-company&view=employee&employee=" +
        request.employeeId +
        "&thread=" +
        request.threadId,
    });
    await until(
      () => evaluate("!!document.querySelector('.co-connect')"),
      role + " conversation connection notice",
    );
    await idle();
    assert.ok(
      await evaluate(
        "document.querySelector('.co-connect').innerText.includes('Ask a company owner')",
      ),
    );
    assert.equal(
      await evaluate(
        "document.querySelectorAll('.co-connect button,.co-connect input,.co-connect form').length",
      ),
      0,
    );
    await delay(3250);
    assert.deepEqual(
      (await evaluate("window.__connectionRequests")).filter((path) =>
        ownerEndpoints.some((endpoint) => path.startsWith(endpoint)),
      ),
      [],
      role + " conversation never loads owner setup",
    );
    const canWork = role === "approver";
    assert.equal(
      await evaluate(
        "!!document.querySelector('.co-conversation-composer form')",
      ),
      canWork,
    );
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('button')].some(button=>button.textContent.trim()==='New conversation')",
      ),
      canWork,
    );
    await evaluate("document.querySelector('.co-private-context').open=true");
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('.co-private-context button')].some(button=>button.textContent.trim()==='Save private notes')",
      ),
      canWork,
    );
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('.co-private-context button')].some(button=>button.textContent.trim()==='Update employee'||button.textContent.trim()==='Retire employee')",
      ),
      false,
    );
    const visit = async (query, selector) => {
      await send("Page.navigate", {
        url: url + "/?company=local-company&" + query,
      });
      await until(
        () =>
          evaluate(
            "!!document.querySelector(" + JSON.stringify(selector) + ")",
          ),
        role + " " + query,
      );
      await idle();
      assert.equal(
        await evaluate("document.documentElement.scrollWidth<=innerWidth"),
        true,
        role + " " + query + " fits",
      );
    };
    await visit("view=home&home=overview", ".co-home");
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('button')].some(button=>/Hire (your first|an) employee|Continue onboarding/.test(button.textContent))",
      ),
      false,
    );
    await visit("view=employees", ".co-team");
    assert.equal(
      await evaluate("!!document.querySelector('.co-onboarding')"),
      false,
    );
    assert.equal(
      await evaluate("document.querySelectorAll('main form').length"),
      0,
    );
    await visit("view=departments&section=departments", ".co-company-facts");
    await click("Fictional Paper StudioApproved knowledge");
    assert.equal(
      await evaluate(
        "document.querySelectorAll('.co-context-editor textarea,.co-context-editor form').length",
      ),
      0,
    );
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('.co-context-editor button')].some(button=>button.textContent.trim()==='Revoke')",
      ),
      canWork,
    );
    await visit("view=departments&section=budgets", ".co-budgets");
    assert.equal(
      await evaluate(
        "document.querySelectorAll('.co-budgets button,.co-budgets input').length",
      ),
      0,
    );
    await visit("view=plans", "[data-plan-id]");
    assert.equal(
      await evaluate(
        "!!document.querySelector('[aria-label=\"Propose company plan\"]')",
      ),
      canWork,
    );
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('button')].some(button=>button.textContent.trim()==='Approve exact plan')",
      ),
      canWork,
    );
    assert.ok(
      await evaluate(
        "document.body.innerText.includes('Fictional member planning record')",
      ),
    );
    await visit("view=meetings", ".co-columns");
    assert.equal(
      await evaluate(
        "!!document.querySelector('[aria-label=\"Prepare company meeting\"]')",
      ),
      canWork,
    );
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('button')].some(button=>button.textContent.trim()==='Approve discussion and start')",
      ),
      canWork,
    );
    assert.ok(
      await evaluate(
        "document.body.innerText.includes('Fictional member meeting')",
      ),
    );
    await visit("view=departments&section=today", ".co-today");
    assert.equal(
      await evaluate("!!document.querySelector('.co-today select')"),
      canWork,
    );
    await visit(
      "view=employee&mode=work&employee=" +
        request.employeeId +
        "&thread=" +
        request.threadId +
        "&work=" +
        request.review.taskId,
      ".co-work-window",
    );
    assert.ok(
      await evaluate(
        "document.querySelector('.co-work-window').innerText.includes('Fictional member review result')",
      ),
    );
    assert.equal(
      await evaluate(
        "[...document.querySelectorAll('.co-work-window button')].some(button=>button.textContent.trim()==='Approve result')",
      ),
      canWork,
    );
    if (canWork) {
      await click("Approve result");
      await click("Complete after review");
      assert.equal(
        (await get("/api/company")).data.tasks.find(
          (task) => task.id === request.review.taskId,
        ).state,
        "completed",
      );
    } else {
      assert.equal(
        await evaluate(
          "document.querySelector('.co-work-window').innerText.includes('your review')",
        ),
        false,
        "A viewer is not asked to review work they cannot approve",
      );
      assert.equal(
        await evaluate(
          "document.querySelectorAll('.co-work-window form,.co-work-window textarea').length",
        ),
        0,
      );
      assert.equal(
        await evaluate(
          "[...document.querySelectorAll('button[aria-label]')].some(button=>button.getAttribute('aria-label').startsWith('Cancel '))",
        ),
        false,
      );
    }
    for (const action of [
      "employee.hire",
      "department.create",
      "knowledge.save",
      "budget.configure",
      ...(canWork
        ? []
        : [
            "artifact.review",
            "task.complete",
            "message.send",
            "plan.approve",
            "meeting.start",
          ]),
    ]) {
      const denied = await fetch(url + "/api/company/commands", {
        method: "POST",
        headers: {
          Origin: url,
          "X-Dev-Request": "1",
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ requestId: crypto.randomUUID(), action }),
      });
      assert.equal(
        denied.status,
        403,
        role + " is refused " + action + " before input processing",
      );
    }
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
      await delay(150);
      assert.equal(
        await evaluate("document.documentElement.scrollWidth<=innerWidth"),
        true,
      );
      if (size === "desktop") {
        assert.equal(
          await evaluate(
            "(()=>{const panel=document.querySelector('.fe-cockpit-body');return panel.scrollWidth<=panel.clientWidth;})()",
          ),
          true,
          "Long connection titles fit inside the cockpit without horizontal scrolling",
        );
        assert.equal(
          await evaluate(
            "document.querySelector('.co-needs h3').textContent.includes('Needs you')",
          ),
          canWork,
        );
      }
      const shot = await send("Page.captureScreenshot", {
        format: "png",
        captureBeyondViewport: false,
      });
      await fs.writeFile(
        path.join(
          repo,
          "test-results/member-work-" + role + "-" + size + ".png",
        ),
        Buffer.from(shot.data, "base64"),
      );
    }
  }
  await fetch(url + "/__fixture/connection-role/owner", {
    method: "POST",
    headers: {
      Origin: url,
      "X-Dev-Request": "1",
      "Content-Type": "application/json",
    },
    body: "{}",
  }).then((response) => assert.equal(response.status, 200));
  await route("local-company");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('details[aria-label=\"OpenRouter setup\"]')",
      ),
    "owner setup restored",
  );
  assert.ok(
    await evaluate(
      "!!document.querySelector('button[aria-label=\"Dismiss connecting Google Ads\"]')",
    ),
  );
  assert.equal(
    (await get("/api/company")).data.connections.find(
      (request) => request.integrationId === "google-ads",
    ).state,
    "open",
  );
  const sessionJourney = await memberSessionJourney({
    send,
    evaluate,
    click,
    until,
    idle,
    url,
    repo,
    secondaryCompany: created.id,
  });
  await fs.writeFile(
    path.join(repo, "test-results/integration-isolation-browser.json"),
    JSON.stringify(
      {
        provider: "compiled fake Google",
        liveModelCalls: 0,
        externalRequests: 0,
        companySettingsIsolated: true,
        callbackCompanyPreserved: true,
        signOutPersisted: true,
        otherCompanyUnaffected: true,
        mobileFits: true,
        viewerAndApproverReadOnly: true,
        noOwnerSetupRequestsOrPolling: true,
        serverOwnerChecksPreserved: true,
        ownerSetupRestored: true,
        conversationRequestsReadOnly: true,
        memberSetupReadOnly: true,
        viewerWorkAndGovernanceReadOnly: true,
        approverCompletedReviewedWork: true,
        ...sessionJourney,
      },
      null,
      2,
    ),
  );
  assert.equal(errors.length, 0, "No uncaught UI exceptions");
  console.log(
    "PASS integration isolation: separate credentials, consent callback, sign-out, member status and owner setup. Each keyring has a proper keeper.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/integration-isolation-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  const stop = async (process) => {
    if (!process || process.exitCode !== null || process.signalCode !== null)
      return;
    const stopped = new Promise((resolve, reject) => {
      const timer = setTimeout(
        () =>
          reject(
            new Error(
              "Fixture process did not exit; keeping its temporary files",
            ),
          ),
        10000,
      );
      process.once("exit", () => {
        clearTimeout(timer);
        resolve();
      });
    });
    process.kill();
    await stopped;
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
