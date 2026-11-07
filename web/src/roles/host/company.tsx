import type { CompanyRoleUi } from "../company";
import { Onboarding } from "./Onboarding";

// HireZero, the onboarding host (docs/adr/0186): a new company's first screen, then it steps aside.
export const companyUi: CompanyRoleUi = {
  onboarding: Onboarding,
};
