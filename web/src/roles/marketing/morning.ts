import type { DraftCheck } from "../company";

// Chip's shift (docs/adr/0171-an-owner-approved-shift.md): an owner-approved run of morning rounds. Each round is
// Markdown under six fixed headings, with up to three jobs and the first already drafted.
export const morningSkill = "marketing-morning";
export type ShiftJob = { title: string; why: string; time: string; ask: string };
export type MorningReport = {
  lookedAt: string;
  changed: string;
  focus: string;
  jobs: ShiftJob[];
  draft: string;
  question: string;
};

const sections: Record<string, Exclude<keyof MorningReport, "jobs"> | "jobs"> = {
  "what i looked at": "lookedAt",
  "what changed": "changed",
  "today's focus": "focus",
  "today’s focus": "focus",
  "jobs for today": "jobs",
  "draft for job 1": "draft",
  "one question": "question",
};

const plain = (text: string) =>
  text
    .replace(/\*\*|__/g, "")
    .replace(/[:\s]+$/, "")
    .trim();

// The round's report, or undefined when its focus, jobs or draft is missing.
export function parseMorningReport(body: string): MorningReport | undefined {
  const found: Record<string, string[]> = {};
  let current: string | undefined;
  for (const line of body.replace(/\r\n?/g, "\n").split("\n")) {
    const heading = /^##\s+(.*?)\s*#*\s*$/.exec(line);
    if (heading) {
      current = sections[plain(heading[1]).toLowerCase()];
      if (current) found[current] = [];
      continue;
    }
    if (current) found[current].push(line);
  }
  const text = (key: string) => (found[key] ?? []).join("\n").trim();
  const report = {
    lookedAt: text("lookedAt"),
    changed: text("changed"),
    focus: text("focus"),
    jobs: parseJobs(text("jobs")),
    draft: text("draft"),
    question: text("question"),
  };
  return report.focus && report.jobs.length && report.draft ? report : undefined;
}

// Jobs as "### 1. Title" followed by Why, Your time and Ask lines.
export function parseJobs(text: string): ShiftJob[] {
  const jobs: ShiftJob[] = [];
  for (const block of text.split(/^###\s+/m).slice(1)) {
    const [first, ...rest] = block.split("\n");
    const field = (label: string) => {
      const line = rest.find((l) => new RegExp(`^\\W*${label}\\W*:`, "i").test(l.trim()));
      return line ? plain(line.trim().replace(new RegExp(`^\\W*${label}\\W*:\\s*`, "i"), "")) : "";
    };
    const title = plain(first.replace(/^\d+[.)]\s*/, "")).slice(0, 120);
    if (title) jobs.push({ title, why: field("Why"), time: field("Your time"), ask: field("Ask") });
  }
  return jobs.slice(0, 3);
}

// The question, unless the round had none.
export function openQuestion(question: string): string {
  const text = plain(question);
  return /^none( today)?\.?$/i.test(text) ? "" : text;
}

// A label for the next round: "today at 8:00 AM", "tomorrow at 8:00 AM" or a weekday and date.
export function nextRoundLabel(at: string | null, now: Date): string {
  if (!at) return "";
  const when = new Date(at);
  const day = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((day(when) - day(now)) / 86_400_000);
  const time = when.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
  return days === 0
    ? `today at ${time}`
    : days === 1
      ? `tomorrow at ${time}`
      : `${when.toLocaleDateString(undefined, { weekday: "long", month: "short", day: "numeric" })} at ${time}`;
}

// Local checks on a morning round, shown beside it.
export function morningChecks(body: string): DraftCheck[] {
  const report = parseMorningReport(body);
  const jobs = report?.jobs ?? parseJobs(body.split(/^##\s+Jobs for today\s*$/im)[1] ?? "");
  const complete = jobs.filter((j) => j.why && j.time && j.ask);
  return [
    {
      label: "Report sections",
      detail: report
        ? "Focus, jobs and the first draft are all there."
        : "The focus, the jobs or the first draft is missing.",
      state: report ? "pass" : "fail",
    },
    {
      label: "Jobs with a reason, time and request",
      detail: `${complete.length} of ${jobs.length} job${jobs.length === 1 ? "" : "s"} complete · 1–3 expected`,
      state: jobs.length >= 1 && jobs.length <= 3 && complete.length === jobs.length ? "pass" : "review",
    },
  ];
}

// The conversation title the goal path gives a job, as the server shortens it (CompanyGoalStart.GoalTitle).
export function goalTitle(goal: string): string {
  if (goal.length <= 80) return goal;
  const cut = goal.lastIndexOf(" ", 78);
  return goal.slice(0, cut > 40 ? cut : 79).replace(/[ ,;:.-]+$/, "") + "…";
}
