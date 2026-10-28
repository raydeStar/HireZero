using System.Text;

namespace HireZero.Core;

public sealed record CompanyDecisionInput(
    string Model,
    string StateJson,
    string Instructions,
    Dictionary<string, string> Criteria
);

public sealed record CompanyDecisionPacket(
    string CompanyId,
    string Provider,
    string Model,
    string Json,
    string TransportDigest,
    string[] Choices,
    CompanyModelFunding Funding
);

public sealed record CompanyDecisionOutcome(
    string State,
    string? Choice = null,
    double? Confidence = null,
    Dictionary<string, double>? Probabilities = null,
    ModelUsage? Usage = null,
    string? Receipt = null,
    string? ResolvedModel = null,
    decimal? ReportedCostUsd = null
);

public sealed record CompanyDecisionReconciliation(
    string Receipt,
    ModelUsage Usage,
    bool ProviderStopped,
    string Evidence
);

public sealed record CompanyDecision(
    string Id,
    CompanyDecisionPacket Packet,
    decimal AllowanceCents,
    long PlanningTokens,
    DateTimeOffset ExpiresAt,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    string? AuthorizedBy = null,
    bool DispatchTaken = false,
    CompanyDecisionOutcome? Result = null,
    ModelUsage? Usage = null,
    long? ReportedUsage = null,
    decimal? ReportedCostUsd = null,
    string? ProviderReceipt = null,
    string? Error = null
)
{
    public string Kind => "decision";
}

public sealed partial class CompanyStore
{
    public const long DecisionPlanningTokens = 20000;
    public Func<CompanyDecisionInput, CompanyDecisionPacket> PrepareDecisionPacket { get; init; } =
        _ => throw new WorkspaceException("A company-bound decision provider is not configured.", 503);
    public Func<CompanyDecisionPacket, bool> CurrentDecisionPacket { get; init; } = _ => false;
    private const string DecisionSchema =
        "CREATE TABLE co_decisions(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));";

    private string DecisionOwner(StoreConnection db)
    {
        var subject = CurrentActor.Value?.Subject;
        if (Find<CompanyMember>(db, subject) is not { State: "active", Role: CompanyRoles.Owner })
            throw new WorkspaceException("An active owner must authorize this decision request.", 403);
        return subject!;
    }

    public CompanyDecision Decision(string id)
    {
        using var db = Open();
        return Need<CompanyDecision>(db, id);
    }

    public object Decisions()
    {
        using var db = Open();
        return All<CompanyDecision>(db)
            .OrderByDescending(d => d.UpdatedAt)
            .Take(100)
            .Select(d => new
            {
                d.Id,
                d.State,
                d.Version,
                d.UpdatedAt,
                d.Packet.Model,
            })
            .ToArray();
    }

    public object DecisionReview(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var decision = Need<CompanyDecision>(db, id);
                return new { decision, current = decision.ExpiresAt > Clock() && DecisionCurrent(db, decision) };
            }
        );
    }

    public bool DecisionIsCurrent(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var d = Need<CompanyDecision>(db, id);
                return d.State == "running" && d.DispatchTaken && d.ExpiresAt > Clock() && DecisionCurrent(db, d);
            }
        );
    }

    public bool IsDecisionOperation(string id)
    {
        using var db = Open();
        return Scalar(
                db,
                "SELECT json_extract(result,'$.kind') FROM co_operations WHERE company_id=$0 AND id=$1",
                CompanyId,
                id
            ) == "decision";
    }

    private static string DecisionDigest(CompanyDecision d) =>
        CompanyJson.Digest(
            new
            {
                d.Id,
                d.Packet,
                d.AllowanceCents,
                d.PlanningTokens,
                d.ExpiresAt,
                policy = "reviewed-decision-v1",
                localDeadlineSeconds = 30,
                providerHardSpendCap = false,
            }
        );

    private bool DecisionCurrent(StoreConnection db, CompanyDecision d) =>
        d.Packet.CompanyId == CompanyId
        && d.Digest == DecisionDigest(d)
        && d.PlanningTokens == DecisionPlanningTokens
        && (
            d.AuthorizedBy is null
            || Find<CompanyMember>(db, d.AuthorizedBy) is { State: "active", Role: CompanyRoles.Owner }
        )
        && CurrentDecisionPacket(d.Packet);

    private object PrepareDecision(StoreConnection db, CompanyCommand c)
    {
        DecisionOwner(db);
        if (c.CreditAllowance is < 0.01m or > 100m || c.Body is null || Encoding.UTF8.GetByteCount(c.Body) > 32768)
            throw new WorkspaceException(
                "Provide a bounded decision and an intended allowance from 0.01 to 100 US cents.",
                422
            );
        var input = CompanyJson.Decode<CompanyDecisionInput>(c.Body);
        var packet = PrepareDecisionPacket(input);
        if (
            packet.CompanyId != CompanyId
            || string.IsNullOrWhiteSpace(packet.Provider)
            || packet.Provider.Length > 100
            || string.IsNullOrWhiteSpace(packet.Model)
            || packet.Model.Length > 192
            || packet.Json is null
            || Encoding.UTF8.GetByteCount(packet.Json) is < 2 or > 16384
            || string.IsNullOrWhiteSpace(packet.TransportDigest)
            || packet.TransportDigest.Length > 160
            || packet.Choices is null
            || packet.Choices.Length is < 2 or > 8
            || packet.Choices.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 32)
            || packet.Choices.Distinct(StringComparer.Ordinal).Count() != packet.Choices.Length
            || packet.Funding is null
            || packet.Funding.Source is not ("company-key" or "host")
            || packet.Funding.Provider != packet.Provider
            || string.IsNullOrWhiteSpace(packet.Funding.ConnectionRevision)
            || packet.Funding.ConnectionRevision.Length > 200
            || !CurrentDecisionPacket(packet)
        )
            throw new WorkspaceException("The decision provider did not supply a current company-bound packet.", 409);
        var d = new CompanyDecision(
            Id(),
            packet,
            c.CreditAllowance,
            DecisionPlanningTokens,
            Clock().AddMinutes(10),
            "",
            "prepared",
            1,
            Clock()
        );
        d = d with { Digest = DecisionDigest(d) };
        Save(db, d, d.Id, state: d.State, version: d.Version);
        Event(
            db,
            "decision.prepared",
            d.Id,
            null,
            "An exact advisory decision request is ready for owner review. No provider call is authorized."
        );
        return d;
    }

    private object StartDecision(StoreConnection db, CompanyCommand c)
    {
        var owner = DecisionOwner(db);
        var d = Need<CompanyDecision>(db, c.Id);
        Version(d.Version, c.ExpectedVersion);
        if (
            d.State != "prepared"
            || d.ExpiresAt <= Clock()
            || c.Digest != d.Digest
            || c.Decision != "accept-soft-limits"
            || !DecisionCurrent(db, d)
        )
            throw new WorkspaceException(
                "Authorize this exact unexpired decision and its soft allowance, or prepare a fresh review.",
                409
            );
        if (OwnerModelProviderHeld(db) || All<CompanyAttempt>(db).Any(ProviderHeld))
            throw new WorkspaceException(
                "Resolve the running or uncertain provider request before starting another.",
                409
            );
        var company = Find<CompanyBudget>(db, "company-total") ?? new("company-total", CompanyBudgetCeilings.Company);
        if ((decimal)company.Used + company.Reserved + d.PlanningTokens > company.Limit)
            throw new WorkspaceException("The company allowance cannot cover this decision.", 409);
        ReserveUsage(db, d.Id, "decision", null, null, d.Packet.Model, d.PlanningTokens, d.Packet.Funding);
        Save(db, new CompanyBudget(d.Id, d.PlanningTokens, Reserved: d.PlanningTokens), d.Id);
        Save(
            db,
            company with
            {
                Reserved = company.Reserved + d.PlanningTokens,
                Version = company.Version + 1,
            },
            company.Id,
            version: company.Version + 1
        );
        Save(db, new CompanyReservation(d.Id, d.Id, d.PlanningTokens, Scopes: [d.Id, "company-total"]), d.Id, d.Id);
        d = d with { State = "running", AuthorizedBy = owner, Version = d.Version + 1, UpdatedAt = Clock() };
        Save(db, d, d.Id, state: d.State, version: d.Version);
        Event(
            db,
            "decision.authorized",
            d.Id,
            null,
            "The owner authorized one advisory decision. No downstream action is authorized."
        );
        return d;
    }

    // The worker takes this durable claim before contacting its provider. Replayed command receipts cannot claim twice.
    public CompanyDecision? TakeDecisionDispatch(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var d = Need<CompanyDecision>(db, id);
                if (d.State != "running" || d.DispatchTaken)
                    return null;
                if (d.ExpiresAt <= Clock() || !DecisionCurrent(db, d) || WorkControl(db).Paused)
                {
                    Release(db, d.Id, 0);
                    SaveDecisionOutcome(
                        db,
                        d with
                        {
                            State = "not_sent",
                            Usage = new(0, 0, BilledUsd: 0),
                            ReportedUsage = 0,
                            ReportedCostUsd = 0,
                            Error = "Authority changed before dispatch; no provider call was made.",
                        }
                    );
                    return null;
                }
                d = d with { DispatchTaken = true, Version = d.Version + 1, UpdatedAt = Clock() };
                Save(db, d, d.Id, state: d.State, version: d.Version);
                Event(db, "decision.dispatched", d.Id, null, "The one-shot decision dispatch claim was consumed.");
                return d;
            }
        );
    }

    private object CancelDecision(StoreConnection db, CompanyCommand c)
    {
        DecisionOwner(db);
        var d = Need<CompanyDecision>(db, c.Id);
        Version(d.Version, c.ExpectedVersion);
        if (d.State != "prepared" || c.Digest != d.Digest)
            throw new WorkspaceException(
                "Only an exact unstarted decision can be canceled without reconciliation.",
                409
            );
        SaveDecisionOutcome(db, d with { State = "canceled" });
        return Need<CompanyDecision>(db, d.Id);
    }

    private void SaveDecisionOutcome(StoreConnection db, CompanyDecision d)
    {
        d = d with { Version = d.Version + 1, UpdatedAt = Clock() };
        Save(db, d, d.Id, state: d.State, version: d.Version);
        SyncUsage(db, d.Id);
        Event(
            db,
            "decision." + d.State,
            d.Id,
            null,
            d.State == "completed"
                ? "An advisory decision is ready. No downstream work was authorized."
                : "Decision status recorded. Uncertain usage is retained; this request is never replayed."
        );
    }
}
