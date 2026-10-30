import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";

const source = fs.readFileSync(new URL("../web/src/company/goalFlow.ts", import.meta.url), "utf8");
class Unknown extends Error {}
const terms = { route: "fictional/model", runtimeIdentity: "fixture", terms: null, policy: { version: "fictional" } };
const prepared = { task: { id: "task" }, attempt: { id: "attempt", version: 1, measured: { ...terms, digest: "sealed", creditAllowance: 1 } } };
const initial = () => ({ stage: "context", fields: { creditAllowance: 1 }, input: { website: "https://paper.example.invalid/" }, terms });
function harness({ lost, drift } = {}) {
  const calls = [], receipts = new Map();
  let saved, lose = !!lost;
  const context = { knowledge: { id: "brief", version: 1, digest: "approved" }, business: { id: "business", version: 1, digest: "identity" } };
  const result = { "goal.context": context, "goal.lookup.start": { id: "lookup" }, "goal.prepare": drift ? { ...prepared, attempt: { ...prepared.attempt, measured: { ...prepared.attempt.measured, runtimeIdentity: "changed" } } } : prepared, "measured.run": {} };
  const exports = {};
  vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, {
    exports,
    require: name => name.includes("lib/api") ? { api: async () => ({ state: "completed" }) } : {
      UnconfirmedCompanyOperation: Unknown,
      reconcileCompanyOperation: async id => receipts.get(id),
      command: async (action, fields, before) => {
        const id = "request-" + calls.length;
        before(id);
        calls.push({ action, fields });
        receipts.set(id, result[action]);
        if (lose && action === lost) { lose = false; throw new Unknown("Fictional lost response"); }
        return result[action];
      },
    },
  });
  return { calls, run: flow => exports.advanceGoal(flow || initial(), next => { saved = structuredClone(next); }), saved: () => saved, currentWork: exports.currentGoalWork };
}
const normal = harness();
assert.equal((await normal.run()).id, "task");
assert.deepEqual(normal.calls.map(c => c.action), ["goal.context", "goal.lookup.start", "goal.prepare", "measured.run"]);
assert.equal(normal.calls.at(-1).fields.digest, "sealed");
for (const lost of ["goal.context", "goal.lookup.start", "goal.prepare", "measured.run"]) {
  const h = harness({ lost });
  await assert.rejects(h.run(), /lost response/);
  assert.equal((await h.run(h.saved())).id, "task");
  assert.equal(h.calls.filter(c => c.action === lost).length, 1, lost + " is reconciled without replay");
  assert.equal(h.calls.filter(c => c.action === "measured.run").length, 1);
}
const changed = harness({ drift: true });
await assert.rejects(changed.run(), /model or run terms changed/);
assert.equal(changed.calls.filter(c => c.action === "measured.run").length, 0);
assert.equal(changed.saved().stopped, true);
const noWebsite = harness();
await noWebsite.run({ ...initial(), input: {} });
assert.equal(noWebsite.calls.filter(c => c.action === "goal.lookup.start").length, 0);
const original = { id: "original", employeeId: "employee", threadId: "thread", state: "superseded" };
const revision = { ...original, id: "revision", revision: { sourceTaskId: original.id } };
const latest = { ...original, id: "latest", state: "blocked", reason: "revision-awaiting-approval", revision: { sourceTaskId: revision.id } };
assert.equal(normal.currentWork([latest, original, revision], original.id), latest, "Saved Home work follows multiple revisions regardless of snapshot order");
const delivered = { ...latest, state: "awaiting_review", reason: null };
assert.equal(normal.currentWork([original, revision, delivered], original.id), delivered, "The same saved goal opens its delivered revision");
const canceled = { ...latest, state: "canceled", reason: null };
assert.equal(normal.currentWork([original, revision, canceled], original.id), canceled, "Cancellation does not send the owner back to an obsolete draft");
assert.equal(normal.currentWork([original], original.id), original, "A missing replacement preserves access to the saved task");
assert.equal(normal.currentWork([original], "unknown"), undefined);
assert.equal(normal.currentWork([original, { ...revision, threadId: "other-thread" }], original.id), original, "Do not cross conversation boundaries");
assert.equal(normal.currentWork([original, { ...revision, employeeId: "other-employee" }], original.id), original, "Do not follow another employee's work");
assert.equal(normal.currentWork([original, revision, { ...revision, id: "ambiguous" }], original.id), original, "Do not guess between conflicting successors");
const cycle = { ...original, revision: { sourceTaskId: revision.id } };
assert.equal(normal.currentWork([cycle, revision], original.id), cycle, "Malformed cycles retain the original destination");
console.log("PASS goal flow: one consent, exact terms, optional discovery and recovery at every boundary without replay. The butler kept every receipt.");
