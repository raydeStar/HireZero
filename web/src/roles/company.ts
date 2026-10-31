import type { ComponentType } from "react";
import type { BusinessProfile, Role, Snapshot, Work } from "../company/api";

// What an employee module contributes to the company workspace. Each web/src/roles/<id>/company.tsx exports one,
// and it renders only in the UI slots that module's manifest declares (docs/adr/0002-employee-module-contract.md).
export type SkillInput = {
  // Structured input for one skill: every field is required and the values are sent as a JSON object.
  label: string;
  fields: readonly string[];
  Editor: ComponentType<{ value: Record<string, string>; onChange: (value: Record<string, string>) => void }>;
};
// A card on Home for a role's standing work with the owner, such as Marketing's goal ladder.
export type HomeCardProps = { snapshot: Snapshot; refresh: () => Promise<void>; openWork: (task: Work) => void };
export type CompanyRoleUi = {
  skillInputs?: Record<string, SkillInput>;
  panels?: Record<string, ComponentType>;
  homeCard?: ComponentType<HomeCardProps>;
  businessSetup?: (profile: BusinessProfile) => BusinessSetup;
  // Shared specificity: 3 requested work, 2 named deliverable, 1 topic, 0 general starting option.
  recommendation?: (profile: BusinessProfile) => { reason: string; priority: number; position?: number } | undefined;
  draftChecks?: Record<string, (body: string, task: Work, tasks: Work[]) => DraftCheck[]>;
};
export type DraftCheck = { label: string; detail: string; state: "pass" | "fail" | "review" };

export function draftChecks(role: Role | undefined, body: string, task: Work, tasks: Work[]): DraftCheck[] {
  if (!role?.uiSlots?.includes("draft-checks:" + task.skill)) return [];
  return byRole[role.id]?.draftChecks?.[task.skill]?.(body, task, tasks) ?? [];
}
export type BusinessSetup = {
  reason: string;
  responsibility: string;
  values: Record<string, string>;
  firstAssignment: { title: string; description: string; prompt: string; skill: string };
  companyKnowledge?: { id: string; version: number; digest: string };
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

export function approvedBusinessProfile(snapshot: Snapshot): BusinessProfile | undefined {
  if (snapshot.data.business?.state !== "confirmed") return undefined;
  const knowledge = snapshot.data.knowledge.filter((k) => !k.departmentId && k.state === "approved").at(-1);
  return snapshot.data.businessProfiles
    ?.filter(
      (p) =>
        p.state === "approved" &&
        p.businessVersion === snapshot.data.business?.version &&
        p.knowledgeId === knowledge?.id,
    )
    .at(-1);
}

export function businessSetup(snapshot: Snapshot, roleId: string): BusinessSetup | undefined {
  const role = snapshot.data.roles.find((r) => r.id === roleId);
  if (!role?.uiSlots?.includes("business-setup")) return undefined;
  const profile = approvedBusinessProfile(snapshot);
  const setup = profile ? byRole[roleId]?.businessSetup?.(profile) : undefined;
  const knowledge = snapshot.data.knowledge.find((k) => k.id === profile?.knowledgeId);
  return setup && knowledge
    ? { ...setup, companyKnowledge: { id: knowledge.id, version: knowledge.version, digest: knowledge.digest } }
    : undefined;
}

export function teamRecommendations(snapshot: Snapshot) {
  const profile = approvedBusinessProfile(snapshot);
  if (!profile) return [];
  return recommendationsForProfile(snapshot.data.roles, profile);
}

export function setupForProfile(profile: BusinessProfile, role: Role): BusinessSetup | undefined {
  return role.uiSlots?.includes("business-setup") ? byRole[role.id]?.businessSetup?.(profile) : undefined;
}

export function recommendationsForProfile(roles: Role[], profile: BusinessProfile) {
  const choices = roles.flatMap((role) => {
    if (!role.uiSlots?.includes("business-setup") || !byRole[role.id]?.businessSetup) return [];
    const match = byRole[role.id]?.recommendation?.(profile);
    return match ? [{ role, ...match }] : [];
  });
  // Incidental topics should not compete with the work the owner actually asked for.
  const strongest = Math.max(0, ...choices.map((c) => c.priority));
  return choices
    .filter((c) => c.priority === strongest)
    .sort((a, b) => (a.position ?? 0) - (b.position ?? 0) || a.role.name.localeCompare(b.role.name))
    .slice(0, 3);
}

// Every panel contributed to a named slot by the given roles, in role order.
export function panels(roles: Role[], slot: string): { roleId: string; Panel: ComponentType }[] {
  return roles.flatMap((role) => {
    const Panel = role.uiSlots?.includes(slot) ? byRole[role.id]?.panels?.[slot] : undefined;
    return Panel ? [{ roleId: role.id, Panel }] : [];
  });
}

// Each role's Home card, for roles whose manifest declares the "home-card" slot, in role order.
export function homeCards(roles: Role[]): { roleId: string; Card: ComponentType<HomeCardProps> }[] {
  return roles.flatMap((role) => {
    const Card = role.uiSlots?.includes("home-card") ? byRole[role.id]?.homeCard : undefined;
    return Card ? [{ roleId: role.id, Card }] : [];
  });
}
