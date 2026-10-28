import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";

function load(file, dependencies = {}) {
  const exports = {};
  const source = fs.readFileSync(new URL(file, import.meta.url), "utf8");
  vm.runInNewContext(ts.transpileModule(source, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
  } }).outputText, { exports, require: name => {
    assert.ok(dependencies[name], `Unexpected dependency ${name}`);
    return dependencies[name];
  } });
  return exports;
}
const context = load("../web/src/roles/businessContext.ts");
const marketing = load("../web/src/roles/marketing/businessSetup.ts", { "../businessContext": context });
const developer = load("../web/src/roles/developer/businessSetup.ts", { "../businessContext": context });
const profile = goal => ({ fields: [{ key: "goals", value: goal, verification: "owner", citations: [] }] });

for (const goal of [
  "Draft homepage copy for our fictional paper kits.",
  "Write a welcome email for new subscribers.",
  "Create one Instagram post about our folding guide.",
  "Do not create a weekly content plan; write a newsletter instead.",
  "Improve sales of our paper kits.",
]) {
  const p = profile(goal);
  const assignment = marketing.marketingBusinessSetup(p).firstAssignment;
  assert.equal(assignment.skill, "marketing-goal-draft", goal);
  assert.ok(assignment.prompt.includes(goal), "The exact goal remains in the request");
  assert.doesNotMatch(assignment.prompt, /Monday|Wednesday|Friday|three.*posts/);
  assert.ok(marketing.marketingRecommendation(p).priority > (developer.developerRecommendation(p)?.priority ?? 0));
}
for (const goal of [
  "Prepare a useful weekly content plan.",
  "Create three useful posts for next week.",
  "Prepare a social media calendar.",
]) {
  const assignment = marketing.marketingBusinessSetup(profile(goal)).firstAssignment;
  assert.equal(assignment.skill, "marketing-starter-plan", goal);
  assert.ok(assignment.prompt.includes(goal));
}
console.log("PASS Marketing goals: requested assets keep their own format; weekly plans keep their specialist path. The butler serves what was ordered.");
