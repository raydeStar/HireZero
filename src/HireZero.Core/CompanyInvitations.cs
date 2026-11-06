using System.Security.Cryptography;
using System.Text;

namespace HireZero.Core;

public sealed record CompanyInvitation(CompanyMember Member, string Token);

public sealed record CompanyInvitationPreview(
    string CompanyId,
    string CompanyName,
    string Role,
    int Version,
    string Invitation
);

// Consent belongs to a particular invitation, never to the act of signing in (ADR 0182).
public sealed partial class CompanyStore
{
    private const string InvitationSchema =
        "CREATE TABLE co_member_invites(company_id TEXT NOT NULL,email TEXT NOT NULL,token_hash TEXT NOT NULL UNIQUE,expires_at TEXT NOT NULL,used_by TEXT,PRIMARY KEY(company_id,email),FOREIGN KEY(company_id) REFERENCES co_company(id));";

    public bool SignupInvitesEnabled { get; init; }

    public static string InvitationHash(string token)
    {
        if (token is null || token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c)))
            throw new WorkspaceException("This invitation is invalid, expired or no longer available.", 410);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    // The caller may return this secret once, but must never put it in a durable operation receipt.
    public CompanyInvitation InviteSignupMember(string subject, string email, string role, int expectedVersion = 0)
    {
        var address = Normal(email) ?? throw new WorkspaceException("Invite a valid email address.", 422);
        if (role is not (CompanyRoles.Approver or CompanyRoles.Viewer))
            throw new WorkspaceException("Free teams can invite approvers or viewers.", 422);
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                DemandSignupInvites(db);
                var actor = Need<CompanyMember>(db, subject);
                if (actor.State != "active")
                    throw new WorkspaceException("You are not a member of this company.", 403);
                CompanyAccess.Demand(new(subject, actor.Role), CompanyRoles.Owner, "invite members");
                if (All<CompanyMember>(db).Any(m => m.State == "active" && m.Email == address))
                    throw new WorkspaceException("That email is already a member.", 409);
                var prior = Find<CompanyMember>(db, "invite:" + address);
                Version(prior?.Version ?? 0, expectedVersion);
                var now = Clock().ToUniversalTime();
                var pending = int.Parse(
                    Scalar(
                        db,
                        "SELECT count(*) FROM co_member_invites WHERE company_id=$0 AND email<>$1 AND used_by IS NULL AND expires_at>$2",
                        CompanyId,
                        address,
                        now.ToString("O")
                    )!
                );
                if (pending >= 5)
                    throw new WorkspaceException(
                        "This company already has five pending invitations. Revoke one or wait for it to expire.",
                        409
                    );
                var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
                var expiry = now.AddDays(7);
                var member = new CompanyMember(
                    "invite:" + address,
                    address,
                    address,
                    role,
                    "invited",
                    (prior?.Version ?? 0) + 1,
                    expiry
                );
                Save(db, member, member.Id, state: member.State, version: member.Version);
                Sql(
                    db,
                    "INSERT INTO co_member_invites(company_id,email,token_hash,expires_at,used_by) VALUES($0,$1,$2,$3,NULL) ON CONFLICT(company_id,email) DO UPDATE SET token_hash=$2,expires_at=$3,used_by=NULL",
                    CompanyId,
                    address,
                    InvitationHash(token),
                    expiry.ToString("O")
                );
                var previous = CurrentActor.Value;
                try
                {
                    CurrentActor.Value = new(subject, actor.Role);
                    Event(
                        db,
                        prior is null ? "member.invited" : "member.invitation-replaced",
                        member.Id,
                        null,
                        address + " was invited as " + role + "."
                    );
                }
                finally
                {
                    CurrentActor.Value = previous;
                }
                return new CompanyInvitation(member, token);
            }
        );
    }

    // Directory lookup exposes no invitee address; knowing the link still grants no membership.
    public string InvitationCompany(string token)
    {
        using var db = Open();
        return Scalar(db, "SELECT company_id FROM co_member_invites WHERE token_hash=$0", InvitationHash(token))
            ?? throw new WorkspaceException("This invitation is invalid, expired or no longer available.", 410);
    }

    public CompanyInvitationPreview PreviewInvitation(string token)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var member = PendingInvitation(db, token);
                return new CompanyInvitationPreview(
                    CompanyId,
                    Scalar(db, "SELECT name FROM co_company WHERE id=$0", CompanyId) ?? DefaultName,
                    member.Role,
                    member.Version,
                    InvitationHash(token)
                );
            }
        );
    }

    public bool InvitationMatches(string token, string? email, bool verified)
    {
        using var db = Open();
        return Transaction(
            db,
            () => verified && Normal(email) is { } address && PendingInvitation(db, token).Email == address
        );
    }

    public CompanyCreation AcceptInvitation(
        string token,
        string subject,
        string? email,
        bool verified,
        string? name,
        int expectedVersion
    )
    {
        subject = Text(subject, 400);
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var invite = PendingInvitation(db, token);
                if (!verified || Normal(email) != invite.Email)
                    throw new WorkspaceException(
                        "This invite is for a different verified account. Sign in with the account that was invited.",
                        403
                    );
                Version(invite.Version, expectedVersion);
                var existing = Find<CompanyMember>(db, subject);
                if (
                    existing is { State: "active" }
                    || All<CompanyMember>(db).Any(m => m.State == "active" && m.Email == invite.Email)
                )
                    throw new WorkspaceException(
                        "This account is already a member of the company. Open it from your companies.",
                        409
                    );
                var member = new CompanyMember(
                    subject,
                    invite.Email,
                    Text(name ?? invite.Email, 120),
                    invite.Role,
                    "active",
                    (existing?.Version ?? 0) + 1
                );
                Save(db, member, member.Id, state: member.State, version: member.Version);
                RecordVerifiedEmail(db, member, email, verified);
                Sql(db, "DELETE FROM co_members WHERE company_id=$0 AND id=$1", CompanyId, invite.Id);
                Sql(
                    db,
                    "UPDATE co_member_invites SET used_by=$2 WHERE company_id=$0 AND token_hash=$1",
                    CompanyId,
                    InvitationHash(token),
                    subject
                );
                var previous = CurrentActor.Value;
                try
                {
                    CurrentActor.Value = new(subject, member.Role);
                    Event(db, "member.joined", member.Id, null, member.Email + " joined as " + member.Role + ".");
                }
                finally
                {
                    CurrentActor.Value = previous;
                }
                return new CompanyCreation(
                    CompanyId,
                    Scalar(db, "SELECT name FROM co_company WHERE id=$0", CompanyId) ?? DefaultName,
                    member.Role
                );
            }
        );
    }

    private void DemandSignupInvites(StoreConnection db)
    {
        if (!SignupInvitesEnabled || !SignupCompany(db))
            throw new WorkspaceException("Invitations for free companies are currently unavailable.", 403);
    }

    private CompanyMember PendingInvitation(StoreConnection db, string token)
    {
        DemandSignupInvites(db);
        var address = Scalar(
            db,
            "SELECT email FROM co_member_invites WHERE company_id=$0 AND token_hash=$1 AND used_by IS NULL AND expires_at>$2",
            CompanyId,
            InvitationHash(token),
            Clock().ToUniversalTime().ToString("O")
        );
        if (address is null || Find<CompanyMember>(db, "invite:" + address) is not { State: "invited" } member)
            throw new WorkspaceException("This invitation is invalid, expired or no longer available.", 410);
        if (member.Role is not (CompanyRoles.Approver or CompanyRoles.Viewer))
            throw new WorkspaceException("This invitation is no longer available. Ask for a new link.", 410);
        return member;
    }

    private CompanyMember LeaveCompany(StoreConnection db, CompanyCommand command)
    {
        var subject = CurrentActor.Value!.Subject;
        if (!SignupCompany(db) || command.Id is { } id && id != subject)
            throw new WorkspaceException("You can leave only your own membership in a free company.", 403);
        var member = Need<CompanyMember>(db, subject);
        Version(member.Version, command.ExpectedVersion);
        if (member.State != "active" || member.Role == CompanyRoles.Owner)
            throw new WorkspaceException("The owner must remain in the company.", 403);
        var left = member with { State = "revoked", Version = member.Version + 1 };
        Save(db, left, left.Id, state: left.State, version: left.Version);
        Sql(db, "DELETE FROM co_verified_emails WHERE company_id=$0 AND id=$1", CompanyId, subject);
        Event(db, "member.left", subject, null, member.Email + " left the company.");
        return left;
    }
}
