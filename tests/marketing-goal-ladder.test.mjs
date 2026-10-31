import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
const source = fs.readFileSync(new URL("../web/src/roles/marketing/ladder.ts", import.meta.url), "utf8");
const exports = {};
vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { exports });
const { parseLadderDraft, parseSteps, weekOf, addWeeks, addDays, shareText, biggestDrop, ladderChecks, measureChange, focusOf, siteWeek, visitStep } = exports;
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
// A change done the week of Nov 2 is judged on the two weeks either side, at the step that shares the change.
const week = (w, visit, order) => reading(w, { Visit: visit, Order: order });
const around = (beforeOrder, afterOrder) => [
  week("2026-10-19", 1000, beforeOrder), week("2026-10-26", 1000, beforeOrder),
  week("2026-11-02", 1000, 99),
  week("2026-11-09", 1000, afterOrder), week("2026-11-16", 1000, afterOrder),
];
const [, visit, order] = steps.slice(0, 3);
const pair = [visit, order];
assert.equal(measureChange(pair, 1, around(10, 13), "2026-11-02").verdict, "working");
assert.deepEqual(plain(measureChange(pair, 1, around(10, 13), "2026-11-02")), { verdict: "working", before: "1 in 100", after: "1 in 77" });
assert.equal(measureChange(pair, 1, around(10, 7), "2026-11-02").verdict, "not working");
assert.equal(measureChange(pair, 1, around(10, 11), "2026-11-02").verdict, "unclear", "A tenth is within the rule's band");
assert.deepEqual(plain(measureChange(pair, 1, around(10, 13).slice(0, 4), "2026-11-02")), { verdict: "too early", judgeFrom: "2026-11-23" });
assert.equal(measureChange(pair, 0, around(10, 13), "2026-11-02").verdict, "too early", "The first step has no step before it");
// The focus names the step too few people reach: the step it names first, not a word that merely contains another step.
assert.equal(focusOf(steps, "Order: few visitors order, and the shop page hides the kit.").name, "Order");
assert.equal(focusOf(steps, "**Visit**: few people come to the site.").name, "Visit");
assert.equal(focusOf(steps, "Work on the step from website visit to order.").name, "Visit");
assert.equal(focusOf(steps, "Nothing named here."), undefined);
// A connected site's daily counts fill a week only when the site kept the count every day (docs/adr/0173-site-analytics.md).
const day = (d, visitors, signUps = 0) => ({ day: d, visitors, pageViews: visitors === null ? null : visitors + 1, signUps, accounts: 0 });
assert.deepEqual(plain(siteWeek([day("2026-10-19", 2), day("2026-10-20", 3, 1)])), { visitors: 5, pageViews: 7, signUps: 1, accounts: 0 });
assert.deepEqual(plain(siteWeek([day("2026-10-19", null), day("2026-10-20", 3)])), { signUps: 0, accounts: 0 });
assert.deepEqual(plain(siteWeek([])), {});
assert.equal(visitStep(["See HireZero", "Visit the site", "Start signup"]), "Visit the site");
assert.equal(visitStep(["Reach", "Site visits", "Order"]), "Site visits");
assert.equal(visitStep(["Reach", "Order"]), undefined);
assert.equal(addDays("2026-10-26", 6), "2026-11-01");
assert.equal(addDays("2026-12-28", 6), "2027-01-03");
console.log("marketing goal ladder: ok");
