import type { BusinessProfile } from "../../company/api";
import type { BusinessSetup } from "../company";
import { approvedGoal, goalMatch } from "../businessContext";

export function marketingRecommendation(profile: BusinessProfile) {
  const match = goalMatch(
    profile,
    /\b(marketing|campaigns?|content|advertis\w*|leads?|audience|brand\w*|launch\w*|sales|customers?)\b/i,
  );
  return match
    ? { reason: `Your approved goal mentions “${match}”. Start with a small campaign or content plan.`, priority: 60 }
    : {
        reason:
          "A starting option for a small business: a modest marketing experiment. Choose another role if that better fits your goal.",
        priority: 0,
      };
}

export function marketingBusinessSetup(profile: BusinessProfile): BusinessSetup {
  const objective = approvedGoal(profile) ?? "Choose a useful first marketing experiment";
  return {
    reason: "Suggested from your approved business brief: start with a small marketing plan you can review and try.",
    responsibility: `Help with this goal: ${objective}. Prepare focused marketing recommendations and drafts for owner review.`,
    // Refer to live company knowledge instead of making stale copies of its claims in a second department record.
    values: {
      purpose: "Prepare useful marketing work using the approved company brief and the owner's current goal.",
      audience: "Use the customers identified in the approved company brief. Ask when the audience is unknown.",
      positioning: "Use only approved company facts. Treat positioning ideas as hypotheses to test.",
      voice:
        "Follow the brand voice in the approved company brief. If unknown, propose a clear, approachable tone for review.",
      claims:
        "Use sourced or owner-confirmed company facts. Cite the approved brief; unknowns and hypotheses are not proof.",
      constraints:
        "Draft for internal owner review. Follow the company brief's boundaries. Do not invent claims, publish, send outreach, or spend.",
    },
    firstAssignment: {
      title: "Your first marketing plan",
      description: "A short plan, three small experiments and sample copy, based on the brief you approved.",
      skill: "marketing-starter-plan",
      prompt: `Prepare a concise one-week marketing starter plan for this goal: ${objective}. Use the approved company brief and department context supplied with this request. Separate: 1. Observations grounded in approved facts, with their field names; 2. Up to three hypotheses or small experiments, with an action and a success measure; 3. One short sample message for owner review; 4. Missing facts and at most three useful questions. Label hypotheses and unknowns clearly. Do not imply you visited a website or verified results. Keep the plan practical for a team of one to five people. Return a draft for internal review; do not perform any actions.`,
    },
  };
}
