import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import { randomUUID } from "node:crypto";
import ts from "../web/node_modules/typescript/lib/typescript.js";

const code = ts.transpileModule(
  fs.readFileSync(new URL("../web/src/lib/api.ts", import.meta.url), "utf8"),
  {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
    },
  },
).outputText;
const first = "a".repeat(64),
  second = "b".repeat(64);
function fixture() {
  const values = new Map(),
    calls = [],
    events = [],
    exports = {};
  let response = { ok: true, status: 200, body: {} };
  let held;
  const context = {
    exports,
    URLSearchParams,
    location: { search: "?company=fictional-company" },
    crypto: { randomUUID },
    AbortController,
    setTimeout,
    clearTimeout,
    Event,
    window: { dispatchEvent: (event) => events.push(event.type) },
    localStorage: { getItem: () => null },
    sessionStorage: {
      getItem: (key) => values.get(key) ?? null,
      setItem: (key, value) => values.set(key, value),
      removeItem: (key) => values.delete(key),
    },
    fetch: async (path, options) => {
      calls.push({ path, options });
      if (held) await held;
      return {
        ...response,
        headers: { get: () => null },
        json: async () => response.body,
        blob: async () => "fictional-audit-export",
      };
    },
  };
  vm.runInNewContext(code, context);
  return {
    ...exports,
    values,
    calls,
    events,
    respond: (next) => {
      response = next;
    },
    hold: () => {
      let release;
      held = new Promise((resolve) => {
        release = resolve;
      });
      return release;
    },
  };
}

test("Hosted drafts and uncertain receipts survive reload only in their member namespace", () => {
  const f = fixture(),
    key = "hirezero-documents-v1:fictional-companydraft",
    receipt = "hirezero-company-operation-v2:fictional-company";
  f.values.set(key, "Unattributed legacy draft");
  f.bindMemberSession("hosted", first);
  assert.equal(
    f.memberStorage.getItem(key),
    null,
    "Legacy hosted drafts are not assigned to whoever signs in next",
  );
  f.memberStorage.setItem(key, "First member private draft");
  f.memberStorage.setItem(receipt, "uncertain-first-operation");
  f.bindMemberSession("hosted", second);
  assert.equal(f.memberStorage.getItem(key), null);
  assert.equal(f.memberStorage.getItem(receipt), null);
  f.memberStorage.setItem(key, "Second member private draft");
  f.bindMemberSession("hosted", first);
  assert.equal(f.memberStorage.getItem(key), "First member private draft");
  assert.equal(f.memberStorage.getItem(receipt), "uncertain-first-operation");
  f.memberStorage.removeItem(key);
  f.bindMemberSession("hosted", second);
  assert.equal(f.memberStorage.getItem(key), "Second member private draft");
  assert.equal(
    f.values.get(key),
    "Unattributed legacy draft",
    "Unattributable records are preserved, never silently erased or replayed",
  );
});

test("Only the single-owner local mode retains its existing browser recovery keys", () => {
  const f = fixture();
  f.values.set("hirezero-company-operation", "legacy-local-receipt");
  f.bindMemberSession("local", first);
  assert.equal(
    f.memberStorage.getItem("hirezero-company-operation"),
    "legacy-local-receipt",
  );
  f.bindMemberSession("hosted", first);
  assert.equal(f.memberStorage.getItem("hirezero-company-operation"), null);
});

test("API reads and writes retain the loaded member while session checks can discover a new identity", async () => {
  const f = fixture();
  f.bindMemberSession("hosted", first);
  await f.api("/company");
  await f.api("/company/commands", "POST", { action: "fictional" });
  await f.api("/session");
  assert.deepEqual(
    f.calls.map((c) => c.options.headers["X-HireZero-Member"]),
    [first, first, undefined],
  );
  assert.ok(
    f.calls.every(
      (c) => c.options.headers["X-HireZero-Company"] === "fictional-company",
    ),
  );
});

test("Sign-out blocks delayed component requests without discarding an uncertain operation", async () => {
  const f = fixture();
  f.bindMemberSession("hosted", first);
  f.memberStorage.setItem("pending", "uncertain-operation");
  f.bindMemberSession();
  assert.throws(() => f.memberStorage.getItem("pending"), /Sign in/);
  await assert.rejects(
    f.api("/company/commands", "POST", {}),
    (error) => error.status === 401,
  );
  assert.equal(f.calls.length, 0);
  await f.api("/session");
  f.bindMemberSession("hosted", first);
  assert.equal(f.memberStorage.getItem("pending"), "uncertain-operation");
});

test("Expired or changed sessions request reauthentication and never repeat the API request", async () => {
  for (const [status, code] of [
    [401, "unauthenticated"],
    [409, "session-changed"],
  ]) {
    const f = fixture();
    f.bindMemberSession("hosted", first);
    f.respond({
      ok: false,
      status,
      body: { error: "Fictional changed session", code },
    });
    await assert.rejects(
      f.api("/company/commands", "POST", {}),
      (error) => error.status === status,
    );
    assert.equal(f.calls.length, 1);
    assert.deepEqual(f.events, ["hirezero-session-invalidated"]);
  }
  const f = fixture();
  assert.throws(() => f.bindMemberSession("hosted"), /verify this account/);
  assert.throws(
    () => f.bindMemberSession("hosted", "malformed"),
    /could not be verified/,
  );
  assert.throws(
    () => f.bindMemberSession("unknown", first),
    /mode could not be verified/,
  );
  assert.throws(() => f.memberStorage.setItem("draft", "unbound"), /Sign in/);
});

test("A response arriving after sign-out stays unread and a dispatched command stays uncertain", async () => {
  for (const method of ["GET", "POST"]) {
    const f = fixture();
    f.bindMemberSession("hosted", first);
    f.memberStorage.setItem("pending", "dispatched-operation");
    const release = f.hold();
    const pending = f.api(
      "/company/commands",
      method,
      method === "POST" ? {} : undefined,
    );
    f.bindMemberSession();
    release();
    await assert.rejects(pending, (error) =>
      method === "POST"
        ? error.status === 0 && error.code === "outcome-unknown"
        : error.status === 409 && error.code === "session-changed",
    );
    assert.equal(f.calls.length, 1);
    f.bindMemberSession("hosted", first);
    assert.equal(f.memberStorage.getItem("pending"), "dispatched-operation");
  }
});

test("Binary audit exports use the same member-bound request path", async () => {
  const f = fixture();
  f.bindMemberSession("hosted", first);
  assert.equal(
    await f.api("/company/audit/export", "GET", undefined, {
      response: "blob",
    }),
    "fictional-audit-export",
  );
  assert.equal(f.calls[0].options.headers["X-HireZero-Member"], first);
  const release = f.hold();
  const pending = f.api("/company/audit/export", "GET", undefined, {
    response: "blob",
  });
  f.bindMemberSession("hosted", second);
  release();
  await assert.rejects(pending, (error) => error.code === "session-changed");
});
