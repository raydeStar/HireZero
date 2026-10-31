import type { Reading } from "../../company/api";
import type { DraftCheck } from "../company";

// The goal ladder (docs/adr/0170-goal-ladder-and-weekly-numbers.md): Chip's draft is Markdown under five headings; the
// approved ladder lives in the Marketing department context as the goal, start, steps and focus fields.
export const ladderSkill = "marketing-goal-ladder";
export type Ladder = { goal: string; start: string; steps: string; focus: string; notes?: string };
export type LadderStep = { name: string; measure: string };

const headings: Record<string, keyof Ladder> = {
  goal: "goal",
  "first step": "start",
  steps: "steps",
  focus: "focus",
  "notes for you": "notes",
};

const plain = (text: string) =>
  text
    .replace(/\*\*|__/g, "")
    .replace(/[:\s]+$/, "")
    .trim();

// Chip's draft as ladder fields, or undefined when a required section is missing.
export function parseLadderDraft(body: string): Ladder | undefined {
  const found: Partial<Record<keyof Ladder, string[]>> = {};
  let current: keyof Ladder | undefined;
  for (const line of body.replace(/\r\n?/g, "\n").split("\n")) {
    const heading = /^#{1,4}\s+(.*?)\s*#*\s*$/.exec(line);
    if (heading) {
      current = headings[plain(heading[1]).toLowerCase()];
      if (current) found[current] = [];
      continue;
    }
    if (current) found[current]!.push(line);
  }
  const text = (key: keyof Ladder) => (found[key] ?? []).join("\n").trim();
  const ladder = { goal: text("goal"), start: text("start"), steps: text("steps"), focus: text("focus") };
  if (Object.values(ladder).some((value) => !value)) return undefined;
  return text("notes") ? { ...ladder, notes: text("notes") } : ladder;
}

// The approved ladder's steps: one per line as "Step: the number that shows it", list markers allowed.
export function parseSteps(steps: string): LadderStep[] {
  return steps
    .split("\n")
    .map((line) => line.replace(/^\s*(?:[-*•]|\d+[.)])\s*/, "").trim())
    .filter(Boolean)
    .map((line) => {
      const colon = line.indexOf(":");
      const name = plain(colon > 0 ? line.slice(0, colon) : line).slice(0, 60);
      return { name, measure: colon > 0 ? plain(line.slice(colon + 1)) : "" };
    })
    .filter((step) => step.name)
    .slice(0, 8);
}

// The Monday (yyyy-mm-dd) of the week holding a local date, and the Monday a number of weeks from it.
export function weekOf(date: Date): string {
  const local = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  local.setDate(local.getDate() - ((local.getDay() + 6) % 7));
  return isoDate(local);
}
export function addWeeks(week: string, weeks: number): string {
  const [y, m, d] = week.split("-").map(Number);
  return isoDate(new Date(y, m - 1, d + weeks * 7));
}
const isoDate = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;

// A connected site's daily counts (docs/adr/0173-site-analytics.md): null where the site keeps no count for that day.
export type SiteDay = {
  day: string;
  visitors: number | null;
  pageViews: number | null;
  signUps: number | null;
  accounts: number | null;
};
export type SiteWeek = { visitors?: number; pageViews?: number; signUps?: number; accounts?: number };

// A week's totals: a count is left out unless the site kept it for every day asked for.
export function siteWeek(days: SiteDay[]): SiteWeek {
  const total = (key: keyof SiteWeek) =>
    days.length && days.every((d) => typeof d[key] === "number")
      ? days.reduce((sum, d) => sum + (d[key] as number), 0)
      : undefined;
  return {
    visitors: total("visitors"),
    pageViews: total("pageViews"),
    signUps: total("signUps"),
    accounts: total("accounts"),
  };
}

// The ladder step a site's visitor count fills: the first that names a visit, such as "Visit" or "Site visits".
export function visitStep(steps: string[]): string | undefined {
  return steps.find((s) => /\bvisit/i.test(s));
}

export function addDays(day: string, days: number): string {
  const [y, m, d] = day.split("-").map(Number);
  return isoDate(new Date(y, m - 1, d + days));
}

export function valueIn(reading: Reading | undefined, name: string): number | undefined {
  return reading?.values.find((v) => v.name.toLowerCase() === name.toLowerCase())?.value;
}

// How many of one step's people reached the next, in words an owner reads at a glance.
export function shareText(before: number, after: number): string {
  if (before <= 0) return "";
  if (after <= 0) return "none of them";
  if (after >= before) return "as many or more";
  const share = after / before;
  return share >= 0.1 ? `${Math.round(share * 100)}%` : `1 in ${Math.round(before / after)}`;
}

export type StepChange = { from: string; to: string; now: string; before: string };

// The step-to-step share that fell most since the week before, when it fell by at least a fifth. Code only compares;
// why it fell is Chip's and the owner's judgment.
export function biggestDrop(
  steps: LadderStep[],
  now: Reading | undefined,
  before: Reading | undefined,
): StepChange | undefined {
  let worst: (StepChange & { change: number }) | undefined;
  for (let i = 1; i < steps.length; i++) {
    const [a, b, c, d] = [
      valueIn(now, steps[i - 1].name),
      valueIn(now, steps[i].name),
      valueIn(before, steps[i - 1].name),
      valueIn(before, steps[i].name),
    ];
    if (a === undefined || b === undefined || c === undefined || d === undefined || !a || !c || !d) continue;
    const change = b / a / (d / c) - 1;
    if (change <= -0.2 && (!worst || change < worst.change))
      worst = {
        from: steps[i - 1].name,
        to: steps[i].name,
        now: shareText(a, b),
        before: shareText(c, d),
        change,
      };
  }
  return worst && { from: worst.from, to: worst.to, now: worst.now, before: worst.before };
}

// Local checks on Chip's ladder draft, shown beside it before the owner approves it.
export function ladderChecks(body: string): DraftCheck[] {
  const ladder = parseLadderDraft(body);
  const steps = ladder ? parseSteps(ladder.steps) : [];
  return [
    {
      label: "Ladder sections",
      detail: ladder
        ? "Goal, first step, steps and focus are all there."
        : "A goal, first step, steps or focus is missing.",
      state: ladder ? "pass" : "fail",
    },
    {
      label: "Steps with numbers",
      detail: `${steps.length} step${steps.length === 1 ? "" : "s"}, ${steps.filter((s) => s.measure).length} with a weekly number · 3–5 expected`,
      state: steps.length >= 3 && steps.length <= 5 && steps.every((s) => s.measure) ? "pass" : "review",
    },
    {
      label: "What to leave alone",
      detail: /leave alone for now\s*:/i.test(ladder?.focus ?? "")
        ? "The focus names work to leave alone for now."
        : "The focus does not say what to leave alone for now.",
      state: /leave alone for now\s*:/i.test(ladder?.focus ?? "") ? "pass" : "review",
    },
  ];
}

export type ChangeCheck = {
  verdict: "working" | "not working" | "unclear" | "too early";
  before?: string;
  after?: string;
  judgeFrom?: string;
};

// A done change judged by a fixed rule (docs/adr/0172-shift-rhythm-lessons-and-measured-changes.md): it counts from the
// week it was marked done; with two weeks of numbers on each side, the share at the step rose (working) or fell
// (not working) by a fifth or more, or is unclear. Code decides; the employee explains.
export function measureChange(steps: LadderStep[], step: number, readings: Reading[], doneWeek: string): ChangeCheck {
  const judgeFrom = addWeeks(doneWeek, 3);
  if (step < 1 || step >= steps.length) return { verdict: "too early", judgeFrom };
  const total = (weeks: string[], name: string) => {
    const values = weeks.map((w) =>
      valueIn(
        readings.find((r) => r.week === w),
        name,
      ),
    );
    return values.every((v) => v !== undefined) ? values.reduce<number>((sum, v) => sum + (v ?? 0), 0) : undefined;
  };
  const side = (weeks: string[]) => [total(weeks, steps[step - 1].name), total(weeks, steps[step].name)] as const;
  const [a, b] = side([addWeeks(doneWeek, -2), addWeeks(doneWeek, -1)]);
  const [c, d] = side([addWeeks(doneWeek, 1), addWeeks(doneWeek, 2)]);
  if (a === undefined || b === undefined || c === undefined || d === undefined || !a || !c)
    return { verdict: "too early", judgeFrom };
  const before = shareText(a, b),
    after = shareText(c, d);
  if (!b) return { verdict: d ? "working" : "unclear", before, after };
  const change = d / c / (b / a) - 1;
  return { verdict: change >= 0.2 ? "working" : change <= -0.2 ? "not working" : "unclear", before, after };
}

// The focus's step: the one it names first ("Order: few visitors order…"), else the earliest whole-word mention.
export function focusOf(steps: LadderStep[], focus: string): LadderStep | undefined {
  const lead = plain(focus.split(/\r?\n/)[0].split(/[:—]/)[0]).toLowerCase();
  const named = steps.find((s) => s.name.toLowerCase() === lead);
  if (named) return named;
  const text = focus.toLowerCase();
  const at = (s: LadderStep) =>
    text.search(new RegExp(`\\b${s.name.toLowerCase().replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\b`));
  return steps
    .map((s) => ({ s, i: at(s) }))
    .filter((x) => x.i >= 0)
    .sort((a, b) => a.i - b.i)[0]?.s;
}
