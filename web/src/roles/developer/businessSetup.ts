import type { BusinessProfile } from "../../company/api";
import type { BusinessSetup } from "../company";
import { approvedGoal, goalMatch } from "../businessContext";

export function developerRecommendation(profile: BusinessProfile) {
  const match = goalMatch(
    profile,
    /\b(websites?|web\s+pages?|landing\s+pages?|technical|software|bugs?|coding|code|integrations?|automat\w*|performance|accessibility)\b/i,
  );
  return match
    ? {
        reason: `Your approved goal mentions “${match}”. A Developer can scope the work and identify evidence needed before changes.`,
        priority: 80,
      }
    : undefined;
}

export function developerBusinessSetup(profile: BusinessProfile): BusinessSetup {
  const objective = approvedGoal(profile) ?? "Choose a useful first technical improvement";
  return {
    reason: "Use the approved business brief to scope technical work before granting repository or tool access.",
    responsibility: `Help with this goal: ${objective}. Prepare technical assessments and implementation proposals for owner review.`,
    values: {
      purpose: "Support the owner's approved business goal with practical technical advice and bounded proposals.",
      architecture:
        "Use verified system details from approved evidence. Website descriptions do not establish its code, stack or access. Mark missing details unknown.",
      standards:
        "Follow approved company constraints. Propose maintainable, accessible changes and explain validation; do not claim tests have run.",
      acceptance:
        "Explain the observed need, a small proposed improvement, required evidence and a concrete check the owner can review.",
      constraints:
        "Prepare internal advice only. Do not browse, execute code, edit files, contact others, publish or spend without separate task authority.",
    },
    firstAssignment: {
      title: "Your first technical plan",
      description:
        "A scoped technical assessment: known facts, up to three possible improvements, missing evidence and acceptance checks.",
      skill: "technical-starter-plan",
      prompt: `Prepare a concise technical starter plan for this goal: ${objective}. Use only the approved company brief and department context supplied. Separate: 1. Known business requirements with brief field references; 2. Up to three proposed technical improvements, clearly labeled hypotheses; 3. Evidence or access needed before implementation; 4. One small first task with acceptance checks and at most three questions. Do not imply you visited the website, inspected a repository or ran tests. Prefer practical work for a team of one to five people. Return internal advice for owner review; do not perform actions.`,
    },
  };
}
