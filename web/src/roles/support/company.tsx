import type { CompanyRoleUi } from "../company";
import { ReplyBrief, replyFields } from "./ReplyBrief";

export const companyUi: CompanyRoleUi = {
  skillInputs: {
    "support-reply": { label: "Customer reply", fields: replyFields, Editor: ReplyBrief },
  },
};
