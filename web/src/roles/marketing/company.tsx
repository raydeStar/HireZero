import type { CompanyRoleUi } from "../company";
import { EmployeeBrief, marketingFields } from "./EmployeeBrief";
import { marketingBusinessSetup, marketingRecommendation } from "./businessSetup";
import { starterPlanChecks } from "./draftChecks";
import { ladderChecks } from "./ladder";
import { MarketingHome, MarketingLead, MarketingNoteLine, MarketingShiftAction } from "./ShiftCard";
import { morningChecks } from "./morning";

export const companyUi: CompanyRoleUi = {
  draftChecks: {
    "marketing-starter-plan": starterPlanChecks,
    "marketing-goal-ladder": ladderChecks,
    "marketing-morning": morningChecks,
  },
  homeCard: MarketingHome,
  homeLead: MarketingLead,
  homeAction: MarketingShiftAction,
  noteLine: MarketingNoteLine,
  businessSetup: marketingBusinessSetup,
  recommendation: marketingRecommendation,
  skillInputs: {
    "marketing-draft": { label: "Campaign brief", fields: marketingFields, Editor: EmployeeBrief },
  },
};
