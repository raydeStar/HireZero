namespace HireZero.Core;

public sealed record CompanyWorkControl(bool Paused, int Version, DateTimeOffset UpdatedAt);

public sealed partial class CompanyStore
{
    private CompanyWorkControl WorkControl(StoreConnection db)
    {
        var policy = UsagePolicy(db);
        return new(policy.WorkPaused, policy.Version, policy.UpdatedAt);
    }

    public CompanyWorkControl WorkControl()
    {
        using var db = Open();
        return WorkControl(db);
    }

    // Admission and pause use the same company transaction. Already admitted work keeps its receipts and authority.
    private void DemandWorkAdmission(StoreConnection db)
    {
        if (UsagePolicy(db).WorkPaused)
            throw new WorkspaceException(
                "New work is paused for this company. An owner can resume it in Companies. Previously admitted work may still finish.",
                409
            );
    }

    private CompanyWorkControl ConfigureWorkControl(StoreConnection db, CompanyCommand command)
    {
        if (
            CurrentActor.Value is not { } actor
            || Find<CompanyMember>(db, actor.Subject) is not { State: "active", Role: CompanyRoles.Owner }
        )
            throw new WorkspaceException("Only a current company owner may pause or resume new work.", 403);
        var current = UsagePolicy(db);
        Version(current.Version, command.ExpectedVersion);
        if (command.Decision is not ("pause-new-work" or "resume-new-work"))
            throw new WorkspaceException("Choose whether to pause or resume new work for this company.", 422);
        var paused = command.Decision == "pause-new-work";
        if (paused == current.WorkPaused)
            return WorkControl(db);
        var next = current with { WorkPaused = paused, Version = current.Version + 1, UpdatedAt = Clock() };
        Save(db, next, next.Id, version: next.Version);
        Event(
            db,
            paused ? "company.work-paused" : "company.work-resumed",
            next.Id,
            null,
            paused
                ? "Owner paused new work. Previously admitted work and recovery may continue."
                : "Owner resumed new work. Existing approvals, limits and expiry checks still apply."
        );
        return WorkControl(db);
    }
}
