namespace HireZero.Tools.AzureDevOps;

// Organization is null for every configured organization; Max is 1 to 25 per organization.
public sealed record AttentionRequest(string? Organization, int Max);

// One of the owner's open pull requests and what needs them: failing or pending policies, open threads, conflicts,
// blocking votes. Untrusted third-party text.
public sealed record PullRequestAttention(
    string Organization,
    int Id,
    string Title,
    string Project,
    string Repository,
    string Source,
    string Target,
    bool Draft,
    string MergeStatus,
    string[] Failing,
    string[] Running,
    int OpenThreads,
    string[] Problems,
    string Url
);

public sealed record AzureDevOpsStatusRequest;

public static class AzureDevOpsTools
{
    public static readonly ToolContract<AzureDevOpsStatusRequest, IntegrationStatus> Status = new(
        "azuredevops.status",
        ToolRisk.Read,
        "Whether Azure DevOps is configured, and why not."
    );

    public static readonly ToolContract<AttentionRequest, PullRequestAttention[]> Attention = new(
        "azuredevops.pullrequests.attention",
        ToolRisk.Read,
        "Read the owner's open pull requests and what needs them (checks, threads, conflicts, votes)."
    );
}
