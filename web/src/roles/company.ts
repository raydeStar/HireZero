import type { ComponentType } from "react";
import type { Role } from "../company/api";

// What an employee module contributes to the company workspace. Each web/src/roles/<id>/company.tsx exports one,
// and it renders only in the UI slots that module's manifest declares (docs/adr/0002-employee-module-contract.md).
export type SkillInput = {
  // Structured input for one skill: every field is required and the values are sent as a JSON object.
  label: string;
  fields: readonly string[];
  Editor: ComponentType<{ value: Record<string, string>; onChange: (value: Record<string, string>) => void }>;
};
export type CompanyRoleUi = {
  skillInputs?: Record<string, SkillInput>;
  panels?: Record<string, ComponentType>;
};

// Picked up automatically: adding web/src/roles/<id>/company.tsx needs no edit here (docs/adr/0006).
const contributions = import.meta.glob<{ companyUi: CompanyRoleUi }>("./*/company.tsx", { eager: true });
const byRole: Record<string, CompanyRoleUi> = Object.fromEntries(
  Object.entries(contributions).map(([path, module]) => [path.split("/")[1], module.companyUi]),
);

export function skillInput(role: Role | undefined, skill: string): SkillInput | undefined {
  if (!role?.uiSlots?.includes("skill-input:" + skill)) return undefined;
  return byRole[role.id]?.skillInputs?.[skill];
}

// Every panel contributed to a named slot by the given roles, in role order.
export function panels(roles: Role[], slot: string): { roleId: string; Panel: ComponentType }[] {
  return roles.flatMap((role) => {
    const Panel = role.uiSlots?.includes(slot) ? byRole[role.id]?.panels?.[slot] : undefined;
    return Panel ? [{ roleId: role.id, Panel }] : [];
  });
}
