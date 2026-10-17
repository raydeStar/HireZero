export interface ReviewRepository {
  id: string;
  accountId: number;
  gitHubId: number;
  name: string;
  rootPath: string;
  project: string;
}
export interface RepositoryChoice {
  id: number;
  name: string;
  checkoutId?: string | null;
  rootPath?: string | null;
}
export interface ConnectionProposal {
  id: string;
  requestId: string;
  accountId: number;
  choices: RepositoryChoice[];
  confirmed: boolean;
  action: "connect" | "enableAutomatic" | "disableAutomatic";
}
export interface ReviewThread {
  id: string;
  path: string;
  line: number | null;
  outdated: boolean;
  complete: boolean;
  actionable: boolean;
  comments: { author: string; body: string }[];
}
export interface CodeReview {
  repository: string;
  number: number;
  title: string;
  headSha: string;
  complete: boolean;
  threads: ReviewThread[];
}
export interface ReviewFix {
  id: string;
  repositoryId: string;
  number: number;
  headSha: string;
  snapshot: string;
  threadIds: string[];
  status: string;
  workItemId: string | null;
  scoutId: string | null;
  builderId: string | null;
}
export interface ReviewRun {
  id: string;
  state: string;
  kind: string;
  model: string;
  reasoning: string;
  contextHash: string;
  proposalVersion: number;
  assessment: { diagnosis: string; acceptanceCriteria: string[]; requiredPermissions: string[] } | null;
  questions: { id: string; prompt: string; state: string }[];
  result: string | null;
  error: string | null;
  steps?: string[];
}
export interface FixStatus {
  fix: ReviewFix;
  run: ReviewRun | null;
}
export interface FixStrategy {
  summary: string;
  steps: string[];
  tests: string[];
  businessRuleChanges: string[];
  questions: string[];
  evidenceRequests?: { kind: string; value: string; startLine: number }[];
  frontend: boolean;
  priority: string;
  ambiguous: boolean;
  crossSystem: boolean;
  escalationReason: string;
  trigger: string;
  evidence: string[];
  regressionTest: string;
}
export interface FixLoop {
  id: string;
  requestId: string;
  repository: string;
  number: number;
  state: string;
  round: number;
  maxRounds: number;
  stopAfterRound: boolean;
  workflowId: string | null;
  reviewedSha: string | null;
  reviewRequestedAt: string | null;
  note: string | null;
  updatedAt: string;
}
export interface PullRequestWorkflow {
  id: string;
  requestId: string;
  repository: string;
  number: number;
  headSha: string;
  evidenceSnapshot: string;
  approvalSnapshot: string;
  publicationSnapshot: string;
  strategy: FixStrategy;
  planningModel: string;
  state: string;
  rulesApproved: boolean;
  commitSha: string | null;
  error: string | null;
  ruleConcerns: string[] | null;
  runState?: string | null;
  fixId?: string | null;
  codingRunId?: string | null;
  sensitiveFindings?: string[] | null;
  sensitivePending?: string | null;
  round?: number | null;
  approvalAvailable?: boolean | null;
}
export interface MergeReadiness {
  headSha: string;
  snapshot: string;
  ready: boolean;
  blockers: string[];
}
