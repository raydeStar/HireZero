using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// The bounds of one execution run, approved with its grant: the reservation is sized from Tokens, the run is stopped
// after Seconds, and Effort is passed to the coding runtime. Defaults are the host's; Ceiling is a sanity bound.
public sealed record CompanyExecutionLimits(int Tokens, int Seconds, string Effort)
{
    public static readonly CompanyExecutionLimits Pilot = new(5000, 20, "low");

    // The owner's chosen defaults for a fix run (2026-10-17): 200,000 tokens, 15 minutes, medium effort.
    public static readonly CompanyExecutionLimits Default = new(200_000, 900, "medium");
    public static readonly CompanyExecutionLimits Ceiling = new(1_000_000, 3600, "high");

    public bool Within() =>
        Tokens > 0
        && Tokens <= Ceiling.Tokens
        && Seconds > 0
        && Seconds <= Ceiling.Seconds
        && Effort is "low" or "medium" or "high";
}

public sealed record CompanyWorkspaceProposal(
    string AttemptId,
    string Workspace,
    string Revision,
    string Fingerprint,
    string ContextDigest,
    string Route,
    bool AllowWrite = false,
    CompanyExecutionLimits? Limits = null,
    bool AllowNetwork = false
);

public sealed record CompanyExecutionGrant(
    string Id,
    string AttemptId,
    string TaskId,
    string EmployeeId,
    string Workspace,
    string Revision,
    string Fingerprint,
    string ContextDigest,
    string Route,
    bool AllowWrite,
    string Digest,
    string State,
    DateTimeOffset ExpiresAt,
    string? RunId = null,
    int Version = 1,
    CompanyRepositoryReview? Review = null,
    CompanyExecutionLimits? Limits = null,
    DateTimeOffset? DispatchedAt = null,
    bool AllowNetwork = false
)
{
    // Grants recorded before limits were approved with them ran under the pilot limits.
    public CompanyExecutionLimits Bounds => Limits ?? CompanyExecutionLimits.Pilot;
}

public sealed record CompanyRepositorySnapshot(string Workspace, string Revision, string Fingerprint);

public sealed record CompanyFileDiff(string Path, string Change, string Patch, string BeforeSha256, string AfterSha256);

public sealed record CompanyRepositoryReview(
    string BaseRevision,
    string Fingerprint,
    CompanyFileDiff[] Files,
    string Digest = ""
);

public interface ICompanyCodingFactory
{
    // True only for a runner the host has qualified: the synthetic fixture in tests, or a pinned, verified runtime.
    bool Qualified { get; }
    ICodingRunner Create(CompanyExecutionGrant grant, Func<bool> mayStart);
}

public sealed partial class CompanyStore
{
    public CompanyExecutionGrant ProposeExecution(CompanyWorkspaceProposal proposal)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var a = Need<CompanyAttempt>(db, proposal.AttemptId);
                if (a.Phase != "claimed" || !Current(db, a) || a.ContextId is null)
                    throw new WorkspaceException("A current admitted attempt is required.");
                var task = Need<CompanyTask>(db, a.TaskId);
                var employee = Need<CompanyEmployee>(db, a.EmployeeId);
                var workspace = CanonicalWorkspace(proposal.Workspace);
                if (
                    !Role(employee.RoleId).WorkspaceClaims
                    || !Role(employee.RoleId).ExecutionSkills.Contains(task.Skill)
                    || task.Resource != workspace
                    || proposal.ContextDigest != CompanyJson.Digest(Need<CompanyContext>(db, a.ContextId))
                )
                    throw new WorkspaceException("The workspace or context does not match owner-scoped work.", 403);
                // Network access only as the owner granted it with the checkout; it is part of the approved digest.
                if (proposal.AllowNetwork && !task.AllowNetwork)
                    throw new WorkspaceException("Network access was not granted for this work.", 403);
                Text(proposal.Revision, 80);
                Text(proposal.Fingerprint, 80);
                Text(proposal.Route, 100);
                var limits = proposal.Limits ?? ExecutionDefaults;
                if (!limits.Within())
                    throw new WorkspaceException("Execution limits are outside the allowed bounds.", 422);
                proposal = proposal with { Limits = limits };
                if (All<CompanyExecutionGrant>(db).Any(g => g.AttemptId == a.Id))
                    throw new WorkspaceException("This attempt already has an execution proposal.");
                var id = Id();
                var digest = CompanyJson.Digest(proposal);
                var grant = new CompanyExecutionGrant(
                    id,
                    a.Id,
                    task.Id,
                    employee.Id,
                    workspace,
                    proposal.Revision,
                    proposal.Fingerprint,
                    proposal.ContextDigest,
                    proposal.Route,
                    proposal.AllowWrite,
                    digest,
                    "proposed",
                    Clock().AddMinutes(10),
                    Limits: limits,
                    AllowNetwork: proposal.AllowNetwork
                );
                Save(db, grant, id, task.Id, employee.Id, grant.State);
                Event(
                    db,
                    "execution.proposed",
                    id,
                    employee.Id,
                    "An exact workspace execution proposal requires owner approval. Live execution remains unavailable."
                );
                return grant;
            }
        );
    }

    private CompanyExecutionGrant ReviewExecution(SqliteConnection db, CompanyCommand command)
    {
        var g = Need<CompanyExecutionGrant>(db, command.Id);
        Version(g.Version, command.ExpectedVersion);
        if (command.Decision == "revoke")
        {
            g = g with { State = "revoked", Version = g.Version + 1 };
            Save(db, g, g.Id, g.TaskId, g.EmployeeId, g.State, g.Version);
            Stop(db, Need<CompanyTask>(db, g.TaskId), "canceled", "Owner revoked the execution grant.");
            return g;
        }
        var a = Need<CompanyAttempt>(db, g.AttemptId);
        if (
            command.Decision != "approve"
            || g.State != "proposed"
            || g.Digest != command.Digest
            || Clock() >= g.ExpiresAt
            || !Current(db, a)
            || a.ContextId is null
            || g.ContextDigest != CompanyJson.Digest(Need<CompanyContext>(db, a.ContextId))
        )
            throw new WorkspaceException("Approve the exact current, unexpired execution proposal.");
        g = g with { State = "approved", Version = g.Version + 1 };
        Save(db, g, g.Id, g.TaskId, g.EmployeeId, g.State, g.Version);
        // Approving the exact limits is the work's allowance for this run; company, department and employee budgets
        // still cap it.
        var task = Need<CompanyTask>(db, g.TaskId);
        var root = Need<CompanyBudget>(db, task.RootId);
        if (root.Limit < root.Used + root.Reserved + g.Bounds.Tokens)
        {
            root = root with { Limit = root.Used + root.Reserved + g.Bounds.Tokens, Version = root.Version + 1 };
            Save(db, root, root.Id, version: root.Version);
            Event(
                db,
                "budget.changed",
                root.Id,
                g.EmployeeId,
                "The approved execution limits set this work's allowance."
            );
        }
        Event(
            db,
            "execution.approved",
            g.Id,
            g.EmployeeId,
            "Owner approved one exact execution scope; the runtime qualification gate still applies."
        );
        return g;
    }

    public CompanyExecutionGrant Execution(string id)
    {
        using var db = Open();
        return Need<CompanyExecutionGrant>(db, id);
    }

    public CompanyExecutionGrant? FindExecution(string id)
    {
        using var db = Open();
        return Find<CompanyExecutionGrant>(db, id);
    }

    public CompanyTask Work(string id)
    {
        using var db = Open();
        return Need<CompanyTask>(db, id);
    }

    public CompanyContext ExecutionContext(string id)
    {
        using var db = Open();
        var g = Need<CompanyExecutionGrant>(db, id);
        var a = Need<CompanyAttempt>(db, g.AttemptId);
        return Need<CompanyContext>(db, a.ContextId);
    }

    public bool ExecutionCurrent(string id)
    {
        using var db = Open();
        var g = Need<CompanyExecutionGrant>(db, id);
        var a = Need<CompanyAttempt>(db, g.AttemptId);
        // An approved grant must start before its proposal expires; a dispatched run may last its approved time.
        var until =
            g.State == "dispatched" && g.DispatchedAt is { } started
                ? started.AddSeconds(g.Bounds.Seconds + 60)
                : g.ExpiresAt;
        return (g.State is "approved" or "dispatched")
            && Clock() < until
            && Current(db, a)
            && a.ContextId is not null
            && g.ContextDigest == CompanyJson.Digest(Need<CompanyContext>(db, a.ContextId))
            && KnowledgeCurrent(db, Need<CompanyContext>(db, a.ContextId), Need<CompanyTask>(db, a.TaskId));
    }

    public bool DispatchExecution(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var g = Need<CompanyExecutionGrant>(db, id);
                var a = Need<CompanyAttempt>(db, g.AttemptId);
                if (
                    g.State != "approved"
                    || Clock() >= g.ExpiresAt
                    || a.ContextId is null
                    || g.ContextDigest != CompanyJson.Digest(Need<CompanyContext>(db, a.ContextId))
                    || !Reserve(db, a, g.Bounds.Tokens)
                )
                    return false;
                g = g with { State = "dispatched", DispatchedAt = Clock(), Version = g.Version + 1 };
                Save(db, g, g.Id, g.TaskId, g.EmployeeId, g.State, g.Version);
                return true;
            }
        );
    }

    public void BindExecution(string id, string runId)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var g = Need<CompanyExecutionGrant>(db, id);
                if (g.State != "dispatched" || g.RunId is not null)
                    throw new WorkspaceException("Execution receipt is already bound or no longer current.");
                Save(
                    db,
                    g with
                    {
                        RunId = Text(runId, 80),
                        Version = g.Version + 1,
                    },
                    g.Id,
                    g.TaskId,
                    g.EmployeeId,
                    g.State,
                    g.Version + 1
                );
                return true;
            }
        );
    }

    public void RecordExecutionOutcome(string id, string state)
    {
        if (state is not ("completed" or "withheld" or "unknown" or "not_sent"))
            throw new WorkspaceException("Unsupported execution outcome.");
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var g = Need<CompanyExecutionGrant>(db, id);
                if (g.State is "completed" or "withheld" or "unknown" or "not_sent" or "revoked")
                    return false;
                Save(
                    db,
                    g with
                    {
                        State = state,
                        Version = g.Version + 1,
                    },
                    g.Id,
                    g.TaskId,
                    g.EmployeeId,
                    state,
                    g.Version + 1
                );
                return true;
            }
        );
    }

    public void RecordExecutionReview(string id, CompanyRepositoryReview review)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var g = Need<CompanyExecutionGrant>(db, id);
                if (
                    g.State != "dispatched"
                    || g.RunId is null
                    || g.Review is not null
                    || !Current(db, Need<CompanyAttempt>(db, g.AttemptId))
                    || review.BaseRevision != g.Revision
                    || review.Digest != CompanyJson.Digest(review with { Digest = "" })
                    || review.Files.Length > 50
                    || review.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                        != review.Files.Length
                    || review.Files.Sum(f => System.Text.Encoding.UTF8.GetByteCount(f.Patch)) > 65536
                    || review.Files.Any(f =>
                        Path.IsPathRooted(f.Path)
                        || f.Change is not ("added" or "modified" or "deleted")
                        || f.BeforeSha256.Length != 64
                        || f.AfterSha256.Length != 64
                        || f.BeforeSha256.Concat(f.AfterSha256).Any(c => !Uri.IsHexDigit(c))
                        || f.Path.Replace('\\', '/').Split('/').Any(p => p is "" or "." or "..")
                    )
                )
                    throw new WorkspaceException("Repository review does not match the current execution scope.");
                Save(
                    db,
                    g with
                    {
                        Review = review,
                        Version = g.Version + 1,
                    },
                    g.Id,
                    g.TaskId,
                    g.EmployeeId,
                    g.State,
                    g.Version + 1
                );
                return true;
            }
        );
    }
}
