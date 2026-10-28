import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
const source = fs.readFileSync(
  new URL("../web/src/company/api.ts", import.meta.url),
  "utf8",
);
const code = ts.transpileModule(source, {
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
  },
}).outputText;
class ApiError extends Error {
  constructor(status) {
    super("Synthetic failure");
    this.status = status;
  }
}
let requests = [],
  scenario = "ok",
  sequence = 0,
  finishInFlight;
const storage = new Map();
const scopedKey = "hirezero-company-operation-v2:local-company",
  legacyKey = "hirezero-company-operation";
const api = async (path, method = "GET", body) => {
  requests.push({ path, method, body });
  if (path === "/company/commands") {
    if (scenario === "inflight" && body.action === "measured.run")
      return new Promise((resolve) => (finishInFlight = resolve));
    if (
      scenario === "lost" ||
      scenario === "unconfirmed" ||
      scenario === "lost-concurrent"
    )
      throw new ApiError(0);
    if (scenario === "refused") throw new ApiError(409);
    return { saved: true };
  }
  if (scenario === "unconfirmed") throw new ApiError(404);
  if (scenario === "lost-concurrent")
    storage.set(scopedKey, "fictional-other-operation");
  return { saved: true, reconciled: true };
};
const browserStorage = {
  getItem: (key) => storage.get(key) || null,
  setItem: (key, value) => storage.set(key, value),
  removeItem: (key) => storage.delete(key),
};
const crypto = {
  randomUUID: () =>
    `00000000-0000-4000-8000-${String(++sequence).padStart(12, "0")}`,
};
function client(company = "", sessionStorage = browserStorage) {
  const exports = {};
  vm.runInNewContext(code, {
    exports,
    require: () => ({
      api,
      ApiError,
      company: () => company,
      memberStorage: sessionStorage,
    }),
    crypto,
    sessionStorage,
  });
  return exports;
}
const exports = client();
await assert.rejects(exports.command("decision.prepare", {}, () => {
  throw new Error("Fictional denied receipt storage");
}), /denied receipt storage/);
assert.equal(requests.length, 0, "Receipt storage refusal happens before any network call");
assert.equal(storage.has(scopedKey), false, "A refused dispatch must not leave an impossible operation to reconcile");
let attempted;
await exports.command("department.create", { name: "Fictional" }, (id) => {
  attempted = id;
  assert.equal(
    storage.get(scopedKey),
    id,
    "Attempt identity is durable before the request starts",
  );
});
assert.equal(attempted, requests[0].body.requestId);
assert.equal(requests.length, 1);
assert.equal(requests[0].body.action, "department.create");
requests = [];
scenario = "lost";
const confirmed = await exports.command("task.create", {
  employeeId: "employee-1",
});
assert.equal(confirmed.reconciled, true);
assert.equal(
  requests.filter((r) => r.method === "POST").length,
  1,
  "Lost response is reconciled, never reposted",
);
assert.equal(storage.size, 0);
requests = [];
scenario = "lost-concurrent";
await exports.command("task.create");
assert.equal(
  storage.get(scopedKey),
  "fictional-other-operation",
  "A late receipt cannot erase another uncertain operation",
);
storage.clear();
requests = [];
scenario = "unconfirmed";
await assert.rejects(exports.command("task.create"), /unconfirmed/);
assert.equal(storage.size, 1);
const operation = storage.values().next().value;
await assert.rejects(exports.command("task.create"), /previous operation/);
assert.equal(
  requests.filter((r) => r.method === "POST").length,
  1,
  "Further actions cannot accidentally duplicate an unresolved mutation",
);
assert.ok(requests.at(-1).path.endsWith(operation));
scenario = "ok";
await assert.rejects(
  exports.command("task.create"),
  /previous operation is confirmed/,
);
assert.equal(storage.size, 0);
await exports.command("task.create");
assert.equal(requests.filter((r) => r.method === "POST").length, 2);
scenario = "refused";
await assert.rejects(exports.command("task.create"), ApiError);
assert.equal(
  storage.size,
  0,
  "Definitive domain refusal is not an uncertain outcome",
);
requests = [];
scenario = "inflight";
const running = exports.command("measured.run", { id: "fictional-turn" });
assert.equal(
  storage.size,
  0,
  "An ordinary in-flight run does not become an unresolved operation",
);
await exports.command("task.cancel", { id: "fictional-turn" });
assert.equal(
  requests.filter((r) => r.method === "POST").length,
  2,
  "Owner cancellation remains available during an in-flight run",
);
finishInFlight({ saved: true });
await running;
const denied = () => {
  throw new Error("Storage denied");
};
for (const sessionStorage of [
  { getItem: denied, setItem: denied, removeItem: denied },
  { getItem: () => null, setItem: denied, removeItem: denied },
]) {
  const restricted = client("", sessionStorage);
  requests = [];
  scenario = "ok";
  await restricted.command("employee.hire");
  scenario = "unconfirmed";
  await assert.rejects(
    restricted.command("employee.hire"),
    (error) =>
      error instanceof restricted.UnconfirmedCompanyOperation &&
      !!error.requestId,
    "Storage-denied tabs retain an explicit uncertain identity for their in-memory guide",
  );
  await assert.rejects(
    restricted.command("employee.hire"),
    /previous operation/,
  );
  assert.equal(
    requests.filter((r) => r.method === "POST").length,
    2,
    "Denied writes cannot release the in-memory duplicate guard",
  );
  scenario = "ok";
  await assert.rejects(
    restricted.command("employee.hire"),
    /previous operation is confirmed/,
  );
  await restricted.command("employee.hire");
  assert.equal(
    requests.filter((r) => r.method === "POST").length,
    3,
    "Confirmed in-memory receipts release the guard",
  );
}

storage.clear();
requests = [];
const first = client("co-fictional-first"),
  second = client("co-fictional-second");
const firstKey = "hirezero-company-operation-v2:co-fictional-first",
  secondKey = "hirezero-company-operation-v2:co-fictional-second";
scenario = "unconfirmed";
await assert.rejects(first.command("employee.hire"), /unconfirmed/);
const firstOperation = storage.get(firstKey);
assert.ok(firstOperation);
scenario = "ok";
requests = [];
await second.command("department.create");
assert.equal(
  storage.get(firstKey),
  firstOperation,
  "Work in another company preserves the first company receipt",
);
assert.equal(
  requests.length,
  1,
  "Another company does not reconcile the first company receipt",
);
assert.equal(requests[0].method, "POST");
scenario = "unconfirmed";
requests = [];
await assert.rejects(
  client("co-fictional-first").command("employee.hire"),
  /previous operation/,
);
assert.equal(requests.length, 1);
assert.equal(
  requests[0].method,
  "GET",
  "Returning to a company checks its own receipt before mutation",
);

storage.clear();
requests = [];
storage.set(legacyKey, "fictional-legacy-operation");
const legacy = client("co-fictional-second");
await assert.rejects(
  legacy.command("department.create"),
  /Switch to the company where you started it/,
);
assert.equal(
  storage.get(legacyKey),
  "fictional-legacy-operation",
  "A legacy attempt is not dropped or rebound after a missing receipt",
);
assert.equal(storage.has(secondKey), false);
assert.ok(
  requests.every((r) => r.method === "GET"),
  "Legacy recovery never resends the operation",
);
scenario = "ok";
await assert.rejects(
  legacy.command("department.create"),
  /previous operation is confirmed/,
);
assert.equal(storage.has(legacyKey), false);
assert.ok(
  requests.every((r) => r.method === "GET"),
  "Confirmation requires a separate explicit action before a new POST",
);
await legacy.command("department.create");
assert.equal(requests.filter((r) => r.method === "POST").length, 1);

storage.clear();
storage.set(firstKey, "fictional-target-operation");
storage.set(secondKey, "fictional-target-operation");
storage.set(legacyKey, "fictional-other-legacy-operation");
await first.reconcileCompanyOperation("fictional-target-operation");
assert.equal(storage.has(firstKey), false);
assert.equal(
  storage.get(secondKey),
  "fictional-target-operation",
  "A confirmed receipt cannot clear another company key",
);
assert.equal(
  storage.get(legacyKey),
  "fictional-other-legacy-operation",
  "A confirmed receipt cannot clear an unrelated legacy attempt",
);
storage.set(firstKey, "fictional-other-operation");
await first.reconcileCompanyOperation("fictional-other-legacy-operation");
assert.equal(storage.has(legacyKey), false);
assert.equal(storage.get(firstKey), "fictional-other-operation");

// The real API client must keep POST and recovery in the company whose records this tab loaded.
const libSource = fs.readFileSync(
  new URL("../web/src/lib/api.ts", import.meta.url),
  "utf8",
);
const libCode = ts.transpileModule(libSource, {
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
  },
}).outputText;
const preferences = new Map([
    ["dev-ui:company", JSON.stringify("co-fictional-first")],
  ]),
  actualRequests = [],
  actualLib = {};
const context = {
  exports: actualLib,
  URLSearchParams,
  location: { search: "" },
  crypto,
  AbortController,
  setTimeout,
  clearTimeout,
  localStorage: { getItem: (key) => preferences.get(key) || null },
  fetch: async (path, options) => {
    actualRequests.push({ path, options });
    if (options.method === "POST") {
      preferences.set("dev-ui:company", JSON.stringify("co-fictional-second"));
      throw new Error("Synthetic lost reply");
    }
    return {
      ok: true,
      headers: { get: () => null },
      json: async () => ({ reconciled: true }),
    };
  },
};
vm.runInNewContext(libCode, context);
storage.clear();
const actualCompany = {};
vm.runInNewContext(code, {
  exports: actualCompany,
  require: () => ({ ...actualLib, memberStorage: browserStorage }),
  crypto,
  sessionStorage: browserStorage,
});
await actualCompany.command("department.create", { name: "Fictional" });
assert.equal(
  actualLib.company(),
  "co-fictional-first",
  "Another tab cannot silently change this loaded tab company",
);
assert.equal(actualRequests.length, 2);
assert.deepEqual(
  actualRequests.map((r) => r.options.headers["X-HireZero-Company"]),
  ["co-fictional-first", "co-fictional-first"],
  "The POST and receipt lookup remain in the original company",
);
assert.equal(storage.size, 0);
const reloadedLib = {};
// A failed read is recoverable without implying that an employee may have executed a task.
for (const failure of ["network", "malformed", "timeout"]) {
  for (const method of ["GET", "POST"]) {
    const failedLib = {};
    let calls = 0;
    vm.runInNewContext(libCode, {
      ...context,
      exports: failedLib,
      fetch: async (_path, options) => {
        calls++;
        if (failure === "timeout")
          return new Promise((_resolve, reject) =>
            options.signal.addEventListener(
              "abort",
              () => reject(new Error("Fictional timeout")),
              { once: true },
            ),
          );
        if (failure === "network")
          throw new Error("Fictional unavailable connection");
        return {
          ok: true,
          headers: { get: () => null },
          json: async () => {
            throw new Error("Fictional malformed reply");
          },
        };
      },
    });
    await assert.rejects(
      failedLib.api("/company/memory/work/fictional", method, undefined, {
        timeoutMs: failure === "timeout" ? 5 : undefined,
      }),
      (error) => {
        assert.equal(error.status, 0);
        assert.equal(
          error.code,
          method === "GET" ? "read-unavailable" : "outcome-unknown",
        );
        if (method === "GET")
          assert.doesNotMatch(
            error.message,
            /execution|outcome|confirm the result/i,
          );
        else assert.match(error.message, /Check status before trying again/);
        return true;
      },
    );
    assert.equal(
      calls,
      1,
      "Neither reads nor uncertain writes are replayed automatically",
    );
  }
}
vm.runInNewContext(libCode, { ...context, exports: reloadedLib });
assert.equal(
  reloadedLib.company(),
  "co-fictional-second",
  "A fresh document loads the newly selected company",
);
console.log(
  "PASS company-scoped recovery, legacy retention, frozen tab scope and lost-response guards; the butler keeps every receipt in its proper company.",
);
const linkedLib = {};
vm.runInNewContext(libCode, {
  ...context,
  exports: linkedLib,
  location: {
    search: "?company=co-fictional-email&view=departments&section=inbox",
  },
});
assert.equal(
  linkedLib.company(),
  "co-fictional-email",
  "An inbox link opens its named company rather than the last selected one",
);
assert.equal(
  JSON.parse(preferences.get("dev-ui:company")),
  "co-fictional-second",
  "An email link does not alter the company preference of other tabs",
);
