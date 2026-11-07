// Onboarding with HireZero (docs/adr/0186): a new company starts in chat. HireZero asks the business name, website and
// goal, reads the site, drafts the company brief for the owner to correct and approve, asks what they need, and brings
// in the first teammate, then steps aside. Fictional providers and a fictional website only.
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
      HIREZERO_BROWSER_ONBOARDING: "1",
      HIREZERO_BROWSER_SYNTHETIC_BUSINESS: "1",
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
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "this test host owns the listener",
  );
  await until(
    async () => (await fetch(url + "/health")).ok,
    "fresh host health",
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
    () => evaluate("!!document.querySelector('.co-workspace')"),
    "the workspace",
  );
  // The fixture seeds two employees; retired from the page, the company has no team, as a new sign-up does.
  assert.equal(
    await evaluate(`(async () => {
      const s = await fetch('/api/company').then((r) => r.json());
      let ok = 0;
      for (const e of s.data.employees) {
        const r = await fetch('/api/company/commands', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'X-Dev-Request': '1' },
          body: JSON.stringify({ requestId: crypto.randomUUID(), action: 'employee.lifecycle', id: e.id, expectedVersion: e.version, decision: 'retire' }),
        });
        if (r.ok) ok++;
      }
      return ok;
    })()`),
    2,
  );
  await send("Page.reload");
  await until(
    () => evaluate("!!document.querySelector('.co-onboard')"),
    "onboarding with HireZero",
  );
  const click = clicker(evaluate, until),
    idle = pageIdle(evaluate, until);
  const text = (phrase) =>
    until(
      () =>
        evaluate(`document.body.innerText.includes(${JSON.stringify(phrase)})`),
      phrase,
    );
  // Sets a field the way typing would, so React sees the change.
  const fill = (selector, value) =>
    evaluate(`(() => {
      const el = document.querySelector(${JSON.stringify(selector)});
      if (!el) throw new Error('Missing field: ' + ${JSON.stringify(selector)});
      const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement : el.tagName === 'SELECT' ? HTMLSelectElement : HTMLInputElement;
      Object.getOwnPropertyDescriptor(proto.prototype, 'value').set.call(el, ${JSON.stringify(value)});
      el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    })()`);
  const capture = async (name) =>
    fs.writeFile(
      path.join(repo, "test-results/" + name + ".png"),
      Buffer.from(
        (await send("Page.captureScreenshot", { format: "png" })).data,
        "base64",
      ),
    );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  // Each answer is the owner's Send, one reply each.
  const say = async (message, reply) => {
    await fill(".co-onboard .fe-composer textarea", message);
    await idle();
    await evaluate(
      "document.querySelector('.co-conversation-composer form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))",
    );
    await text(reply);
  };

  await text("Hi, I’m HireZero!");
  await capture("onboarding-greeting");
  await fill("#co-onboard-business", "Fictional Paper Studio");
  await click("Send");
  await text("Lovely to meet you, Fictional Paper Studio!");
  let state = (await dashboard()).data;
  const host = state.employees.find((e) => e.roleId === "host");
  assert.equal(host?.state, "active", "HireZero joined to answer");
  await say(
    "We're at paper.example.invalid and we sell paper craft kits",
    "Paper kits sound fun.",
  );
  await say(
    "More people signing up for our weekend workshops",
    "Your details are below to check",
  );

  // The details HireZero heard, to check before it reads the site.
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-onboard-details input[maxlength=\"300\"]')?.value === 'paper.example.invalid'",
      ),
    "website picked up from the chat",
  );
  await click("Look us up");
  await text("Here’s what I found.");
  state = (await dashboard()).data;
  assert.equal(state.business.website, "https://paper.example.invalid/");
  assert.equal(state.businessProfiles.at(-1).state, "draft");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-onboard-fact button') && !document.querySelector('.co-onboard-fact button:disabled')",
      ),
    "the brief is ready to correct",
  );
  // "This is wrong": the owner's own words replace a line.
  await evaluate(
    "[...document.querySelectorAll('.co-onboard-fact')].find((f) => f.querySelector('dt').textContent.startsWith('Who you serve')).querySelector('button').click()",
  );
  await until(
    () =>
      evaluate(
        "!!document.querySelector('textarea[aria-label=\"Rewrite Who you serve\"]')",
      ),
    "rewrite box",
  );
  await fill(
    "textarea[aria-label='Rewrite Who you serve']",
    "Adults who want a calm creative hobby",
  );
  await click("Save");
  await until(
    async () =>
      (await dashboard()).data.businessProfiles
        .at(-1)
        .fields.find((f) => f.key === "customers")?.verification === "owner",
    "owner correction saved",
  );
  await text("Adults who want a calm creative hobby");
  await capture("onboarding-findings");
  await click("Looks right");
  await text("what do you need help with most right now?");
  state = (await dashboard()).data;
  assert.equal(state.businessProfiles.at(-1).state, "approved");
  await say(
    "Getting found by people nearby",
    "Say yes below and I'll bring them in.",
  );
  await text("Bring in a Marketing partner?");
  await capture("onboarding-offer");

  // Yes: the first teammate starts on a draft and HireZero steps aside; Home becomes their chat.
  await click("Yes, bring them in");
  await until(
    () =>
      evaluate(
        "!!document.querySelector('.co-desk-head') && !document.querySelector('.co-onboard')",
      ),
    "the desk with the first teammate",
  );
  state = (await dashboard()).data;
  assert.equal(
    state.employees.find((e) => e.id === host.id).state,
    "retired",
    "HireZero stepped aside",
  );
  const marketing = state.employees.find(
    (e) => e.roleId === "marketing" && e.state === "active",
  );
  assert.ok(marketing, "the Marketing partner is on the team");
  const first = state.tasks.find(
    (t) => t.employeeId === marketing.id && t.kind !== "conversation",
  );
  assert.ok(first, "their first draft started");
  assert.match(
    first.input,
    /Getting found by people nearby/,
    "what the owner needs shapes the first draft",
  );
  assert.match(
    await evaluate("document.querySelector('.co-desk-head').innerText"),
    /Marketing partner/,
  );
  await capture("onboarding-handoff");
  assert.deepEqual(errors, []);
  console.log(
    "PASS onboarding: HireZero in chat, a site read, an owner correction, an approved brief, and the first teammate hired.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  try {
    const failureState = await fetch(url + "/api/company", {
      signal: AbortSignal.timeout(5000),
    }).then((r) => r.json());
    await fs.writeFile(
      path.join(repo, "test-results/onboarding-browser-failure.json"),
      JSON.stringify(failureState, null, 2),
    );
  } catch {
    /* Retain the original failure if the fixture is already unavailable. */
  }
  await fs.writeFile(
    path.join(repo, "test-results/onboarding-browser-host.log"),
    hostLog,
  );
  throw error;
} finally {
  ws?.close();
  const stop = async (child) => {
    if (!child || child.exitCode !== null || child.signalCode !== null) return;
    await new Promise((resolve, reject) => {
      const timer = setTimeout(
        () =>
          reject(new Error("Owned fixture did not exit; retaining its files.")),
        10000,
      );
      child.once("exit", () => {
        clearTimeout(timer);
        resolve();
      });
      child.kill();
    });
  };
  await Promise.all([stop(browser), stop(host)]);
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
