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
public sealed record CompanyMember(string Id, string Email, string Name, string Role, string State, int Version = 1);

public static class CompanyAccess
{
    // Structure, budgets, checkout grants, integrations and members are the owner's alone.
    private static readonly HashSet<string> OwnerOnly =
    [
        "department.create",
        "employee.hire",
        "employee.update",
        "employee.lifecycle",
        "knowledge.save",
        "context.draft",
        "connection.resolve",
        "connection.dismiss",
        "budget.configure",
        "workspace.grant",
        "member.invite",
        "member.role",
        "member.revoke",
    ];

    // The least role a company command needs. Every command changes something, so a viewer runs none.
    public static string Required(string action) =>
        OwnerOnly.Contains(action) ? CompanyRoles.Owner : CompanyRoles.Approver;

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
