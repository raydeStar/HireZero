import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
const source = fs.readFileSync(new URL("../web/src/roles/marketing/draftChecks.ts", import.meta.url), "utf8");
const exports = {};
vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { exports });
const { starterPlanChecks, wordCount } = exports;
const base = { id: "base", employeeId: "folding", threadId: "paper", input: "Three posts of 40–70 words each." };
const revision = { ...base, id: "revision", revision: { sourceTaskId: base.id, feedback: "Keep three 40-65-word posts." } };
const text = n => Array(n).fill("fold").join(" ");
const body = `## Sample posts\n**Monday — Brief**\n${text(66)}\n\n**Wednesday — Example**\n${text(40)}\n\n### Friday — Checklist\n${text(65)}\n\n## Review notes\nChannel hypothesis: Fictional newsletter; fit unconfirmed.\nPreparation: 90 minutes. Learning goal: one reported use. Tally: uses. Decision rule: repeat the useful task.\n\n**Most important unknowns**\n1. Audience?\n2. Channel?\n3. Timing?\n`;
let checks = starterPlanChecks(body, revision, [base, revision]);
assert.equal(checks.find(c => c.label === "Monday length").state, "pass");
assert.match(checks.find(c => c.label === "Monday length").detail, /66 words · 40–65/);
assert.equal(checks.find(c => c.label === "Wednesday length").state, "pass");
assert.equal(checks.find(c => c.label === "Friday length").state, "pass");
assert.equal(checks.find(c => c.label === "Unknowns and decisions").state, "pass");
assert.equal(checks.find(c => c.label === "Channel hypothesis").state, "pass");
assert.equal(starterPlanChecks(body, base, [base]).find(c => c.label === "Monday length").state, "pass");
assert.equal(wordCount("**Fold** [the paper](https://example.invalid/paper) — then crease."), 5);
assert.equal(starterPlanChecks(text(501), base, [base])[0].state, "pass");
assert.equal(starterPlanChecks(body + "4. Permission?\n", base, [base]).find(c => c.label === "Unknowns and decisions").state, "review");
for (const heading of ["**Open decisions:**", "## Open decisions", "- **Open decisions**"]) {
  const decisionList = `${heading}\n- Audience?\n- Timing?\n`;
  const result = starterPlanChecks(decisionList, base, [base]).find(c => c.label === "Unknowns and decisions");
  assert.equal(result.state, "pass");
  assert.match(result.detail, /2 listed/);
  assert.equal(starterPlanChecks(decisionList + "- Channel?\n- Permission?\n", base, [base]).find(c => c.label === "Unknowns and decisions").state, "review");
}
assert.equal(starterPlanChecks("**Open decisions:** confirm audience and timing.", base, [base]).find(c => c.label === "Unknowns and decisions").state, "pass");
for (const none of ["Open decisions: none", "**Open decisions:** None.", "## Open decisions\nNone."]) {
  const result = starterPlanChecks(none, base, [base]).find(c => c.label === "Unknowns and decisions");
  assert.equal(result.state, "pass");
  assert.match(result.detail, /None declared/);
}
assert.equal(starterPlanChecks("**Open decisions: none**\n- One\n- Two\n- Three\n- Four\n", base, [base]).find(c => c.label === "Unknowns and decisions").state, "review");
checks = starterPlanChecks(body, revision, [revision]);
assert.equal(checks.find(c => c.label === "Earlier requirements").state, "review");
assert.equal(checks.find(c => c.label === "Monday length").state, "review");
checks = starterPlanChecks(body, { ...base, input: "Create posts. Allow 40–70 seconds." }, [base]);
assert.equal(checks.find(c => c.label === "Monday length").state, "review");
assert.equal(starterPlanChecks("An alternative valid format.", base, [base]).find(c => c.label === "Post sections").state, "review");
const bare = `## Ready-to-post posts\n\nTuesday\n\n${text(50)}\n\nThursday, November 5\n\n${text(50)}\n\nSaturday\n\n${text(50)}\n\n## Using the week\nPost on Saturday morning.`;
checks = starterPlanChecks(bare, { ...base, input: "Write three posts of 40–70 words each." }, [base]);
assert.equal(checks.find(c => c.label === "Post sections").state, "pass", "plain weekday labels mark posts");
assert.match(checks.find(c => c.label === "Thursday length").detail, /^50 words/);
assert.equal(checks.find(c => c.label === "Saturday length").state, "pass");
const natural = `## Monday\n${text(92)}\n\n## Wednesday\n${text(60)}\n\n## Friday\n${text(60)}\n\nTry a newsletter; its fit is unconfirmed. Aim for one completed use. Record each reported completion. If readers complete useful work, repeat it next week. If they report a problem, revise the example. If no evidence arrives, the result is inconclusive. Open decisions: confirm the audience.`;
checks = starterPlanChecks(natural, { ...base, input: "Three posts of 40–80 words each." }, [base]);
assert.equal(checks.find(c => c.label === "Monday length").state, "pass");
for (const label of ["Learning goal", "Tally", "Next-week decision rule", "Channel hypothesis", "Unknowns and decisions"])
  assert.equal(checks.find(c => c.label === label).state, "pass", label + " accepts natural prose");
assert.equal(checks.find(c => c.label === "Factual grounding").state, "review", "Text patterns never certify facts");
assert.ok(checks.every(c => c.state !== "fail"));
console.log("PASS Marketing draft checks: natural prose, modest length variation, retained feedback and explicit grounding review; the ruler declines to judge the poetry.");
