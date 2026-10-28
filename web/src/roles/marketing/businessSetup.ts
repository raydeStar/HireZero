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
        prompt: `Produce the finished marketing deliverable for this goal:\n${objective}\nUse the supplied approved business brief and department context. Preserve the requested format, number of assets, audience and constraints. Lead with the actual copy or requested analysis; do not substitute a weekly content plan or assign the creative work back to the owner. Write for the reader: provide useful choices, explanations and a clear next step, with each section adding value instead of repeating the business description. If the goal is broad, choose one small useful asset and identify that choice as a proposal. Use approved facts wherever they appear in the brief. Choose wording and structure yourself, but preserve known service scope and customer contact or booking routes; do not add purchase options or ask the owner to reconfirm supplied facts. A review-note caveat cannot justify an unsupported claim in the asset. Keep internal approval notes and missing decisions in a short review section, outside customer copy; retain qualifications a reader actually needs. Choose content you can complete with the known facts. Cite the facts used and distinguish them from reversible assumptions; list at most three consequential missing decisions. Do not invent factual claims, offers, contact or ordering details, results or source inspections. Do not add a fictional client example unless it helps the requested deliverable. Follow the owner's requested length; otherwise aim for about 500 words or fewer. Return an internal draft for review without publishing, outreach, tool use or spending.`,
      };
  return {
    reason: "Use your approved business brief to prepare the marketing work requested in your goal.",
    responsibility: `Help with this goal: ${objective}. Prepare focused marketing recommendations and drafts for owner review.`,
    // Refer to live company knowledge instead of making stale copies of its claims in a second department record.
    values: {
      purpose: "Prepare useful marketing work using the approved company brief and the owner's current goal.",
      audience:
        "Use the customers identified anywhere in the approved company brief. If unstated, propose a suitable audience in review notes without presenting it as an established company fact.",
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
    prompt: `Prepare the finished one-week marketing plan requested here:
${objective}

Use the approved business brief and department context. Preserve the owner's explicit quantity, channel, days, format and length. Otherwise supply three complete posts, each with a proposed posting day and a different useful takeaway for the intended reader. Choose the content mix for this business and goal; do not impose a fixed teaching sequence. Include promised examples, templates or checklists in the posts themselves, once.

After the copy, include one short batch-preparation checklist with honest effort estimates, totaling no more than 90 minutes. Do not turn a ready-written plan into a week of approval chores. Propose one observable learning target, how to record it through the known customer route, and next-week choices for promising, weak or inconclusive evidence.

Keep sources, assumptions and at most three consequential open decisions in short internal review notes. Facts supplied anywhere in the brief are facts, not assumptions. Aim for a focused plan; allow about 750–900 words when full assets and notes need the space, or honor a shorter requested length. Return a private draft only; proposed posting days do not authorize publishing.`,
  };
}
