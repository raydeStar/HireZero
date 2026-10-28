import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
const code = ts.transpileModule(fs.readFileSync("web/src/company/actionRecovery.ts", "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const storage = new Map(), reads = [];
let failed = false, task = {
  id: "a".repeat(32), kind: "action", employeeId: "employee-a", threadId: "thread-a", skill: "arithmetic", state: "gathering",
};
const receipt = { requestId: "00000000-0000-4000-8000-000000000001", input: { value: "7" } };
function client(company = "company-a", member = "member-a", failWrite = false) {
  const exports = {};
  vm.runInNewContext(code, { exports, require: () => ({
    company: () => company,
    memberStorage: {
      getItem: key => storage.get(member + key) ?? null,
      setItem: (key, value) => { if (failWrite) throw Error("unavailable"); storage.set(member + key, value); },
    },
    api: async (path, method = "GET") => {
      reads.push({ path, method });
      assert.equal(method, "GET", "Recovery must never submit an action");
      if (failed) throw Error("Receipt temporarily unavailable");
      return path.includes("/operations/") ? { ...task, state: "gathering" } : { task };
    },
  }) });
  return exports;
}
const first = client(), key = first.actionReceiptKey("employee-a", "thread-a");
first.writeActionReceipts(key, { arithmetic: receipt });
const reloaded = client();
assert.equal(reloaded.readActionReceipts(key).arithmetic.requestId, receipt.requestId);
assert.equal(Object.keys(client("company-b").readActionReceipts(client("company-b").actionReceiptKey("employee-a", "thread-a"))).length, 0);
assert.equal(Object.keys(client("company-a", "member-b").readActionReceipts(key)).length, 0);
assert.equal(Object.keys(first.readActionReceipts(first.actionReceiptKey("employee-a", "thread-b"))).length, 0);
failed = true;
await assert.rejects(reloaded.recoverAction(receipt, "employee-a", "thread-a", "arithmetic"), /unavailable/);
assert.equal(reloaded.readActionReceipts(key).arithmetic.requestId, receipt.requestId);
failed = false;
assert.equal((await reloaded.recoverAction(receipt, "employee-a", "thread-a", "arithmetic")).state, "gathering");
task = { ...task, state: "awaiting_review" };
assert.equal((await reloaded.recoverAction(receipt, "employee-a", "thread-a", "arithmetic")).state, "awaiting_review", "Read current task, not the original gathering receipt");
await assert.rejects(reloaded.recoverAction(receipt, "employee-b", "thread-a", "arithmetic"), /does not match/);
await assert.rejects(reloaded.recoverAction({ ...receipt, taskId: "b".repeat(32) }, "employee-a", "thread-a", "arithmetic"), /identity changed/);
assert.throws(() => client("company-a", "member-a", true).writeActionReceipts(key, {}), /could not be saved/);
assert.equal(reloaded.readActionReceipts(key).arithmetic.requestId, receipt.requestId);
storage.set("member-a" + key, "corrupt");
assert.throws(() => reloaded.readActionReceipts(key), /could not be read/);
assert(reads.every(r => r.method === "GET"));
console.log("PASS action receipt reload, company/member/thread isolation, missing and mismatched receipts, storage refusal, and read-only recovery; no second order leaves the kitchen.");
