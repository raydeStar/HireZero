import type { CompanyRoleUi } from "../company";
import { PlanGuidance } from "./PlanGuidance";
import { ceoBusinessSetup, ceoRecommendation } from "./businessSetup";

export const companyUi: CompanyRoleUi = {
  panels: { "plan-guidance": PlanGuidance },
  businessSetup: ceoBusinessSetup,
  recommendation: ceoRecommendation,
};
