import type { BusinessProfile } from "../company/api";

export function approvedGoal(profile: BusinessProfile): string | undefined {
  const goal = profile.fields.find((f) => f.key === "goals");
  return goal && (goal.verification === "owner" || goal.verification === "sourced") && goal.value.trim()
    ? goal.value.trim()
    : undefined;
}

export function goalMatch(profile: BusinessProfile, pattern: RegExp): string | undefined {
  return approvedGoal(profile)?.match(pattern)?.[0];
}
