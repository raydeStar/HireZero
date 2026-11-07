// A real arithmetic action against a disposable fixture: a failed view refresh must never repeat saved work.
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
    "--measured-browser-fixture",
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
let hostLog = "",
  browser,
  ws;
host.stdout.on("data", (b) => (hostLog += b));
host.stderr.on("data", (b) => (hostLog += b));
const until = waiter();
try {
  await until(
    () => hostLog.includes("Now listening on: " + url),
    "this fixture owns the listener",
  );
  await until(async () => (await fetch(url + "/health")).ok, "fixture health");
  assert.match(hostLog, /BROWSER FIXTURE: fake provider only/);
  const dashboard = () => fetch(url + "/api/company").then((r) => r.json());
  const initial = await dashboard();
  const employee = initial.data.employees.find((e) => e.roleId === "marketing");
  const thread = initial.data.threads.find(
    (t) =>
      t.employeeId === employee.id && (t.scope ?? "employee") === "employee",
  );
  const action = initial.data.roles
    .find((r) => r.id === employee.roleId)
    .actions.find((a) => a.id === "ab-significance");
  assert.ok(action, "The fixture exposes a pure arithmetic action");
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
            path.join(temporary, "browser/DevToolsActivePort"),
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
  const pending = new Map(),
    errors = [];
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
  await send("Page.navigate", {
    url: `${url}/?view=employee&employee=${employee.id}&thread=${thread.id}`,
  });
  await until(
    () => evaluate('!!document.querySelector(".co-action-strip")'),
    "employee actions loaded",
  );
  const click = clicker(evaluate, until);
  await evaluate("document.querySelector('.co-action-strip summary').click()");
  await evaluate(`(() => {
    const button = [...document.querySelectorAll('.co-action-chip')].find(b => b.textContent === ${JSON.stringify(action.name)});
    if (button) button.click();
    else {
      const select = document.querySelector('.co-action-more');
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(select, ${JSON.stringify(action.id)});
      select.dispatchEvent(new Event('change', { bubbles: true }));
    }
  })()`);
  await until(
    () => evaluate('!!document.querySelector(".co-action")'),
    "action form opened",
  );
  const values = {
    "control-visitors": "1000",
    "control-conversions": "50",
    "variant-visitors": "1000",
    "variant-conversions": "70",
    alpha: "5",
  };
  for (const field of action.fields) {
    await evaluate(`(() => {
      const el = [...document.querySelectorAll('.co-action input,.co-action textarea,.co-action select')].find(e => e.getAttribute('aria-label') === ${JSON.stringify(field.label)});
      const proto = el.tagName === 'SELECT' ? HTMLSelectElement.prototype : el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, ${JSON.stringify(values[field.id])});
      el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    })()`);
  }
  const inputValues = () =>
    evaluate(
      "[...document.querySelectorAll('.co-action input,.co-action textarea,.co-action select')].map(e=>e.value)",
    );
  const submitted = action.fields.map((field) => values[field.id]);
  assert.deepEqual(await inputValues(), submitted);
  await evaluate(`(() => {
    const original = window.fetch.bind(window);
    window.__actionBaseFetch = original;
    window.__actionPosts = 0;
    window.__failedRefreshes = 0;
    window.__failActionRefresh = false;
    window.fetch = async (...args) => {
      const target = typeof args[0] === 'string' ? args[0] : args[0].url;
      if (target === '/api/company/actions' && args[1]?.method === 'POST') {
        window.__actionPosts++;
        const response = await original(...args);
        window.__failActionRefresh = true;
        return response;
      }
      if (target === '/api/company' && window.__failActionRefresh) {
        window.__failedRefreshes++;
        return new Response(JSON.stringify({ error: 'Synthetic saved-view refresh outage' }), { status: 503, headers: { 'Content-Type': 'application/json' } });
      }
      return original(...args);
    };
  })()`);
  await click("Run");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-action')?.innerText.includes('Action saved. The workspace could not refresh.')",
      ),
    "confirmed save distinguished from refresh failure",
  );
  const tasks = (await dashboard()).data.tasks.filter(
    (t) => t.kind === "action" && t.skill === action.id,
  );
  assert.equal(tasks.length, 1, "Exactly one real action was recorded");
  assert.equal(await evaluate("window.__actionPosts"), 1);
  assert.deepEqual(
    await inputValues(),
    submitted,
    "Confirmed inputs survive refresh failure",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-action button')].some(b=>b.textContent==='Run')",
    ),
    false,
    "A confirmed action cannot be accidentally rerun",
  );
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-action input,.co-action textarea,.co-action select')].every(e=>e.disabled)",
    ),
    true,
  );
  assert.equal(
    await evaluate("document.querySelector('.co-action details')?.open"),
    false,
    "Submitted inputs start collapsed after confirmation",
  );
  assert.equal(
    await evaluate(
      "document.querySelector('.co-action details summary')?.textContent",
    ),
    "Submitted inputs",
  );
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/action-recovery-position.png"),
    Buffer.from(
      (
        await send("Page.captureScreenshot", {
          format: "png",
          captureBeyondViewport: false,
        })
      ).data,
      "base64",
    ),
  );
  assert.equal(
    await evaluate(`(() => {
    const button = [...document.querySelectorAll('.co-action button')].find(b => b.textContent === 'Retry refresh');
    const r = button.getBoundingClientRect();
    return r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth && document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2) === button;
  })()`),
    true,
    "Retry refresh is immediately visible and reachable without scrolling",
  );

  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  const recoveryImage = await send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  await fs.writeFile(
    path.join(repo, "test-results/action-recovery-desktop.png"),
    Buffer.from(recoveryImage.data, "base64"),
  );
  await evaluate(
    "document.querySelector('.co-action details summary').click()",
  );
  assert.equal(
    await evaluate("document.querySelector('.co-action details').open"),
    true,
    "Submitted inputs can be reviewed",
  );
  assert.deepEqual(
    await inputValues(),
    submitted,
    "Collapsed submitted inputs retain every original value",
  );
  const controlsImage = await send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: false,
  });
  await fs.writeFile(
    path.join(repo, "test-results/action-recovery-controls-desktop.png"),
    Buffer.from(controlsImage.data, "base64"),
  );
  await evaluate(
    "document.querySelector('.co-action details summary').click()",
  );
  const failedBefore = await evaluate("window.__failedRefreshes");
  await click("Retry refresh");
  await until(
    () =>
      evaluate(
        `window.__failedRefreshes > ${failedBefore} && [...document.querySelectorAll('.co-action button')].some(b=>b.textContent==='Retry refresh'&&!b.disabled)`,
      ),
    "failed refresh retry leaves recovery available",
  );
  assert.equal(
    await evaluate("window.__actionPosts"),
    1,
    "A failed read-only retry does not send another action",
  );
  await evaluate("window.__failActionRefresh = false");
  await click("Retry refresh");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-action')?.innerText.includes('Action saved. Review its result in this conversation.')",
      ),
    "successful refresh reveals saved result",
  );
  await until(
    () =>
      evaluate(`!!document.querySelector('[data-task-id="${tasks[0].id}"]')`),
    "saved result visible in the conversation",
  );
  assert.equal(
    await evaluate("window.__actionPosts"),
    1,
    "Successful refresh retry is also read-only",
  );
  assert.equal(
    (await dashboard()).data.tasks.filter(
      (t) => t.kind === "action" && t.skill === action.id,
    ).length,
    1,
  );
  assert.deepEqual(
    await inputValues(),
    submitted,
    "Inputs remain available until a deliberate new attempt",
  );
  await click("Start another run");
  await until(
    () =>
      evaluate(
        "[...document.querySelectorAll('.co-action button')].some(b=>b.textContent==='Run'&&!b.disabled)",
      ),
    "an explicit new attempt returns to the form",
  );
  assert.equal(
    await evaluate("window.__actionPosts"),
    1,
    "Starting another form does not itself run work",
  );
  assert.equal(
    await evaluate("document.querySelector('.co-action input').value"),
    "",
  );
  // A receipt must survive before dispatch. Unavailable storage must refuse a new run.
  for (const field of action.fields) {
    await evaluate(`(() => {
      const el = [...document.querySelectorAll('.co-action input,.co-action textarea,.co-action select')].find(e => e.getAttribute('aria-label') === ${JSON.stringify(field.label)});
      const proto = el.tagName === 'SELECT' ? HTMLSelectElement.prototype : el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, ${JSON.stringify(values[field.id])});
      el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    })()`);
  }
  await evaluate(
    `window.__storageSet = Storage.prototype.setItem; Storage.prototype.setItem = function(key,value) { if(key.includes('hirezero-action-receipts-v1:')) throw new Error('Synthetic unavailable storage'); return window.__storageSet.call(this,key,value); }`,
  );
  await click("Run");
  await until(
    () =>
      evaluate(
        "document.body.innerText.includes('The action recovery receipt could not be saved.')",
      ),
    "Storage refusal before sending",
  );
  assert.equal(await evaluate("window.__actionPosts"), 1);
  await evaluate(`Storage.prototype.setItem = window.__storageSet; window.fetch = async (...args) => {
    const target = typeof args[0] === 'string' ? args[0] : args[0].url;
    if(target === '/api/company/actions' && args[1]?.method === 'POST') {
      window.__actionPosts++; await window.__actionBaseFetch(...args); throw new TypeError('Synthetic lost action reply');
    }
    if(target.startsWith('/api/company/operations/')) return new Response('{}',{status:503,headers:{'Content-Type':'application/json'}});
    return window.__actionBaseFetch(...args);
  }`);
  await click("Run");
  await until(
    () =>
      evaluate(
        "document.body.innerText.includes('This action needs a status check.')",
      ),
    "Lost reply retains exact receipt",
  );
  assert.equal(await evaluate("window.__actionPosts"), 2);
  assert.deepEqual(await inputValues(), submitted);
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-action input,.co-action textarea,.co-action select')].every(e=>e.disabled)",
    ),
    true,
  );
  await click("Check saved status");
  await until(
    () =>
      evaluate(
        "document.body.innerText.includes('The saved action could not be confirmed.')",
      ),
    "Unavailable receipt leaves read-only recovery",
  );
  assert.equal(await evaluate("window.__actionPosts"), 2);
  const beforeReload = (await dashboard()).data.tasks.filter(
    (t) => t.kind === "action" && t.skill === action.id,
  );
  assert.equal(beforeReload.length, 2);
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await send("Page.reload");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-action')?.innerText.includes('This action needs a status check.')",
      ),
    "Reload restores the same action receipt and submitted inputs",
  );
  assert.deepEqual(await inputValues(), submitted);
  assert.equal(
    await evaluate(
      "[...document.querySelectorAll('.co-action button')].some(b=>b.textContent==='Run')",
    ),
    false,
  );
  await click("Check saved status");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-action')?.innerText.includes('Action saved. Review its result in this conversation.')",
      ),
    "Read-only recovery finds the original completed action",
  );
  assert.equal(
    (await dashboard()).data.tasks.filter(
      (t) => t.kind === "action" && t.skill === action.id,
    ).length,
    2,
    "Reload recovery does not rerun work",
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  await fs.writeFile(
    path.join(repo, "test-results/action-reload-mobile.png"),
    Buffer.from(
      (await send("Page.captureScreenshot", { format: "png" })).data,
      "base64",
    ),
  );
  // A late reply from an unmounted form must not resurrect a receipt already recovered and cleared.
  await click("Start another run");
  for (const field of action.fields) {
    await evaluate(`(() => {
      const el = [...document.querySelectorAll('.co-action input,.co-action textarea,.co-action select')].find(e => e.getAttribute('aria-label') === ${JSON.stringify(field.label)});
      const proto = el.tagName === 'SELECT' ? HTMLSelectElement.prototype : el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, ${JSON.stringify(values[field.id])});
      el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
    })()`);
  }
  await evaluate(`window.__lateBaseFetch = window.fetch.bind(window); window.fetch = async (...args) => {
    const response = await window.__lateBaseFetch(...args);
    if(args[0] === '/api/company/actions' && args[1]?.method === 'POST') {
      await new Promise(resolve => window.__releaseLateAction = resolve); window.__lateActionDelivered = true;
    }
    return response;
  }`);
  await click("Run");
  await until(
    () => evaluate("typeof window.__releaseLateAction==='function'"),
    "Original action saved with its response held",
  );
  await evaluate(
    "history.pushState({},'', '/?view=home&home=overview'); dispatchEvent(new PopStateEvent('popstate'))",
  );
  await until(
    () => evaluate("!document.querySelector('.co-action-strip')"),
    "Action form unmounted during navigation",
  );
  await evaluate(
    `history.pushState({},'', ${JSON.stringify(`/?view=employee&employee=${employee.id}&thread=${thread.id}`)}); dispatchEvent(new PopStateEvent('popstate'))`,
  );
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-action')?.innerText.includes('This action needs a status check.')",
      ),
    "Returning restores the same pending receipt",
  );
  await click("Check saved status");
  await until(
    () =>
      evaluate(
        "document.querySelector('.co-action')?.innerText.includes('Action saved. Review its result in this conversation.')",
      ),
    "Existing task recovered before its old response arrives",
  );
  await click("Start another run");
  await evaluate("window.__releaseLateAction()");
  await until(
    () => evaluate("window.__lateActionDelivered===true"),
    "Late original response delivered once",
  );
  await evaluate(
    "new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))",
  );
  assert.equal(
    await evaluate(
      `Object.keys(JSON.parse(sessionStorage.getItem(${JSON.stringify(`hirezero-action-receipts-v1:local-company:${employee.id}:${thread.id}`)}))).length`,
    ),
    0,
    "Unmounted response cannot restore an old receipt",
  );
  assert.equal(
    (await dashboard()).data.tasks.filter(
      (t) => t.kind === "action" && t.skill === action.id,
    ).length,
    3,
  );
  assert.equal(
    await evaluate("document.documentElement.scrollWidth<=innerWidth+1"),
    true,
  );
  const files = await fs.readdir(temporary, { recursive: true });
  assert.equal(
    files.filter((file) => file.endsWith("fixture-native-starts")).length,
    0,
    "Arithmetic action never calls a provider",
  );
  assert.equal(errors.length, 0, "No uncaught UI exceptions");
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/action-recovery-browser.json"),
    JSON.stringify(
      {
        action: action.id,
        actionPosts: 3,
        savedTasks: 3,
        failedRefreshDistinguished: true,
        retryReadOnly: true,
        recoveryVisibleWithoutScrolling: true,
        submittedInputsCollapsed: true,
        inputsPreserved: true,
        explicitNewAttempt: true,
        missingStoragePreventsDispatch: true,
        lostReplyRetainsLockedInputs: true,
        unavailableReceiptNeverReposts: true,
        reloadRestoresReceiptAndInputs: true,
        reloadRecoveryReadOnly: true,
        lateUnmountedResponseCannotRestoreReceipt: true,
        mobileNoHorizontalOverflow: true,
        liveModelCalls: 0,
      },
      null,
      2,
    ),
  );
  console.log(
    "PASS action recovery: saved work survives failed refresh, inputs stay intact, retries are read-only; the butler keeps the receipt.",
  );
  await send("Browser.close").catch(() => {});
} catch (error) {
  await fs.mkdir(path.join(repo, "test-results"), { recursive: true });
  await fs.writeFile(
    path.join(repo, "test-results/action-recovery-browser-host.log"),
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
