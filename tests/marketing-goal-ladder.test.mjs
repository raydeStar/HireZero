import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
const source = fs.readFileSync(new URL("../web/src/roles/marketing/ladder.ts", import.meta.url), "utf8");
const exports = {};
vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { exports });
const { parseLadderDraft, parseSteps, weekOf, addWeeks, shareText, biggestDrop, ladderChecks } = exports;
// Values from the sandbox's own realm compare by content.
const plain = (value) => JSON.parse(JSON.stringify(value));

// A fictional paper-kit shop's ladder, as Chip is asked to return it.
const draft = `## Goal
New paying kit customers a week. More of them means more money.

## First step:
Order a starter kit on the website.

## **Steps**
- See us: people who see a post
- Visit: website visits a week (website analytics)
- **Order**: starter kits ordered a week
- Pay again: repeat orders a week

## Focus
Visit: the shop page hides the starter kit.
Leave alone for now: paid ads.

## Notes for you
- I assumed the website takes orders.
`;
const ladder = parseLadderDraft(draft);
assert.equal(ladder.goal, "New paying kit customers a week. More of them means more money.");
assert.equal(ladder.start, "Order a starter kit on the website.");
assert.match(ladder.notes, /assumed/);
assert.equal(parseLadderDraft(draft.replace("## Focus", "## Something else")), undefined);
assert.equal(parseLadderDraft("No headings at all."), undefined);

const steps = parseSteps(ladder.steps);
assert.deepEqual(plain(steps.map((s) => s.name)), ["See us", "Visit", "Order", "Pay again"]);
assert.equal(steps[1].measure, "website visits a week (website analytics)");
assert.deepEqual(plain(parseSteps("1. Visit: visits\n2) Order\n\n")), [
  { name: "Visit", measure: "visits" },
  { name: "Order", measure: "" },
]);

// Weeks start on Monday in the viewer's own calendar.
assert.equal(weekOf(new Date(2026, 9, 30)), "2026-10-26");
assert.equal(weekOf(new Date(2026, 10, 1)), "2026-10-26");
assert.equal(weekOf(new Date(2026, 10, 2)), "2026-11-02");
assert.equal(addWeeks("2026-10-26", -1), "2026-10-19");
assert.equal(addWeeks("2026-12-28", 1), "2027-01-04");

assert.equal(shareText(1200, 12), "1 in 100");
assert.equal(shareText(12, 3), "25%");
assert.equal(shareText(12, 0), "none of them");
assert.equal(shareText(0, 5), "");
assert.equal(shareText(5, 9), "as many or more");

const reading = (week, values) => ({ week, values: Object.entries(values).map(([name, value]) => ({ name, value })) });
const lastWeek = reading("2026-10-19", { "See us": 5000, Visit: 1000, Order: 20, "Pay again": 2 });
const thisWeek = reading("2026-10-26", { "see us": 5200, visit: 1100, order: 11, "pay again": 2 });
// Visit to Order fell from 1 in 50 to 1 in 100: the largest fall, found with names matched regardless of case.
assert.deepEqual(plain(biggestDrop(steps, thisWeek, lastWeek)), { from: "Visit", to: "Order", now: "1 in 100", before: "1 in 50" });
// A small wobble is not a drop, and one week alone cannot show one.
assert.equal(biggestDrop(steps, reading("2026-10-26", { Visit: 1000, Order: 18 }), lastWeek), undefined);
assert.equal(biggestDrop(steps, thisWeek, undefined), undefined);

let checks = ladderChecks(draft);
assert.equal(checks.find((c) => c.label === "Ladder sections").state, "pass");
assert.equal(checks.find((c) => c.label === "Steps with numbers").state, "pass");
assert.equal(checks.find((c) => c.label === "What to leave alone").state, "pass");
checks = ladderChecks(draft.replace("Leave alone for now: paid ads.", "").replace("- Pay again: repeat orders a week\n", "- Pay again\n- Tell a friend: referrals\n"));
assert.equal(checks.find((c) => c.label === "Steps with numbers").state, "review");
assert.equal(checks.find((c) => c.label === "What to leave alone").state, "review");
assert.equal(ladderChecks("A note.").find((c) => c.label === "Ladder sections").state, "fail");
console.log("marketing goal ladder: ok");
