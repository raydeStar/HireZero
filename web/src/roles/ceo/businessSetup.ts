import type { BusinessProfile } from "../../company/api";
import type { BusinessSetup } from "../company";
import { approvedGoal, goalMatch } from "../businessContext";

export function ceoRecommendation(profile: BusinessProfile) {
  const match = goalMatch(profile, [
    /\b(coordinate|prioriti[sz]e|delegate|sequence)\b/i,
    /\b(company|team|business|project)\s+(priorities|roadmaps?|strategy|plans?)\b/i,
    /\b(coordinat\w*|prioriti\w*|team|delegat\w*|roadmap|strateg\w*|workflow\w*|project\s+plan\w*)\b/i,
  ]);
  return match
    ? {
        reason: `Your approved goal mentions “${match.text}”. A CEO can turn it into priorities and decisions for your review.`,
        priority: match.priority,
        position: match.position,
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
      prompt: `Prepare a one-week priority plan in at most 450 words for this goal:\n${objective}\nStart with at most two observations citing approved brief fields. Rank up to three priorities: explain why each comes now, its concrete deliverable, a proposed success check and estimated timebox. Sequence them using relative days, and name one thing to defer with a reason. Include a ready-to-use first-task brief now: approved inputs, steps, exact output and a check for completion. If it calls for a question, checklist or small template, write that item with a short completed example instead of assigning another planning exercise. End with at most three owner decisions or missing facts, recommending a default where supported. Distinguish facts, hypotheses and estimates. Mention colleagues only when supplied; do not assign work or invent a team. Return readable internal advice, not delegation JSON. Nothing is approved or executed by this response.`,
    },
  };
}
