import type { CompanyRoleUi } from "../company";
import { EmployeeBrief, marketingFields } from "./EmployeeBrief";
import { marketingBusinessSetup, marketingRecommendation } from "./businessSetup";

export const companyUi: CompanyRoleUi = {
  businessSetup: marketingBusinessSetup,
  recommendation: marketingRecommendation,
  skillInputs: {
    "marketing-draft": { label: "Campaign brief", fields: marketingFields, Editor: EmployeeBrief },
  },
};
