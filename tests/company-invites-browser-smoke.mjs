// Real HTTPS cookies, APIs and UI with fictional login and an unavailable model. No live service or spend.
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { spawn } from "node:child_process";
import { once } from "node:events";
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
const temporary = await fs.mkdtemp(path.join(os.tmpdir(), "hirezero-team-ui-"));
const chrome =
  process.env.HIREZERO_TEST_CHROME ||
  path.join(
    process.env.ProgramFiles || "C:/Program Files",
    "Google/Chrome/Application/chrome.exe",
  );
const url = (await freeLocalUrl()).replace("http:", "https:");
const until = waiter();
const sockets = [],
  errors = [];
let host,
  browser,
  hostLog = "",
  allHostLog = "";
async function startHost(enabled) {
  hostLog = "";
  host = spawn(
    "dotnet",
    [
      path.join(
        repo,
        "tests/HireZero.Company.Tests/bin/Debug/net10.0/HireZero.Company.Tests.dll",
      ),
      "--team-browser-fixture",
      temporary,
      url,
      path.join(repo, "src/HireZero.Host/wwwroot"),
      enabled ? "1" : "0",
    ],
    {
      cwd: repo,
      windowsHide: true,
      env: { ...process.env, Logging__LogLevel__Default: "Warning" },
      stdio: ["ignore", "pipe", "pipe"],
    },
  );
  const record = (b) => {
    hostLog = (hostLog + b).slice(-50000);
    allHostLog = (allHostLog + b).slice(-200000);
  };
  host.stdout.on("data", record);
  host.stderr.on("data", record);
  await until(() => {
    if (host.exitCode !== null) throw Error(hostLog);
    return hostLog.includes("TEAM FIXTURE:");
  }, "fictional HTTPS fixture");
}
async function stop(process) {
  if (!process || process.exitCode !== null) return;
  const exited = once(process, "exit");
  process.kill();
  await Promise.race([
    exited,
    new Promise((_, reject) =>
      setTimeout(() => reject(Error("Fixture did not exit")), 10000).unref(),
    ),
  ]);
}
async function connect(target, browserControl = false) {
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  sockets.push(ws);
  await new Promise((resolve, reject) => {
    ws.addEventListener("open", resolve, { once: true });
    ws.addEventListener("error", reject, { once: true });
  });
  let sequence = 0;
  const pending = new Map();
  ws.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (message.id) {
      const item = pending.get(message.id);
      if (!item) return;
      clearTimeout(item.timer);
      pending.delete(message.id);
      message.error
        ? item.reject(Error(item.method + ": " + message.error.message))
        : item.resolve(message.result);
    }
    if (message.method === "Runtime.exceptionThrown")
      errors.push(message.params.exceptionDetails.text);
  });
  const send = (method, params = {}) =>
    new Promise((resolve, reject) => {
      const id = ++sequence;
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(Error("Debugger timeout: " + method));
      }, 10000);
      pending.set(id, { resolve, reject, timer, method });
      ws.send(JSON.stringify({ id, method, params }));
    });
  const evaluate = async (expression) => {
    const result = await send("Runtime.evaluate", {
      expression,
      returnByValue: true,
      awaitPromise: true,
    });
    if (result.exceptionDetails)
      throw Error(
        result.exceptionDetails.exception?.description ||
          result.exceptionDetails.text,
      );
    return result.result.value;
  };
  if (!browserControl) {
    await send("Runtime.enable");
    await send("Page.enable");
    await send("Page.addScriptToEvaluateOnNewDocument", {
      source: fetchTracker,
    });
  }
  const click = clicker(evaluate, until);
  const navigate = async (route) => {
    await send("Page.navigate", {
      url: route.startsWith("https:") ? route : url + route,
    });
    await until(
      () => evaluate('document.readyState === "complete"'),
      "page ready",
    );
  };
  const request = (route, method = "GET", body, company) =>
    evaluate(`fetch(${JSON.stringify(route)}, {
    method:${JSON.stringify(method)}, headers:{'Content-Type':'application/json','X-Dev-Request':'1',
      ${company ? `'X-HireZero-Company':${JSON.stringify(company)},` : ""}},
    ${body === undefined ? "" : `body:${JSON.stringify(JSON.stringify(body))},`}
  }).then(async response => {const text=await response.text();if(!response.ok)throw Error(response.status+': '+text);return JSON.parse(text);})`);
  const login = async (name) => request("/fixture/account", "POST", { name });
  return { send, evaluate, click, navigate, request, login };
}
async function screenshot(page, name, mobile = false) {
  await page.send("Emulation.setDeviceMetricsOverride", {
    width: mobile ? 390 : 1440,
    height: mobile ? 844 : 1000,
    deviceScaleFactor: 1,
    mobile,
  });
  await delay(150);
  assert.equal(
    await page.evaluate("document.documentElement.scrollWidth <= innerWidth"),
    true,
    "No horizontal overflow",
  );
  const shot = await page.send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  await fs.writeFile(
    path.join(repo, "test-results", name + ".png"),
    Buffer.from(shot.data, "base64"),
  );
}
try {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await startHost(true);
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
      "--ignore-certificate-errors",
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
            path.join(temporary, "browser/DevToolsActivePort"),
            "utf8",
          )
        ).split("\n")[0],
      ),
    "isolated browser",
  );
  const targets = () =>
    fetch(`http://127.0.0.1:${port}/json/list`).then((response) =>
      response.json(),
    );
  const owner = await connect(
    (await targets()).find((target) => target.type === "page"),
  );
  until.diagnose = pageDiagnosis(owner.evaluate);
  await owner.navigate("/");
  await until(
    () => owner.evaluate('!!document.querySelector("a[href=\\"/signin\\"]")'),
    "sign-in gate",
  );
  await owner.login("founder");
  await owner.navigate("/");
  await owner.click("Create my company");
  await until(
    () => owner.evaluate('!!document.querySelector(".co-main")'),
    "founder workspace",
  );
  const company = await owner.evaluate(
    'new URLSearchParams(location.search).get("company")',
  );
  assert.ok(company);
  await owner.request(
    "/api/company/name",
    "POST",
    { name: "Fictional Maple Bakery" },
    company,
  );
  await owner.navigate(`/?company=${company}&view=departments&section=members`);
  await until(
    () =>
      owner.evaluate(
        '!!document.querySelector(".co-members input[type=email]")',
      ),
    "owner invites",
  );
  await owner.evaluate(`(() => {
    const field=document.querySelector('.co-members input[type=email]');
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(field,'guest@example.invalid');
    field.dispatchEvent(new Event('input',{bubbles:true}));
    const role=document.querySelector('select[aria-label="Role for the invitation"]');
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(role,'viewer');
    role.dispatchEvent(new Event('change',{bubbles:true}));
  })()`);
  await owner.click("Invite");
  await until(
    () => owner.evaluate('!!document.querySelector(".co-invite-link input")'),
    "one-time link",
  );
  const link = await owner.evaluate(
    'document.querySelector(".co-invite-link input").value',
  );
  assert.equal(new URL(link).pathname, "/join");
  assert.equal(new URL(link).hash.length, 65);
  assert.equal(
    await owner.evaluate(
      '!!document.querySelector(".co-members option[value=owner]")',
    ),
    false,
    "No extra free-company owner",
  );
  const control = await connect(
    await fetch(`http://127.0.0.1:${port}/json/version`).then((response) =>
      response.json(),
    ),
    true,
  );
  const context = await control.send("Target.createBrowserContext");
  const target = await control.send("Target.createTarget", {
    url: "about:blank",
    browserContextId: context.browserContextId,
  });
  const guest = await connect(
    await until(
      async () => (await targets()).find((item) => item.id === target.targetId),
      "guest context",
    ),
  );
  until.diagnose = pageDiagnosis(guest.evaluate);
  await guest.navigate(link.replace("/join#", "/JOIN/#"));
  await until(
    () =>
      guest.evaluate(
        'document.body.innerText.includes("Sign in to review invitation")',
      ),
    "anonymous invitation",
  );
  assert.equal(
    await guest.evaluate("location.hash"),
    "",
    "Secret removed from browser address",
  );
  assert.equal(
    (
      await owner.request("/api/company/members", "GET", undefined, company)
    ).filter((member) => member.state === "active").length,
    1,
  );
  await guest.login("wrong");
  await guest.navigate("/join");
  await until(
    () =>
      guest.evaluate(
        'document.body.innerText.includes("different verified account")',
      ),
    "wrong account recovery",
  );
  assert.equal(
    await guest.evaluate(
      '[...document.querySelectorAll("button")].some(button=>button.textContent.startsWith("Join "))',
    ),
    false,
  );
  await guest.login("guest");
  await guest.navigate("/join");
  await until(
    () =>
      guest.evaluate(
        '[...document.querySelectorAll("button")].some(button=>button.textContent==="Join Fictional Maple Bakery")',
      ),
    "explicit consent",
  );
  await screenshot(guest, "team-invite-desktop");
  await screenshot(guest, "team-invite-mobile", true);
  assert.equal(
    (
      await owner.request("/api/company/members", "GET", undefined, company)
    ).filter((member) => member.state === "active").length,
    1,
    "Preview never activates membership",
  );
  await guest.click("Join Fictional Maple Bakery");
  await until(
    () => guest.evaluate('!!document.querySelector(".co-joined-notice")'),
    "joined banner",
  );
  assert.equal(
    (await guest.request("/api/session", "GET", undefined, company)).role,
    "viewer",
  );
  await screenshot(guest, "team-joined-mobile", true);
  await guest.navigate(
    `/?company=${company}&view=departments&section=companies`,
  );
  await guest.click("Create my company");
  await until(
    () =>
      guest.evaluate(
        `!!document.querySelector('.co-main') && new URLSearchParams(location.search).get('company') !== ${JSON.stringify(company)}`,
      ),
    "viewer creates own company",
  );
  const own = await guest.evaluate(
    'new URLSearchParams(location.search).get("company")',
  );
  assert.ok(own);
  assert.notEqual(own, company);
  assert.equal(
    (await guest.request("/api/company", "GET", undefined, company))
      .capabilities.freeAllowance.companyRemainingCents,
    50,
  );
  assert.equal(
    (await guest.request("/api/company", "GET", undefined, own)).capabilities
      .freeAllowance.companyRemainingCents,
    50,
  );
  // An operator disables invitations by restarting, while real session cookies and existing memberships survive.
  await stop(host);
  await startHost(false);
  await owner.navigate(`/?company=${company}&view=departments&section=members`);
  await until(
    () =>
      owner.evaluate(
        'document.body.innerText.includes("New invitations are currently unavailable")',
      ),
    "disabled invitation controls",
  );
  assert.equal(
    await owner.evaluate(
      '!!document.querySelector(".co-members input[type=email]")',
    ),
    false,
  );
  await guest.navigate(`/?company=${company}&view=departments&section=members`);
  await guest.click("Leave company");
  await screenshot(guest, "team-leave-mobile", true);
  await guest.click("Confirm leave");
  await until(
    () =>
      guest.evaluate('document.body.innerText.includes("Choose your company")'),
    "departure company chooser",
  );
  const choices = await guest.request("/api/companies");
  assert.deepEqual(
    choices.map((item) => item.id),
    [own],
  );
  assert.equal(
    (
      await owner.request(
        "/api/company/audit/verify",
        "GET",
        undefined,
        company,
      )
    ).intact,
    true,
  );
  // Recover a committed join whose response disappears; do not repeat it or consume another signup.
  await stop(host);
  await startHost(true);
  const renewed = await owner.request(
    "/api/company/member-invites",
    "POST",
    { email: "guest@example.invalid", role: "viewer" },
    company,
  );
  await guest.navigate(renewed.link);
  await until(
    () =>
      guest.evaluate(
        '[...document.querySelectorAll("button")].some(button=>button.textContent==="Join Fictional Maple Bakery")',
      ),
    "renewed invitation",
  );
  // Hold an old acceptance while the owner replaces the link and the guest reviews the replacement.
  await guest.evaluate(`(() => {
    const original = window.fetch.bind(window); window.fixtureOriginalFetch = original;
    window.fixtureJoinPosts = 0;
    window.fetch = async (input, options) => {
      if (input === '/api/join/accept') {
        window.fixtureJoinPosts++;
        await new Promise(resolve => { window.fixtureReleaseJoin = resolve; });
      }
      return original(input, options);
    };
  })()`);
  await guest.click("Join Fictional Maple Bakery");
  await until(
    () => guest.evaluate("!!window.fixtureReleaseJoin"),
    "held acceptance",
  );
  const replacement = await owner.request(
    "/api/company/member-invites",
    "POST",
    {
      email: "guest@example.invalid",
      role: "approver",
      expectedVersion: renewed.member.version,
    },
    company,
  );
  await guest.evaluate(
    `location.hash = ${JSON.stringify(new URL(replacement.link).hash)}`,
  );
  await until(
    () => guest.evaluate('document.body.innerText.includes("as an approver")'),
    "replacement review during old acceptance",
  );
  assert.equal(
    await guest.evaluate(
      'document.querySelector("button.co-primary").disabled',
    ),
    true,
    "No overlapping acceptance while the old request is pending",
  );
  await guest.evaluate("window.fixtureReleaseJoin()");
  await until(
    () =>
      guest.evaluate(
        '!!document.querySelector("button.co-primary:not(:disabled)")',
      ),
    "old acceptance settled without replacing the new review",
  );
  assert.equal(
    await guest.evaluate(
      'document.body.innerText.includes("as an approver") && !document.querySelector("[role=alert]")',
    ),
    true,
  );
  assert.equal(await guest.evaluate("window.fixtureJoinPosts"), 1);
  await guest.evaluate("window.fetch = window.fixtureOriginalFetch");
  await guest.evaluate(`(() => {
    const original = window.fetch.bind(window); window.fixtureJoinPosts = 0;
    window.fetch = async (input, options) => {
      if (input === '/api/join/accept') {
        window.fixtureJoinPosts++; const response = await original(input, options);
        await response.text(); throw new TypeError('Fictional response lost after commit');
      }
      return original(input, options);
    };
  })()`);
  await guest.click("Join Fictional Maple Bakery");
  await until(
    () =>
      guest.evaluate(
        'document.body.innerText.includes("Check your companies")',
      ),
    "lost response recovery guidance",
  );
  assert.equal(
    await guest.evaluate("window.fixtureJoinPosts"),
    1,
    "An uncertain join is never repeated",
  );
  assert.equal(
    (
      await owner.request("/api/company/members", "GET", undefined, company)
    ).filter((member) => member.state === "active").length,
    2,
  );
  assert.equal((await guest.request("/api/companies")).length, 2);
  // A new fragment on the same page is a new review, not a silently ignored link or another acceptance.
  await guest.evaluate('location.hash = "invalid"');
  await until(
    () =>
      guest.evaluate('document.body.innerText.includes("invalid, expired")'),
    "same-page invalid link feedback",
  );
  assert.equal(await guest.evaluate("window.fixtureJoinPosts"), 1);
  assert.equal(errors.length, 0, "No uncaught UI exceptions");
  assert.equal(
    allHostLog.includes(new URL(link).hash.slice(1)),
    false,
    "Invite token never appears in host log",
  );
  console.log(
    "PASS team browser: owner issuance, anonymous preview, wrong-account recovery, explicit viewer join, desktop/mobile, own-company creation, flag-off restart, viewer leave, stale acceptance isolation, lost-response recovery and same-page link changes. The guest list survived the inspection.",
  );
} catch (error) {
  await fs.writeFile(
    path.join(repo, "test-results/team-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  sockets.forEach((socket) => socket.close());
  await stop(browser);
  await stop(host);
  const resolved = path.resolve(temporary);
  if (
    path.dirname(resolved) !== path.resolve(os.tmpdir()) ||
    !path.basename(resolved).startsWith("hirezero-team-ui-")
  )
    throw Error("Refusing unexpected cleanup path");
  await fs.rm(resolved, {
    recursive: true,
    force: true,
    maxRetries: 5,
    retryDelay: 200,
  });
}
