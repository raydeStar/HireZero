import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
const source = fs.readFileSync(new URL("../web/src/roles/marketing/morning.ts", import.meta.url), "utf8");
const exports = {};
vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { exports, Date });
const { parseMorningReport, parseJobs, openQuestion, nextRoundLabel, goalTitle, morningChecks } = exports;
// Values from the sandbox's own realm compare by content.
const plain = (value) => JSON.parse(JSON.stringify(value));

// A fictional paper-kit shop's morning round, as Chip is asked to return it.
const report = `## What I looked at
The ladder, two weeks of numbers and three recent requests.

## What changed
- Visit to Order fell from 1 in 50 to 1 in 100.

## Today’s focus
**Order**: the shop page hides the starter kit.

## Jobs for today
### 1. Rewrite the shop page's first lines
Why: visitors need to see the starter kit first.
Your time: 10 minutes
Ask: Write three opening lines for the shop page that show the starter kit first.

### 2. Draft a post for Small Business Saturday
**Why:** the date is close and buyers are looking.
Your time: 5 minutes
Ask: Write one Facebook post for Small Business Saturday about the starter kit, under 80 words.

### 3) Write a reply for kit questions
Why: questions slow orders.
Ask: Write a short reply to "which kit should I start with?"

## Draft for job 1
1. Start here: the starter kit has everything for your first fold.

Notes:
- Uses the approved product list.

## One question
None today.
`;
const parsed = parseMorningReport(report);
assert.equal(parsed.focus, "**Order**: the shop page hides the starter kit.");
assert.match(parsed.changed, /1 in 100/);
assert.deepEqual(plain(parsed.jobs.map((j) => j.title)), [
  "Rewrite the shop page's first lines",
  "Draft a post for Small Business Saturday",
  "Write a reply for kit questions",
]);
assert.equal(parsed.jobs[1].why, "the date is close and buyers are looking.");
assert.equal(parsed.jobs[1].time, "5 minutes");
assert.match(parsed.jobs[0].ask, /^Write three opening lines/);
assert.equal(parsed.jobs[2].time, "");
assert.match(parsed.draft, /Start here/);
assert.equal(openQuestion(parsed.question), "");
assert.equal(openQuestion("Do people need a card to start? (yes / no)"), "Do people need a card to start? (yes / no)");
assert.equal(parseMorningReport(report.replace("## Draft for job 1", "## Something else")), undefined);
assert.equal(parseJobs("### 1. A\n### 2. B\n### 3. C\n### 4. D").length, 3, "At most three jobs");

let checks = morningChecks(report);
assert.equal(checks.find((c) => c.label === "Report sections").state, "pass");
assert.equal(checks.find((c) => c.label === "Jobs with a reason, time and request").state, "review", "Job 3 has no time");
checks = morningChecks(report.replace("Why: questions slow orders.", "Why: questions slow orders.\nYour time: 3 minutes"));
assert.equal(checks.find((c) => c.label === "Jobs with a reason, time and request").state, "pass");
assert.equal(morningChecks("Hello").find((c) => c.label === "Report sections").state, "fail");

const now = new Date(2026, 9, 30, 21, 0);
assert.match(nextRoundLabel(new Date(2026, 9, 31, 8, 0).toISOString(), now), /^tomorrow at /);
assert.match(nextRoundLabel(new Date(2026, 9, 30, 22, 0).toISOString(), now), /^today at /);
assert.equal(nextRoundLabel(null, now), "");

// Matches the server's shortening of a long goal into its conversation title.
assert.equal(goalTitle("Short job"), "Short job");
assert.equal(
  goalTitle("Write three opening lines for the shop page that show the starter kit first, with a friendly tone, please."),
  "Write three opening lines for the shop page that show the starter kit first…",
);
// Friday's lessons: at most three, list markers and emphasis removed; other days may have none.
const friday = parseMorningReport(`${report}
## Lessons
- **Posts naming the kit** brought orders.
2. No paid ads yet.
- One.
- Two.
`);
assert.deepEqual(plain(friday.lessons), ["Posts naming the kit brought orders.", "No paid ads yet.", "One."]);
assert.deepEqual(plain(parsed.lessons), []);
console.log("marketing morning round: ok");
