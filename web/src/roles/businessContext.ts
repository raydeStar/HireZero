import type { BusinessProfile } from "../company/api";

export function approvedGoal(profile: BusinessProfile): string | undefined {
  const goal = profile.fields.find((f) => f.key === "goals");
  return goal && (goal.verification === "owner" || goal.verification === "sourced") && goal.value.trim()
    ? goal.value.trim()
    : undefined;
}

export function goalMatch(profile: BusinessProfile, patterns: readonly [RegExp, RegExp, RegExp]) {
  const goal = approvedGoal(profile);
  if (!goal) return undefined;
  // Requests outrank named deliverables, which outrank topics. The company shell need not know any role's words.
  for (const [index, pattern] of patterns.entries()) {
    for (const match of goal.matchAll(new RegExp(pattern.source, pattern.flags.replace(/g/g, "") + "g"))) {
      const clause =
        goal
          .slice(0, match.index)
          .split(/[.;,!?\n]|\bbut\b/i)
          .at(-1) ?? "";
      if (/\b(?:do\s+not|don['’]t|no\s+need\s+to|avoid|exclude|without)\b/i.test(clause)) continue;
      return { text: match[0], priority: 3 - index, position: match.index };
    }
  }
  return undefined;
}
