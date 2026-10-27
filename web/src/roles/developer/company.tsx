import type { CompanyRoleUi } from "../company";
import { developerBusinessSetup, developerRecommendation } from "./businessSetup";

export const companyUi: CompanyRoleUi = {
  businessSetup: developerBusinessSetup,
  recommendation: developerRecommendation,
};
