import type { CompanyRoleUi } from "../company";
import { EmployeeBrief, marketingFields } from "./EmployeeBrief";
import { marketingBusinessSetup, marketingRecommendation } from "./businessSetup";
import { starterPlanChecks } from "./draftChecks";

export const companyUi: CompanyRoleUi = {
  draftChecks: { "marketing-starter-plan": starterPlanChecks },
  businessSetup: marketingBusinessSetup,
  recommendation: marketingRecommendation,
  skillInputs: {
    "marketing-draft": { label: "Campaign brief", fields: marketingFields, Editor: EmployeeBrief },
  },
};
