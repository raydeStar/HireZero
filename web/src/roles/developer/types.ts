// Developer-role data shapes (preparation, Jev triage, source packets, Wiki drafts).
export type Triage = {
  status: string;
  route: string | null;
  candidateRequestId: string | null;
  confidence: number | null;
  explanation: string | null;
};
export type Context = { semaForge: { status: string; packet: unknown }; wiki: { status: string; packet: unknown } };
export type Sources = { semaForge: string; jev: string; wiki: string };
export type PreparationStage = "intake" | "scoping" | "gathering" | "ready";
export type Preparation = {
  requestId: string;
  stage: PreparationStage;
  blocker: string;
  nextAction: string;
  updatedAt: string;
};
export type PreparationSuggestion = {
  suggestion: { stage: PreparationStage; blocker: string; nextAction: string; questions: string[] };
  sources: { semaForge: string; wiki: string };
};
export type WikiProject = { project: string; wikiProjectId: string; synthetic: boolean };
export type WikiContent = {
  knowledgeKey: string;
  kind: string;
  title: string;
  body: string;
  reason: string;
  sourceNote: string;
  conflict: boolean;
  missingInformation: boolean;
  resolutionNote: string;
};
export type WikiTarget = {
  entryId: string;
  revisionId: string;
  knowledgeKey: string;
  kind: string;
  title: string;
  body: string;
  version: number;
  status: string;
  environment: string;
  region: string;
  supported: boolean;
};
export type WikiDraft = {
  id: string;
  requestId: string;
  project: string;
  destination: WikiProject;
  content: WikiContent;
  before: WikiTarget | null;
  evidence: { kind: string; id: string }[];
  version: number;
  status: string;
  revisionId: string | null;
  entryId: string | null;
  updatedAt: string;
};
export type WikiScopeReview = { draftId: string; requestId: string };
export const preparationStages = [
  ["intake", "Intake"],
  ["scoping", "Scoping"],
  ["gathering", "Information gathering"],
  ["ready", "Ready for review"],
] as const;
