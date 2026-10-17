namespace HireZero.Roles.Developer;

// The persisted workflow uses wire strings. Parse them once into the state contract used by local observations.
public enum PullRequestWorkflowState
{
    Unknown,
    Proposed,
    NeedsEvidence,
    HandoffAttention,
    Preparing,
    Scoping,
    Coding,
    Validating,
    NeedsInput,
    NeedsAttention,
    ValidationAttention,
    BusinessRuleAttention,
    PublicationProposed,
    Committing,
    Pushing,
    Published,
    MergePending,
    MergeAttention,
    Merged,
    Canceled,
    ConsentRevoked,
    ScopeChanged,
    ReadOnly,
    Failed,
    ExecutionError,
}

public static class PullRequestWorkflowStates
{
    public static PullRequestWorkflowState Parse(string state) =>
        state switch
        {
            "proposed" => PullRequestWorkflowState.Proposed,
            "needs-evidence" => PullRequestWorkflowState.NeedsEvidence,
            "handoff-attention" => PullRequestWorkflowState.HandoffAttention,
            "preparing" => PullRequestWorkflowState.Preparing,
            "scoping" => PullRequestWorkflowState.Scoping,
            "coding" => PullRequestWorkflowState.Coding,
            "validating" => PullRequestWorkflowState.Validating,
            "needs-input" => PullRequestWorkflowState.NeedsInput,
            "needs-attention" => PullRequestWorkflowState.NeedsAttention,
            "validation-attention" => PullRequestWorkflowState.ValidationAttention,
            "business-rule-attention" => PullRequestWorkflowState.BusinessRuleAttention,
            "publication-proposed" => PullRequestWorkflowState.PublicationProposed,
            "committing" => PullRequestWorkflowState.Committing,
            "pushing" => PullRequestWorkflowState.Pushing,
            "published" => PullRequestWorkflowState.Published,
            "merge-pending" => PullRequestWorkflowState.MergePending,
            "merge-attention" => PullRequestWorkflowState.MergeAttention,
            "merged" => PullRequestWorkflowState.Merged,
            "canceled" => PullRequestWorkflowState.Canceled,
            "consent-revoked" => PullRequestWorkflowState.ConsentRevoked,
            "scope-changed" => PullRequestWorkflowState.ScopeChanged,
            "read-only" => PullRequestWorkflowState.ReadOnly,
            "failed" => PullRequestWorkflowState.Failed,
            "execution-error" => PullRequestWorkflowState.ExecutionError,
            _ => PullRequestWorkflowState.Unknown,
        };

    public static string BacklogStatus(string state) =>
        Parse(state) switch
        {
            PullRequestWorkflowState.Failed or PullRequestWorkflowState.ExecutionError => "failure",
            PullRequestWorkflowState.Preparing
            or PullRequestWorkflowState.Scoping
            or PullRequestWorkflowState.Coding
            or PullRequestWorkflowState.Validating
            or PullRequestWorkflowState.Committing
            or PullRequestWorkflowState.Pushing
            or PullRequestWorkflowState.MergePending
            or PullRequestWorkflowState.Merged
            or PullRequestWorkflowState.Canceled => state,
            _ => "needs-you",
        };
}
