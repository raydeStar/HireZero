using System.Text.Json;
using HireZero.Tools.Git;
using HireZero.Tools.GitHub;
using HireZero.Tools.SemaForge;
using HireZero.Tools.Wiki;

namespace HireZero.Roles.Developer;

// The authority under which Developer's git and GitHub tool calls run (docs/adr/0007-tool-registry.md). An approved
// workflow or fix loop enters its scope around the work it authorizes; the verifiers below check that scope against
// the stored approvals and the exact command, at the moment the tool is called.
public static class DeveloperAuthority
{
    public const string WorkflowKind = "developer-workflow";
    public const string LoopKind = "developer-loop";

    private static readonly AsyncLocal<ToolAuthority?> Ambient = new();

    public static ToolAuthority? Current => Ambient.Value;

    public static IDisposable Workflow(string workflowId) => Enter(new(WorkflowKind, workflowId));

    public static IDisposable Loop(string loopId) => Enter(new(LoopKind, loopId));

    public const string WikiDraftKind = "developer-wiki-draft";
    public const string FixCancelKind = "developer-fix-cancel";

    // Stopping a fix's own SemaForge runs is always allowed: cancellation only stops work.
    public static IDisposable FixCancel(ReviewFix fix) => Enter(new(FixCancelKind, fix.RepositoryId + "/" + fix.Id));

    public static IDisposable WikiDraft(string draftId) => Enter(new(WikiDraftKind, draftId));

    // The authority for a service tool call: none for reads; otherwise the most specific scope in effect, falling
    // back to the owner's request in flight.
    public static ToolAuthority? ForService(IToolContract contract) =>
        contract.Risk == ToolRisk.Read ? null : Current ?? OwnerRequests.Current;

    private static IDisposable Enter(ToolAuthority authority)
    {
        var previous = Ambient.Value;
        Ambient.Value = authority;
        return new Scope(() => Ambient.Value = previous);
    }

    internal static string[] Args(ToolInvocation invocation)
    {
        using var input = JsonDocument.Parse(invocation.InputJson);
        return [.. input.RootElement.GetProperty("args").EnumerateArray().Select(a => a.GetString() ?? "")];
    }

    internal static string? Input(ToolInvocation invocation)
    {
        using var input = JsonDocument.Parse(invocation.InputJson);
        return input.RootElement.TryGetProperty("input", out var value) ? value.GetString() : null;
    }

    private sealed class Scope(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

// A workflow authorizes local checkout changes once its repair or publication approval was claimed, a push only
// while it is pushing its own approved commit, and a merge only of its own pull request while the merge is pending.
public sealed class WorkflowAuthorityVerifier(GitHubReviewStore store) : IToolAuthorityVerifier
{
    public string Kind => DeveloperAuthority.WorkflowKind;

    public bool Authorizes(ToolInvocation invocation)
    {
        var workflow = store.Workflows().SingleOrDefault(w => w.Id == invocation.Authority?.Id);
        if (
            workflow is null
            || workflow.State is "canceled" or "consent-revoked" or "scope-changed" or "merged" or "read-only"
        )
            return false;
        string[] Args() => DeveloperAuthority.Args(invocation);
        return invocation.Contract.Id switch
        {
            var id when id == GitCommands.Write.Id => store.ApprovalClaimed(workflow.Id, "repair")
                || store.ApprovalClaimed(workflow.Id, "publish"),
            var id when id == GitCommands.Push.Id => workflow.State == "pushing"
                && workflow.CommitSha is { Length: 40 } commit
                && store.ApprovalClaimed(workflow.Id, "publish")
                && GitCommands.Subcommand(Args()) is [_, _, var refspec]
                && refspec.StartsWith(commit + ":refs/heads/", StringComparison.Ordinal),
            // Scouts, builders and their cancellation belong to the approved repair.
            var id when id == SemaForgeTools.Write.Id => store.ApprovalClaimed(workflow.Id, "repair"),
            var id when id == GitHubCommands.Write.Id => workflow.State == "merge-pending"
                && Args().Contains("PUT")
                && Args().Contains("repos/" + workflow.Repository + "/pulls/" + workflow.Number + "/merge"),
            _ => false,
        };
    }
}

// An active fix loop may ask Codex to review its own pull request, and nothing else.
public sealed class LoopAuthorityVerifier(GitHubReviewStore store) : IToolAuthorityVerifier
{
    public string Kind => DeveloperAuthority.LoopKind;

    public bool Authorizes(ToolInvocation invocation)
    {
        var loop = store.Loops(null).SingleOrDefault(l => l.Id == invocation.Authority?.Id);
        if (loop is null || !loop.Active || invocation.Contract.Id != GitHubCommands.Write.Id)
            return false;
        var args = DeveloperAuthority.Args(invocation);
        return args.Contains("POST")
            && args.Contains("repos/" + loop.Repository + "/issues/" + loop.Number + "/comments")
            && DeveloperAuthority.Input(invocation) == JsonSerializer.Serialize(new { body = "@codex review" });
    }
}

// A reserved wiki draft may submit its own proposal (bound by its idempotency key) and, while publishing, approve
// its own revision under the owner's automatic-publication policy.
public sealed class WikiDraftAuthorityVerifier(WikiDraftStore store) : IToolAuthorityVerifier
{
    public string Kind => DeveloperAuthority.WikiDraftKind;

    public bool Authorizes(ToolInvocation invocation)
    {
        var draft = store.Find(invocation.Authority?.Id ?? "");
        if (draft is null || !WikiDraftPolicy.CanSubmit(draft))
            return false;
        using var input = JsonDocument.Parse(invocation.InputJson);
        var path = (input.RootElement.GetProperty("path").GetString() ?? "").TrimStart('/');
        if (invocation.Contract.Id == WikiTools.Contribute.Id)
        {
            var body = input.RootElement.TryGetProperty("jsonBody", out var value) ? value.GetString() : null;
            if (draft.Status is not ("submitting" or "publishing" or "uncertain") || body is null)
                return false;
            using var proposal = JsonDocument.Parse(body);
            return proposal.RootElement.TryGetProperty("idempotencyKey", out var key)
                && key.GetString() == WikiProposalAdapter.IdempotencyKey(draft);
        }
        return invocation.Contract.Id == WikiTools.Publish.Id
            && draft.Status == "publishing"
            && draft.RevisionId is { Length: > 0 } revision
            && path == "api/review/" + revision;
    }
}

// A fix may cancel its own scout and builder runs, whatever state its workflow is in.
public sealed class FixCancelVerifier(GitHubReviewStore store) : IToolAuthorityVerifier
{
    public string Kind => DeveloperAuthority.FixCancelKind;

    public bool Authorizes(ToolInvocation invocation)
    {
        if (
            invocation.Contract.Id != SemaForgeTools.Write.Id
            || invocation.Authority?.Id.Split('/') is not [var repository, var id]
        )
            return false;
        var fix = store.Fixes(repository).SingleOrDefault(f => f.Id == id);
        using var input = JsonDocument.Parse(invocation.InputJson);
        var method = input.RootElement.GetProperty("method").GetString();
        var path = input.RootElement.GetProperty("path").GetString();
        return fix is not null
            && method == "POST"
            && new[] { fix.BuilderId, fix.ScoutId }.Any(run =>
                run is not null && path == "/api/automation/runs/" + run + "/cancel"
            );
    }
}
