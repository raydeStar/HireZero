import type { CompanyRoleUi } from "../company";
import { EmployeeBrief, marketingFields } from "./EmployeeBrief";
import { marketingBusinessSetup, marketingRecommendation } from "./businessSetup";
import { starterPlanChecks } from "./draftChecks";
import { GoalLadder } from "./GoalLadder";
import { ladderChecks } from "./ladder";

export const companyUi: CompanyRoleUi = {
  draftChecks: { "marketing-starter-plan": starterPlanChecks, "marketing-goal-ladder": ladderChecks },
  homeCard: GoalLadder,
  businessSetup: marketingBusinessSetup,
  recommendation: marketingRecommendation,
  skillInputs: {
    "marketing-draft": { label: "Campaign brief", fields: marketingFields, Editor: EmployeeBrief },
  },
};
