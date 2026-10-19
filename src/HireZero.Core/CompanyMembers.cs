using System.Net.Mail;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Company members (docs/adr/0020-members-roles-and-sign-in.md): invited by email, bound to an identity provider's
// subject on first sign-in, with one role each. A company always keeps an active owner.
public sealed partial class CompanyStore
{
    private readonly string _owner;
    private static readonly AsyncLocal<CompanyActor?> CurrentActor = new();

    // An active member acting now; anyone else is refused before anything is read or changed.
    public CompanyActor Actor(string subject)
    {
        using var db = Open();
        var member = Find<CompanyMember>(db, Text(subject, 400));
        return member is { State: "active" }
            ? new CompanyActor(member.Id, member.Role)
            : throw new WorkspaceException("You are not a member of this company.", 403);
    }

    public CompanyMember[] Members()
    {
        using var db = Open();
        return
        [
            .. All<CompanyMember>(db).OrderBy(m => m.State != "active").ThenBy(m => m.Email, StringComparer.Ordinal),
        ];
    }

    // Someone signed in: their bound membership, or the invitation for their verified email, which then binds to
    // their subject. Returns null for anyone the company has not invited.
    public CompanyActor? SignIn(string subject, string? email, bool emailVerified, string? name)
    {
        subject = Text(subject, 400);
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                if (Find<CompanyMember>(db, subject) is { } bound)
                    return bound.State == "active" ? new CompanyActor(bound.Id, bound.Role) : null;
                if (!emailVerified || Normal(email) is not { } address)
                    return null;
                var invite = Find<CompanyMember>(db, "invite:" + address);
                if (invite is not { State: "invited" })
                    return null;
                var member = new CompanyMember(subject, address, Text(name ?? address, 120), invite.Role, "active");
                Save(db, member, member.Id, state: member.State, version: member.Version);
                Sql(db, "DELETE FROM co_members WHERE company_id=$0 AND id=$1", CompanyId, invite.Id);
                var previous = CurrentActor.Value;
                CurrentActor.Value = new CompanyActor(member.Id, member.Role);
                Event(db, "member.joined", member.Id, null, member.Email + " joined as " + member.Role + ".");
                CurrentActor.Value = previous;
                return new CompanyActor(member.Id, member.Role);
            }
        );
    }

    // member.invite: Body is the email, Decision the role.
    private CompanyMember InviteMember(SqliteConnection db, CompanyCommand c)
    {
        var address = Normal(c.Body) ?? throw new WorkspaceException("Invite a valid email address.", 422);
        if (!CompanyRoles.All.Contains(c.Decision))
            throw new WorkspaceException("Choose owner, approver or viewer.", 422);
        if (All<CompanyMember>(db).Any(m => m.State != "revoked" && m.Email == address))
            throw new WorkspaceException("That email is already a member or invited.");
        var invite = new CompanyMember("invite:" + address, address, address, c.Decision!, "invited");
        Save(db, invite, invite.Id, state: invite.State, version: invite.Version);
        Event(db, "member.invited", invite.Id, null, address + " was invited as " + invite.Role + ".");
        return invite;
    }

    // member.role (Decision is the new role) and member.revoke; the company always keeps an active owner.
    private CompanyMember ChangeMember(SqliteConnection db, CompanyCommand c, bool revoke)
    {
        var member = Need<CompanyMember>(db, Text(c.Id, 400));
        Version(member.Version, c.ExpectedVersion);
        if (!revoke && !CompanyRoles.All.Contains(c.Decision))
            throw new WorkspaceException("Choose owner, approver or viewer.", 422);
        var changed = revoke
            ? member with
            {
                State = "revoked",
                Version = member.Version + 1,
            }
            : member with
            {
                Role = c.Decision!,
                Version = member.Version + 1,
            };
        var owners = All<CompanyMember>(db)
            .Count(m => m.Id != member.Id && m.State == "active" && m.Role == CompanyRoles.Owner);
        if (owners == 0 && !(changed.State == "active" && changed.Role == CompanyRoles.Owner))
            throw new WorkspaceException("The company must keep at least one active owner.");
        Save(db, changed, changed.Id, state: changed.State, version: changed.Version);
        Event(
            db,
            revoke ? "member.revoked" : "member.changed",
            changed.Id,
            null,
            changed.Email + (revoke ? " was removed." : " is now " + changed.Role + ".")
        );
        return changed;
    }

    private static string? Normal(string? email)
    {
        var text = (email ?? "").Trim().ToLowerInvariant();
        try
        {
            return text.Length is > 3 and <= 254 && new MailAddress(text).Address == text ? text : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
