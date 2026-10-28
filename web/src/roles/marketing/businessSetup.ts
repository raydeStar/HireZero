import type { BusinessProfile } from "../../company/api";
import type { BusinessSetup } from "../company";
import { approvedGoal, goalMatch } from "../businessContext";

export function marketingRecommendation(profile: BusinessProfile) {
  const match = goalMatch(profile, [
    /\b(write|draft|prepare|create|plan|launch|improve|grow)\s+(?:(?!about\b|from\b|using\b|based\b|grounded\b|for\b|with\b|and\b)[\w'-]+\s+){0,4}(marketing|campaigns?|content|posts?|copy|newsletters?|advertising|leads?|sales|audience|brand)\b/i,
    /\b(content|marketing|campaign|social\s+media)\s+(plans?|strategy|calendars?|campaigns?)\b|\b(website|landing\s+page|email|blog)\s+copy\b/i,
    /\b(marketing|campaigns?|content|advertis\w*|leads?|audience|brand\w*|launch\w*|sales|customers?)\b/i,
  ]);
  return match
    ? {
        reason: `Your approved goal mentions “${match.text}”. Start with a small campaign or content plan.`,
        priority: match.priority,
        position: match.position,
      }
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
      prompt: `Prepare a concise one-week marketing starter plan for this goal: ${objective}. Use the supplied approved company brief and department context. Return a draft for internal review; do not perform any actions.

Lead with three distinct, finished posts of 40–80 words each for Monday, Wednesday and Friday. This length is guidance: a useful complete asset matters more than an exact count. Include the actual template, checklist or worked example inside its post; a blank template needs a short completed example. Give each reader one useful action. Deliver the work, not suggestions for assets the owner still has to create.

Choose the audience problem from the company facts and goal. Demonstrate the company's offering: name the company and connect the example to its supported offering in at least one post, without repeating a product-description sentence in every post. Keep availability claims within approved facts. If explaining a workflow, use one clearly fictional example across three stages: Monday supplies the complete brief and assignment; Wednesday fulfills that assignment; Friday identifies a specific reader difficulty and improves the same output. The improvement must make an action or decision easier, not merely substitute words or fix an instruction deliberately ignored on Wednesday. Supply every factual detail in Monday's brief. Do not add facts, change requirements, invent a booking/contact channel, or promise unavailable capabilities to finish the example. Each post must make sense on its own and identify a fictional example as fictional. Write each asset once.

After the posts, give a preparation schedule covering all three days, review actions and effort estimates totaling at most 90 minutes. The assets must already be written. Include one proposed numeric learning target based on reported useful completed use, a fill-in tally of completions and problems, and a next-week decision for every outcome. Address reported problems first; otherwise distinguish reaching the target, positive evidence below it when applicable, and no evidence. Small samples are tentative; no reports or unknown reach is inconclusive, not failure. One post's reader action must ask for the before/after or completion report the tally needs. If readers can try the example without the product, distinguish evidence for the workflow from evidence of product demand.

Keep production notes outside the post copy. In short review notes, give two observations restating relevant approved facts with their brief field names, and explain how they support the proposed audience problem and experiment. Label proposals and hypotheses. Recommend one channel as a hypothesis with uncertain fit flagged once; keep copy portable. Do not invent customer results, prices, website inspections or offers the owner must fulfill. End with at most three consequential open decisions, without repeating flagged uncertainties; state none if none remain. Aim for about 500 words overall; retain necessary substance rather than cutting a usable example to meet the count.`,
    },
  };
}
