import type { BusinessProfile } from "../../company/api";
import type { BusinessSetup } from "../company";
import { approvedGoal, goalMatch } from "../businessContext";

export function ceoRecommendation(profile: BusinessProfile) {
  const match = goalMatch(
    profile,
    /\b(coordinat\w*|prioriti\w*|team|delegat\w*|roadmap|strateg\w*|workflow\w*|project\s+plan\w*)\b/i,
  );
  return match
    ? {
        reason: `Your approved goal mentions “${match}”. A CEO can turn it into priorities and decisions for your review.`,
        priority: 90,
      }
    : undefined;
}

export function ceoBusinessSetup(profile: BusinessProfile): BusinessSetup {
  const objective = approvedGoal(profile) ?? "Choose priorities for the company's next week";
  return {
    reason: "Start with a short priority plan grounded in the approved business brief; a full team is not required.",
    responsibility: `Help with this goal: ${objective}. Clarify priorities, propose bounded work and surface decisions for the human owner.`,
    values: {
      purpose:
        "Help the company achieve the goal in its current approved brief with practical priorities and visible decisions.",
      mandate:
        "Recommend the next useful work, what evidence is needed and where an existing specialist can help. Keep assignments as proposals until the owner approves them.",
      constraints:
        "Respect approved company boundaries. Do not invent employee capabilities, read private colleague context, hire, approve your own work, publish or spend.",
    },
    firstAssignment: {
      title: "Your first priority plan",
      description:
        "A short list of priorities, the next useful task and decisions that need you. You can start before hiring a full team.",
      skill: "company-priorities",
      prompt: `Prepare a concise one-week priority plan for this goal: ${objective}. Ground observations in the approved brief, citing field names. Suggest up to three priorities with a reason and success measure, one small first task and at most three owner decisions or missing facts. Distinguish verified facts from hypotheses. Mention existing colleagues only when supplied, and do not assign work or invent a team. Return readable internal advice, not a delegation JSON proposal. Nothing is approved or executed by this response.`,
    },
  };
}
