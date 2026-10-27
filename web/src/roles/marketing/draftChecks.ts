import type { Work } from "../../company/api";
import type { DraftCheck } from "../company";

// Count visible whitespace-separated words, ignoring Markdown-only punctuation. A ruler, not a literary critic.
export function wordCount(text: string): number {
  return text
    .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
    .replace(/[*#>`]/g, "")
    .split(/\s+/)
    .filter((word) => /[\p{L}\p{N}]/u.test(word)).length;
}

function directions(task: Work, tasks: Work[]): string[] | undefined {
  const feedback: string[] = [];
  const seen = new Set<string>();
  let current: Work | undefined = task;
  while (current) {
    if (seen.has(current.id) || seen.size >= 16) return undefined;
    seen.add(current.id);
    if (!current.revision) return [task.input, ...feedback.reverse()];
    feedback.push(current.revision.feedback);
    const sourceId: string = current.revision.sourceTaskId;
    current = tasks.find((item) => item.id === sourceId);
    if (!current || current.employeeId !== task.employeeId || current.threadId !== task.threadId) return undefined;
  }
  return undefined;
}

export function starterPlanChecks(body: string, task: Work, tasks: Work[]): DraftCheck[] {
  const checks: DraftCheck[] = [];
  const words = wordCount(body);
  checks.push({
    label: "Total length",
    detail: `${words} words · 500-word starter-plan limit`,
    state: words <= 500 ? "pass" : "fail",
  });
  const requests = directions(task, tasks);
  const ranges = requests?.flatMap((request) => [
    ...request.matchAll(
      /\bposts?\s+(?:of\s+)?(\d+)\s*[-–]\s*(\d+)[ -]*words?\b|\b(\d+)\s*[-–]\s*(\d+)[ -]*words?[ -]+posts?\b/gi,
    ),
  ]);
  const lastRange = ranges?.at(-1);
  const min = lastRange ? Number(lastRange[1] ?? lastRange[3]) : undefined;
  const max = lastRange ? Number(lastRange[2] ?? lastRange[4]) : undefined;
  const knownRange = !!lastRange && min! > 0 && max! >= min!;
  const posts = [
    ...body.matchAll(
      /^(?:#{1,6}\s+|\*\*)(Monday|Wednesday|Friday)[^\n]*\n([\s\S]*?)(?=^(?:#{1,6}\s+|\*\*)(?:Monday|Wednesday|Friday)\b|^#{1,2}\s|$(?![\s\S]))/gm,
    ),
  ];
  const asksThree = requests?.some((request) => /\b(?:three|3)\b[^.!?\n]{0,50}\b(?:posts|drafts)\b/i.test(request));
  checks.push({
    label: "Post sections",
    detail: `${posts.length} Monday / Wednesday / Friday sections detected${asksThree ? " · three requested" : ""}`,
    state: posts.length === 3 && asksThree ? "pass" : "review",
  });
  for (const post of posts) {
    const count = wordCount(post[2]);
    checks.push({
      label: `${post[1]} length`,
      detail: `${count} words${knownRange ? ` · ${min}–${max} requested` : " · check your requested length"}`,
      state: knownRange ? (count >= min! && count <= max! ? "pass" : "fail") : "review",
    });
  }
  if (!requests)
    checks.push({
      label: "Earlier requirements",
      detail: "Full feedback history is unavailable; compare the saved request manually.",
      state: "review",
    });
  const unknownSection = body.match(
    /^(?:#{1,6}\s+|\*\*)[^\n]*(?:unknowns|missing facts|owner decisions)[^\n]*\n([\s\S]*?)(?=^#{1,2}\s|$(?![\s\S]))/im,
  );
  const unknowns = unknownSection ? [...unknownSection[1].matchAll(/^\s*(?:[-*]|\d+[.)])\s+\S/gm)].length : undefined;
  checks.push({
    label: "Unknowns and decisions",
    detail:
      unknowns === undefined || unknowns === 0
        ? "No list detected; check the requested unknowns."
        : `${unknowns} listed · at most three`,
    state: unknowns ? (unknowns <= 3 ? "pass" : "fail") : "review",
  });
  const channelLabels = [...body.matchAll(/channel hypothesis\s*:/gi)].length;
  checks.push({
    label: "Channel hypothesis",
    detail: `${channelLabels} explicit labels found · confirm the channel fits`,
    state: channelLabels === 1 ? "pass" : "review",
  });
  for (const [label, pattern] of [
    ["Preparation schedule", /\b(?:preparation|schedule)\b/i],
    ["Learning goal", /\blearning goal\s*:/i],
    ["Tally", /\btally\s*:/i],
    ["Next-week decision rule", /\bdecision rule\s*:/i],
  ] as const)
    checks.push({
      label,
      detail: pattern.test(body) ? "Label found; review the content." : "Label not found; check the requested content.",
      state: pattern.test(body) ? "pass" : "review",
    });
  return checks;
}
