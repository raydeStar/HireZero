// Fictional billing passes through the real HTTP routes, durable ledger and UI. No external payment requests.
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

      HIREZERO_BROWSER_BILLING: "1",
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
  await send("Page.addScriptToEvaluateOnNewDocument", {
    source: `(() => { const original = window.fetch; window.fetch = (...args) => { if (args[0] === '/api/company/billing' && !sessionStorage.getItem('fictional-billing-load-failed')) { sessionStorage.setItem('fictional-billing-load-failed', '1'); return Promise.reject(new Error('Fictional initial connection loss')); } return original(...args); }; })();`,
  });
  until.diagnose = async () =>
    (await pageDiagnosis(evaluate)()) +
    "\n" +
    (await evaluate("document.querySelector('.co-billing')?.innerText"));
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1000,
    deviceScaleFactor: 1,
    mobile: false,
  });
  const click = clicker(evaluate, until);
  let companyId = "";
  const api = async (route, body) => {
    const response = await fetch(url + "/api" + route, {
      method: body === undefined ? "GET" : "POST",
      headers: {
        Origin: url,
        "X-Dev-Request": "1",
        "Content-Type": "application/json",
        ...(companyId ? { "X-HireZero-Company": companyId } : {}),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    const value = await response.json();
    assert.equal(response.ok, true, JSON.stringify(value));
    return value;
  };
  const waitText = (text) =>
    until(
      () =>
        evaluate(
          `document.querySelector('.co-billing')?.textContent.replace(/\\s+/g, ' ').includes(${JSON.stringify(text)})`,
        ),
      text,
    );
  const page = () =>
    url +
    "/?view=departments&section=billing" +
    (companyId ? "&company=" + companyId : "");
  const stats = () => api("/fixture/billing", { action: "stats" });
  const deliver = async (envelope) => {
    const response = await fetch(url + "/api/billing/stripe/events", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Stripe-Signature": envelope.signature,
      },
      body: envelope.body,
    });
    assert.equal(response.status, 200);
  };
  const snapshot = async (name) => {
    await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
    await fs.writeFile(
      path.join(repo, "test-results", name),
      Buffer.from(
        (await send("Page.captureScreenshot", { format: "png" })).data,
        "base64",
      ),
    );
  };
  await send("Page.navigate", { url: page() });
  await waitText(
    "Billing could not be loaded. Retry to check your saved subscription.",
  );
  await click("Retry loading billing");
  await waitText("Choose a plan");
  await waitText("Test mode");
  await click("Review Fictional Studio");
  await waitText("Review before continuing");
  assert.equal(
    await evaluate(
      `document.querySelector('.co-billing-review').innerText.includes('$19.00')`,
    ),
    true,
  );
  assert.equal(
    await evaluate(
      `document.querySelector('.co-billing-review button').disabled`,
    ),
    true,
  );
  assert.equal((await stats()).creates, 0);
  await snapshot("billing-review-desktop.png");
  await send("Page.reload");
  await waitText("Review before continuing");
  assert.equal(
    await evaluate(
      `document.querySelector('.co-billing-consent input').checked`,
    ),
    false,
  );
  await evaluate(`document.querySelector('.co-billing-consent input').click()`);
  await evaluate(
    `(() => { const original = window.fetch; window.__lostBillingApproval = 0; window.fetch = async (...args) => { const body = args[1]?.body; if (body && JSON.parse(body).action === 'billing.checkout' && !window.__lostBillingApproval) { window.__lostBillingApproval++; await original(...args); throw new Error('Fictional lost approval reply'); } return original(...args); }; })()`,
  );
  await click("Prepare secure checkout");
  await until(
    () =>
      evaluate(
        `!!document.querySelector('.co-billing a[href^="https://checkout.stripe.com/"]')`,
      ),
    "one saved checkout link",
  );
  assert.equal(await evaluate("window.__lostBillingApproval"), 1);
  assert.equal((await stats()).creates, 1);
  let view = await api("/company/billing");
  const firstOperation = view.checkout.id;
  assert.equal(view.allowance, null);
  assert.equal(
    await evaluate(
      `document.querySelector('.co-billing a[href^="https://checkout.stripe.com/"]').target`,
    ),
    "_blank",
  );
  // A late failed poll must not spoil a newer verified payment. Even receipts deserve punctuality.
  await evaluate(`(() => {
    const original = window.fetch;
    window.__billingPollHeld = false;
    window.__billingPollReleased = false;
    window.fetch = (...args) => {
      if (args[0] === '/api/company/billing' && !window.__billingPollHeld) {
        window.__billingPollHeld = true;
        return new Promise((resolve, reject) => {
          window.__releaseBillingPoll = () => {
            reject(new Error('Fictional old billing read failure'));
            window.__billingPollReleased = true;
          };
        });
      }
      return original(...args);
    };
  })()`);
  await until(
    () => evaluate("window.__billingPollHeld"),
    "older billing poll held",
  );
  const paid = await api("/fixture/billing", {
    action: "paid",
    operation: firstOperation,
  });
  await deliver(paid);
  await deliver(paid);
  await evaluate(`(() => {
    const original = window.fetch;
    window.__billingRefreshCalls = 0;
    window.fetch = (...args) => {
      if (args[0] === '/api/company/billing/refresh' && !window.__billingRefreshCalls) {
        window.__billingRefreshCalls++;
        return new Promise((resolve, reject) => {
          window.__releaseBillingRefresh = () => original(...args).then(resolve, reject);
        });
      }
      return original(...args);
    };
  })()`);
  await click("Check payment status");
  await waitText("Checking payment…");
  assert.equal(
    await evaluate(
      `document.querySelector('.co-billing button[aria-busy="true"]')?.disabled`,
    ),
    true,
  );
  await evaluate(
    `document.querySelector('.co-billing button[aria-busy="true"]').click()`,
  );
  assert.equal(
    await evaluate("window.__billingRefreshCalls"),
    1,
    "Checking payment cannot submit twice.",
  );
  await snapshot("billing-checking-desktop.png");
  await evaluate("window.__releaseBillingRefresh()");
  await waitText("Subscription active");
  await waitText("Payment status checked.");
  await evaluate("window.__releaseBillingPoll()");
  await until(
    () => evaluate("window.__billingPollReleased"),
    "older failed poll released",
  );
  await delay(200);
  assert.equal(
    await evaluate(
      `document.querySelector('.co-billing [role="alert"]')?.textContent || ''`,
    ),
    "",
    "A superseded billing poll cannot display an error over the verified subscription.",
  );
  await evaluate(`(() => {
    const original = window.fetch;
    let failed = false;
    window.fetch = (...args) => {
      if (args[0] === '/api/company/billing' && !failed) {
        failed = true;
        return Promise.reject(new Error('Fictional current billing read failure'));
      }
      return original(...args);
    };
  })()`);
  await click("Check payment status");
  await waitText("The information could not be loaded. Try again.");
  await click("Check payment status");
  await waitText("Payment status checked.");
  assert.equal(
    await evaluate(
      `document.querySelector('.co-billing [role="alert"]')?.textContent || ''`,
    ),
    "",
    "The latest read failure remains visible and an explicit successful check clears it.",
  );
  assert.equal((await stats()).creates, 1);
  await waitText("Remaining: 3 AI jobs");
  await snapshot("billing-active-desktop.png");
  await send("Page.navigate", { url: page() + "&billingReturn=1" });
  await waitText("Payment status checked.");
  assert.equal((await stats()).creates, 1);
  await api("/fixture/billing", { action: "lose-next-portal" });
  await click("Manage billing");
  await waitText("The earlier portal link could not be confirmed");
  assert.equal((await stats()).portals, 1);
  await click("Create a new billing link");
  await until(
    () =>
      evaluate(
        `!!document.querySelector('.co-billing a[href^="https://billing.stripe.com/"]')`,
      ),
    "explicit replacement portal link",
  );
  assert.equal((await stats()).portals, 2);
  await deliver(
    await api("/fixture/billing", {
      action: "past_due",
      operation: firstOperation,
    }),
  );
  await until(
    async () => (await api("/company/billing")).state.status === "past_due",
    "canonical payment failure",
  );
  await click("Check payment status");
  await waitText("Payment overdue");
  await click("View usage");
  await until(
    () =>
      evaluate(
        `document.querySelector('.co-usage-allowance')?.innerText.includes('0 jobs and 0 planning tokens remain')`,
      ),
    "usage respects unpaid allowance",
  );
  await click("Review billing");
  await waitText("Payment overdue");
  const other = await api("/companies", { name: "Fictional second studio" });
  companyId = other.id;
  await send("Page.navigate", { url: page() });
  await waitText("Choose a plan");
  assert.equal((await api("/company/billing")).checkout, null);
  await api("/fixture/billing", { action: "lose-next-checkout" });
  await click("Review Fictional Studio");
  await evaluate(`document.querySelector('.co-billing-consent input').click()`);
  await click("Prepare secure checkout");
  await waitText("Checkout needs confirmation");
  view = await api("/company/billing");
  assert.equal(view.checkout.receipt, null);
  assert.equal(view.allowance, null);
  assert.equal((await stats()).creates, 2);
  await deliver(
    await api("/fixture/billing", {
      action: "paid",
      operation: view.checkout.id,
    }),
  );
  await waitText("Subscription active");
  assert.equal((await stats()).creates, 2);
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await evaluate(`document.querySelector('.co-billing').scrollIntoView()`);
  await delay(200);
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  await snapshot("billing-active-mobile.png");
  await deliver(
    await api("/fixture/billing", {
      action: "canceled",
      operation: view.checkout.id,
    }),
  );
  await until(
    async () => (await api("/company/billing")).state.status === "canceled",
    "confirmed cancellation",
  );
  await click("Check payment status");
  await waitText("Subscription canceled");
  await click("Review Fictional Studio");
  await until(
    () => evaluate(`!!document.querySelector('.co-billing-review')`),
    "mobile plan review",
  );
  await evaluate(
    `document.querySelector('.co-billing-review').scrollIntoView()`,
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth <= innerWidth + 1"),
    true,
  );
  const consent = await evaluate(
    `(() => { const label = document.querySelector('.co-billing-consent'), input = label.querySelector('input'), text = label.querySelector('span'); const a=input.getBoundingClientRect(), b=text.getBoundingClientRect(); return { top: Math.abs(a.top-b.top), left: b.left-a.right }; })()`,
  );
  assert.ok(consent.top < 12 && consent.left > 0, JSON.stringify(consent));
  await snapshot("billing-review-mobile.png");
  await evaluate(`document.documentElement.dataset.theme = 'light'`);
  await snapshot("billing-review-mobile-light.png");
  assert.deepEqual(errors, []);
  await fs.writeFile(
    path.join(repo, "test-results/billing-browser.json"),
    JSON.stringify(
      {
        exactPriceAndConsent: true,
        reloadPreservesReview: true,
        lostApprovalReplyRecovered: true,
        staleReadCannotOverridePayment: true,
        currentReadFailureRecoverable: true,
        paymentCheckProgress: true,
        checkoutCalls: 2,
        companies: 2,
        duplicateWebhookSafe: true,
        unknownCheckoutRecoveredFromEvent: true,
        portalReplacementExplicit: true,
        unpaidUsageZero: true,
        mobileOverflow: false,
        livePaymentRequests: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS billing: two companies, two checkouts, and every receipt in its proper drawer.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/billing-browser-host.log"),
    hostLog,
  );
  throw error;
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
