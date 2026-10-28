import type { BusinessProfile } from "../../company/api";
import type { BusinessSetup } from "../company";
import { approvedGoal, goalMatch } from "../businessContext";

export function marketingRecommendation(profile: BusinessProfile) {
  const match = goalMatch(profile, [
    /\b(write|draft|prepare|create|plan|launch|improve|grow)\s+(?:(?!about\b|from\b|using\b|based\b|grounded\b|for\b|with\b|and\b)[\w'-]+\s+){0,4}(marketing|campaigns?|content|posts?|copy|emails?|newsletters?|advertising|leads?|sales|audience|brand)\b/i,
    /\b(content|marketing|campaign|social\s+media)\s+(plans?|strategy|calendars?|campaigns?)\b|\b(website|landing\s+page|email|blog)\s+copy\b/i,
    /\b(marketing|campaigns?|content|advertis\w*|leads?|audience|brand\w*|launch\w*|sales|customers?)\b/i,
  ]);
  return match
    ? {
        reason: `Your approved goal mentions “${match.text}”. A Marketing employee can prepare that work for review.`,
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
  const weekly = goalMatch(profile, [
    /\b(?:weekly|one[- ]week|next week['’]s?)\s+(?:marketing|content|social(?:\s+media)?)\s+(?:plan|calendar)\b/i,
    /\b(?:content|social(?:\s+media)?)\s+(?:plan|calendar)\b/i,
    /\b(?:three|3)\s+(?:\w+\s+){0,2}posts\b[^.!?\n]*\b(?:week|monday|wednesday|friday)\b/i,
  ]);
  const assignment = weekly
    ? weeklyAssignment(objective)
    : {
        title: "Your marketing draft",
        description: "The asset your goal calls for, with short notes on facts and decisions to review.",
        skill: "marketing-goal-draft",
        prompt: `Produce the finished marketing deliverable for this goal:\n${objective}\nUse the supplied approved business brief and department context. Preserve the requested format, number of assets, audience and constraints. Lead with the actual copy or requested analysis; do not substitute a weekly content plan or assign the creative work back to the owner. Write for the reader: provide useful choices, explanations and a clear next step, with each section adding value instead of repeating the business description. If the goal is broad, choose one small useful asset and identify that choice as a proposal. Use approved facts wherever they appear in the brief. Keep internal approval notes and missing decisions in a short review section, outside customer copy; retain qualifications a reader actually needs. Choose content you can complete with the known facts. Cite the facts used and distinguish them from reversible assumptions; list at most three consequential missing decisions. Do not invent factual claims, offers, contact or ordering details, results or source inspections. Do not add a fictional client example unless it helps the requested deliverable. Follow the owner's requested length; otherwise aim for about 500 words or fewer. Return an internal draft for review without publishing, outreach, tool use or spending.`,
      };
  return {
    reason: "Use your approved business brief to prepare the marketing work requested in your goal.",
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
    firstAssignment: assignment,
  };
}

function weeklyAssignment(objective: string): BusinessSetup["firstAssignment"] {
  return {
    title: "Your first marketing plan",
    description: "One focused week of finished posts shaped around your business and goal.",
    skill: "marketing-starter-plan",
    prompt: `Prepare a practical one-week marketing starter plan for this goal: ${objective}. Use the supplied approved company brief and department context. Return a draft for internal review; do not perform actions.

Preserve any explicit post count, days, channel, format and length in the owner's goal. Otherwise lead with three distinct, finished posts spread across the week. Let each post's purpose, length and reader action fit the actual business, audience and goal. Deliver the copy, not suggestions for assets the owner still has to create. If a post promises a template, checklist or example, include it in full.

Choose the audience problem and content mix from the company facts and goal. Connect the posts naturally to the supported offering without repeating a company description. Do not force a tutorial, fictional client, before/after or three-stage workflow onto a business that does not need one. When an example helps, make it complete, internally consistent and clearly illustrative; each post must stand on its own. Keep real company claims and availability within approved facts. Do not invent booking/contact channels, results, offers or capabilities. Write each asset once.

After the posts, give a preparation schedule covering every post, review actions and effort estimates totaling at most 90 minutes. The assets must already be written. Propose one modest, observable learning target suited to this business and goal, how to record it, and what to change next week for promising, weak or inconclusive evidence. Match the reader actions to that signal. Do not impose completed-workflow reports on a campaign whose goal is inquiries, visits or another outcome. Distinguish attention from demand, avoid invented baselines, and treat small samples and unknown reach cautiously.

Keep production notes outside the post copy. In short review notes, cite the relevant approved facts and explain the proposed audience and approach. Label assumptions and hypotheses. Use the requested channel; otherwise recommend one and flag uncertain fit once. End with at most three consequential open decisions, without repeating flagged uncertainties; state none if none remain. Stay concise, with room for about 750–900 words overall when full assets and review notes need it. This is a ceiling guide, not a quota; honor a shorter requested length.`,
  };
}
