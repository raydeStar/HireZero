namespace HireZero.Core;

// Members, roles and the one authorization table (docs/adr/0020-members-roles-and-sign-in.md).
public static class CompanyRoles
{
    public const string Owner = "owner",
        Approver = "approver",
        Viewer = "viewer";

    public static readonly string[] All = [Owner, Approver, Viewer];

    public static int Rank(string role) =>
        role switch
        {
            Owner => 3,
            Approver => 2,
            Viewer => 1,
            _ => 0,
        };
}

// Who is acting: a member's stable subject and role in this company.
public sealed record CompanyActor(string Subject, string Role)
{
    public const string System = "system";
}

// A company member. Id is the identity provider's issuer and subject once they have signed in, or "invite:<email>"
// while the invitation waits. State is invited, active or revoked.
public sealed record CompanyMember(
    string Id,
    string Email,
    string Name,
    string Role,
    string State,
    int Version = 1,
    DateTimeOffset? InviteExpiresAt = null
);

public static class CompanyAccess
{
    // Structure, budgets, checkout grants, integrations and members are the owner's alone.
    private static readonly HashSet<string> OwnerOnly =
    [
        "decision.prepare",
        "decision.run",
        "decision.cancel",
        "decision.reconcile",
        "company.work.configure",
        "billing.prepare",
        "billing.checkout",
        "billing.portal",
        "department.create",
        "schedule.save",
        "schedule.review",
        "notification.preferences",
        "notification.close",
        "employee.hire",
        "goal.prepare",
        "goal.context",
        "goal.lookup.start",
        "employee.update",
        "employee.lifecycle",
        "employee.personality",
        "knowledge.save",
        "document.save",
        "document.review",
        "document.delete",
        "document.source.delete",
        "document.retention",
        "memory.save",
        "memory.keep",
        "proposal.feedback",
        "memory.review",
        "memory.delete",
        "memory.source.delete",
        "memory.analysis.prepare",
        "memory.analysis.run",
        "memory.analysis.reconcile",
        "memory.analysis.revise",
        "context.draft",
        "business.identify",
        "business.confirm",
        "business.profile.save",
        "business.profile.review",
        "business.draft.prepare",
        "business.lookup.start",
        "business.refresh.start",
        "business.fact.review",
        "business.draft.run",
        "business.draft.reconcile",
        "connection.resolve",
        "connection.dismiss",
        "budget.configure",
        "cost.save",
        "cost.review",
        "usage.configure",
        "compute.configure",
        "browser.prepare",
        "browser.run",
        "browser.revoke",
        "browser.output.prepare",
        "browser.captures.delete",
        "browser.retention",
        "sandbox.prepare",
        "sandbox.run",
        "sandbox.revoke",
        "sandbox.output.prepare",
        "workspace.grant",
        "member.invite",
        "member.role",
        "member.revoke",
    ];

    // A viewer can mark their own inbox alerts or leave their free team. Work changes need an approver or owner.
    public static string Required(string action) =>
        action is "notification.mark" or "member.leave" ? CompanyRoles.Viewer
        : OwnerOnly.Contains(action) ? CompanyRoles.Owner
        : CompanyRoles.Approver;

    // Execution steps (prepare, run, propose, publish, reconcile) and employee actions are an approver's.
    public const string Steps = CompanyRoles.Approver;

    public static bool Allows(CompanyActor actor, string required) =>
        CompanyRoles.Rank(actor.Role) >= CompanyRoles.Rank(required) && CompanyRoles.Rank(required) > 0;

    public static void Demand(CompanyActor actor, string required, string what)
    {
        if (!Allows(actor, required))
            throw new WorkspaceException(
                $"Your role ({actor.Role}) cannot {what}; it needs {required}. Nothing changed.",
                403
            );
    }
}
