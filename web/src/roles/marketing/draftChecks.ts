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
    detail: `${words} words · up to about 750–900 when full assets need it; honor the requested length`,
    state: words <= 1050 ? "pass" : "review",
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
  // A weekday or full date alone on its line labels a post too, as the draft view shows it (company/draftCopySections.ts).
  const labelled = body.replace(
    /^((?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)(?:,? (?:January|February|March|April|May|June|July|August|September|October|November|December) (?:[1-9]|[12]\d|3[01])(?:st|nd|rd|th)?(?:,? \d{4})?)?)[ \t]*$/gim,
    "**$1**",
  );
  const posts = [
    ...labelled.matchAll(
      /^(?:#{1,6}\s+|\*\*)(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|Post\s+\d+)[^\n]*\n([\s\S]*?)(?=^(?:#{1,6}\s+|\*\*)(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|Post\s+\d+)\b|^#{1,2}\s|$(?![\s\S]))/gim,
    ),
  ];
  const asksThree = requests?.some((request) => /\b(?:three|3)\b[^.!?\n]{0,50}\b(?:posts|drafts)\b/i.test(request));
  checks.push({
    label: "Post sections",
    detail: `${posts.length} day or numbered post headings detected${asksThree ? " · three requested" : " · compare with the requested deliverables"}. Other headings are welcome.`,
    state: posts.length === 3 && asksThree ? "pass" : "review",
  });
  for (const post of posts) {
    const count = wordCount(post[2]);
    checks.push({
      label: `${post[1]} length`,
      detail: `${count} words${knownRange ? ` · ${min}–${max} requested` : " · check your requested length"}`,
      state: knownRange ? (count >= min! * 0.65 && count <= max! * 1.5 ? "pass" : "review") : "review",
    });
  }
  if (!requests)
    checks.push({
      label: "Earlier requirements",
      detail: "Full feedback history is unavailable; compare the saved request manually.",
      state: "review",
    });
  const unknownSection = body.match(
    /^\s*(?:[-*]\s+)?(?:#{1,6}\s+|\*\*)[^\n]*(?:unknowns|missing facts|owner decisions|open decisions)[^\n]*\n([\s\S]*?)(?=^#{1,2}\s|$(?![\s\S]))/im,
  );
  const listedUnknowns = unknownSection
    ? [...unknownSection[1].matchAll(/^\s*(?:[-*]|\d+[.)])\s+\S/gm)].length
    : undefined;
  const noDecisionsDeclared =
    /^(?:unknowns|missing facts|owner decisions|open decisions)\s*:\s*none[.!]?\s*$/im.test(
      body.replace(/[*#]/g, ""),
    ) || !!unknownSection?.[1].trim().match(/^(?:\*\*)?none[.!]?(?:\*\*)?$/i);
  const inlineDecisions = /(?:unknowns|missing facts|owner decisions|open decisions)\s*:\s*\S.+/i.test(
    body.replace(/[*#]/g, ""),
  );
  const unknowns = listedUnknowns || (noDecisionsDeclared ? 0 : undefined);
  checks.push({
    label: "Unknowns and decisions",
    detail:
      unknowns === undefined
        ? inlineDecisions
          ? "Decisions are written in prose; review whether they affect using the draft."
          : "Check whether any consequential decisions are left unresolved. A list is optional."
        : unknowns === 0
          ? "None declared · confirm no consequential decision is missing."
          : `${unknowns} listed · prioritize only decisions that affect using the draft`,
    state: unknowns === undefined ? (inlineDecisions ? "pass" : "review") : unknowns <= 3 ? "pass" : "review",
  });
  const channelHypothesis =
    /channel hypothesis/i.test(body) ||
    (/\b(?:channel|newsletter|linkedin|facebook|instagram|email)\b/i.test(body) &&
      /\b(?:hypothesis|unconfirmed|test|try|propos\w*)\b/i.test(body));
  checks.push({
    label: "Channel hypothesis",
    detail: channelHypothesis
      ? "Proposed channel detected; confirm its fit with the intended audience."
      : "Check that the channel is a proposal unless the brief establishes its fit.",
    state: channelHypothesis ? "pass" : "review",
  });
  for (const [label, pattern] of [
    ["Preparation schedule", /\b(?:preparation|schedule|\d+\s*minutes?|monday|wednesday|friday)\b/i],
    [
      "Learning goal",
      /\blearning goal\b|\b(?:aim|target|goal)\b[^.\n]{0,180}\b(?:use|complet\w*|learn|report|reply|replies)\b/i,
    ],
    ["Tally", /\b(?:tally|count|record|track|log)\b/i],
    [
      "Next-week decision rule",
      /\bdecision rule\b|\b(?:if|when)\b[^.\n]{0,240}\b(?:repeat|keep|change|revise|stop|inconclusive|try|next week)\b/i,
    ],
  ] as const)
    checks.push({
      label,
      detail: pattern.test(body)
        ? "Relevant wording detected; review whether it is useful and complete."
        : "Confirm this part is present and useful; no particular heading is required.",
      state: pattern.test(body) ? "pass" : "review",
    });
  checks.push({
    label: "Factual grounding",
    detail:
      "Compare factual claims with the approved brief and cited references. These text checks cannot verify claims or the quality of the work.",
    state: "review",
  });
  return checks;
}
