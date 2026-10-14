using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HireZero.Core;

public sealed record BacklogSourceInfo(string Id, string Title, int Order, bool Configured, bool LocalOnly,
    string? SelectedScope, string? MissingReason)
{
    public string ScopeRevision => BacklogPolicy.Hash(JsonSerializer.Serialize(new { Id, SelectedScope, LocalOnly }));
    public bool Ready => Configured && LocalOnly && !string.IsNullOrWhiteSpace(SelectedScope);
}
public sealed record BacklogRead(string? Cursor, DateTimeOffset? Since, DateTimeOffset Through, int Limit);
public sealed record BacklogEvent(string ItemId, string Revision, string Kind, string Title, string Status,
    DateTimeOffset SourceUpdatedAt, string[] References, string? RequestId = null)
{
    public bool NeedsReview => Kind == "pipeline" ? Status is "failure" or "timed_out" or "action_required" or "needs-you" :
        Kind == "request" && Status == "needs-owner";
}
public sealed record BacklogPage(BacklogEvent[] Items, string? NextCursor, bool Complete, string[] Gaps,
    DateTimeOffset? EarliestAvailable = null);
public interface IBacklogSource
{
    BacklogSourceInfo Info { get; }
    Task<BacklogPage> ReadAsync(BacklogRead read, CancellationToken token);
}
// Trusted local adapters expose unfiltered membership in the same SQLite database as the backlog.
// The store evaluates it inside its completing transaction; paginated/time-filtered reads do not prove absence.
public interface IBacklogSnapshotSource : IBacklogSource
{
    string MembershipSql { get; }
}
public sealed record BacklogCheckpoint(string SourceId, string ScopeRevision, string State = "never-checked",
    string? Cursor = null, DateTimeOffset? Since = null, DateTimeOffset? Through = null,
    DateTimeOffset? LastAttempt = null, DateTimeOffset? LastPage = null, DateTimeOffset? LastComplete = null,
    DateTimeOffset? LeaseUntil = null, int Failures = 0, int Received = 0, string[]? Gaps = null, string? Note = null,
    bool AuthoritativeSnapshot = false);
public sealed record BacklogInboxItem(string Id, string SourceId, string ScopeRevision, BacklogEvent Event,
    DateTimeOffset RecordedAt, bool Current = true, string? UnavailableReason = null);
public sealed record BacklogPlan(string Action, string Provider, string[] Steps, string[] EvidenceFields)
{
    public bool LocalReview => Action == "manual-review" && Provider == "none";
    public string Transmission => LocalReview ? "Local review only. No evidence is sent to a provider." :
        "This plan would send the selected evidence to " + Provider + ". No approved backlog provider adapter is configured; transmission is blocked.";
}
public sealed record BacklogProposal(string Id, string InboxId, string SourceId, string ScopeRevision, string ItemId,
    string Revision, string Title, string Status, DateTimeOffset UpdatedAt, BacklogPlan? Plan = null,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, DateTimeOffset? ExpiresAt = null)
{
    public string Snapshot => BacklogPolicy.Hash(JsonSerializer.Serialize(new { Id, InboxId, SourceId, ScopeRevision, ItemId, Revision, Plan, UpdatedAt, ExpiresAt }));
    public string[] Blockers => Status == "source-unavailable" ? ["The source record is unavailable. Its metadata is history and cannot authorize a current plan."] :
        Plan is { LocalReview: false } ? ["No approved provider adapter or transmission scope. No model or execution will run."] : [];
}
public sealed record BacklogSourceView(BacklogSourceInfo Info, BacklogCheckpoint Checkpoint, string Freshness);
public sealed record BacklogDashboard(BacklogSourceView[] Sources, BacklogInboxItem[] Inbox, BacklogProposal[] Proposals,
    DateTimeOffset? PreviousHostSeen, DateTimeOffset HostSeen, string[] Limits);
public sealed record BacklogPlanInput(string Snapshot, string Action, string Provider);
public sealed record BacklogApprovalInput(string Snapshot);

public static class BacklogPolicy
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static void Validate(BacklogPage page, BacklogRead read)
    {
        if (page.Items.Length > read.Limit || page.Gaps.Length > 8 || page.Gaps.Any(item => item.Length > 500) ||
            page.NextCursor is { Length: > 500 } || !page.Complete && (page.NextCursor is null || page.NextCursor == read.Cursor) ||
            page.Complete && page.NextCursor is not null) throw new WorkspaceException("The source returned an incomplete or invalid bounded page.");
        foreach (var item in page.Items)
            if (item.ItemId.Length is < 1 or > 200 || item.Revision.Length is < 1 or > 100 || item.Title.Length is < 1 or > 240 ||
                item.Kind is not ("pipeline" or "request" or "conversation" or "session") || item.Status.Length is < 1 or > 80 ||
                item.References.Length > 6 || item.References.Any(value => value.Length > 500) || item.SourceUpdatedAt > read.Through)
                throw new WorkspaceException("The source returned metadata outside the selected page or time boundary.");
    }
    public static BacklogPlan Plan(string action, string provider)
    {
        if (action is not ("manual-review" or "diagnose" or "judge-comments" or "repair") || provider is not ("none" or "codex" or "claude") ||
            action == "manual-review" && provider != "none" || action != "manual-review" && provider == "none")
            throw new WorkspaceException("Choose local manual review or an explicitly named, currently unavailable provider plan.", 400);
        return new(action, provider, action == "manual-review" ? ["Review the recorded metadata locally.", "Choose a separately approved next action."] :
            ["Verify current source evidence and the exact scope.", "Approve the selected evidence transmission to an authorized provider.", "Review its result before any execution."],
            ["title", "status", "source reference", "exact evidence revision"]);
    }
}
