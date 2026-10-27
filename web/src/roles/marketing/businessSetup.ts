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
      description: "One focused week, three finished drafts and a useful example your audience can try.",
      skill: "marketing-starter-plan",
      prompt: `Prepare a concise one-week marketing starter plan for this goal: ${objective}. Use the approved company brief and department context supplied with this request. Start with two short observations that restate relevant approved business facts and cite their brief field names. Explain how those facts lead to the recommended audience problem and weekly experiment; a list of field names is not an observation. Deliver three distinct, finished posts of 40–80 words each for Monday, Wednesday and Friday. Include the actual template, checklist or worked example inside the post that uses it; a template must include a short completed example, and an example must show finished text rather than ideas for future assets. Give each reader one useful action; avoid repeating a broad question across posts. Keep post copy separate from production notes. Propose a preparation schedule covering all three posts with review actions and effort estimates totaling at most 90 minutes. The owner should review the supplied assets, not have to create them. Include one modest numeric learning target, a small tally of observed use or feedback, and a decision rule for next week; no reports or unknown reach is inconclusive. Cite the brief fields behind factual claims in a short review section; label proposed audiences, experiments and fictional examples. Recommend one channel as a hypothesis, keeping copy portable and flagging uncertain fit once. Do not invent customer results, prices, inspected websites, product availability or offers the owner must fulfill. End with at most three consequential open decisions, without repeating an uncertainty already flagged. Keep the complete plan under 500 words. Return a draft for internal review; do not perform any actions.`,
    },
  };
}
