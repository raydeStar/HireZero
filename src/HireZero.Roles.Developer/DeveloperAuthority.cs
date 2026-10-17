using System.Text.Json;
using HireZero.Tools.Git;
using HireZero.Tools.GitHub;

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
        var args = DeveloperAuthority.Args(invocation);
        return invocation.Contract.Id switch
        {
            var id when id == GitCommands.Write.Id => store.ApprovalClaimed(workflow.Id, "repair")
                || store.ApprovalClaimed(workflow.Id, "publish"),
            var id when id == GitCommands.Push.Id => workflow.State == "pushing"
                && workflow.CommitSha is { Length: 40 } commit
                && store.ApprovalClaimed(workflow.Id, "publish")
                && GitCommands.Subcommand(args) is [_, _, var refspec]
                && refspec.StartsWith(commit + ":refs/heads/", StringComparison.Ordinal),
            var id when id == GitHubCommands.Write.Id => workflow.State == "merge-pending"
                && args.Contains("PUT")
                && args.Contains("repos/" + workflow.Repository + "/pulls/" + workflow.Number + "/merge"),
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
