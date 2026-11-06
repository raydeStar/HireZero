using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyInvitationTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("company-invitations-");
    private DateTimeOffset _now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private string Path => System.IO.Path.Combine(_root.Path, "company.sqlite");

    private CompanyStore Store(
        string id = CompanyStore.DefaultCompanyId,
        string owner = "owner@example.invalid",
        bool enabled = true
    ) =>
        new(Path, owner, CompanyWorkspace.Definitions(), companyId: id)
        {
            Clock = () => _now,
            SignupInvitesEnabled = enabled,
        };

    private CompanyStore Free(string subject = "founder")
    {
        var root = Store();
        root.ConfigureSignup(new());
        return Store(root.Signup(subject, subject + "@example.invalid", true, subject, new()).Id, subject);
    }

    private static CompanyCommand Command(string action, CompanyMember member) =>
        new(Guid.NewGuid().ToString("D"), action) { Id = member.Id, ExpectedVersion = member.Version };

    private static CompanyCreation Accept(CompanyStore store, CompanyInvitation invite, string subject = "guest") =>
        store.AcceptInvitation(
            invite.Token,
            subject,
            subject + "@example.invalid",
            true,
            "Fictional guest",
            invite.Member.Version
        );

    [Fact]
    public void FlagDefaultsOffAndOnlyOwnersCanIssueViewerOrApproverInvitations()
    {
        Assert.False(CompanySettings.Load(_ => null).SignupInvites);
        Assert.Throws<InvalidOperationException>(() =>
            CompanySettings.Load(n => n == "HIREZERO_SIGNUP_INVITES" ? "yes" : null)
        );
        var enabled = CompanySettings.Load(n => n == "HIREZERO_SIGNUP_INVITES" ? "1" : null);
        Assert.True(enabled.SignupInvites);
        Assert.Throws<InvalidOperationException>(() =>
            CompanyWorkspace.TryCreate(
                [CompanyWorkspace.Flag],
                "owner@example.invalid",
                true,
                _root.Path,
                settings: enabled
            )
        );
        var free = Free();
        Assert.Throws<WorkspaceException>(() =>
            Store(free.CompanyId, "founder", false).InviteSignupMember("founder", "guest@example.invalid", "viewer")
        );
        Assert.Throws<WorkspaceException>(() =>
            Store().InviteSignupMember("owner@example.invalid", "guest@example.invalid", "viewer")
        );
        Assert.Throws<WorkspaceException>(() => free.InviteSignupMember("founder", "guest@example.invalid", "owner"));
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "viewer");
        Accept(free, invite);
        Assert.Throws<WorkspaceException>(() => free.InviteSignupMember("guest", "third@example.invalid", "viewer"));
        var guest = free.Members().Single(m => m.Id == "guest");
        Assert.Throws<WorkspaceException>(() =>
            free.Apply(Command("member.role", guest) with { Decision = "owner" }, "founder", out _)
        );
        Assert.Throws<WorkspaceException>(() =>
            free.Apply(
                new(Guid.NewGuid().ToString("D"), "member.invite")
                {
                    Body = "third@example.invalid",
                    Decision = "viewer",
                },
                "founder",
                out _
            )
        );
    }

    [Fact]
    public void SignInAndPreviewNeverJoinButAnExplicitAcceptanceCommitsOneScopedAuditedMembership()
    {
        var free = Free();
        var other = Free("other");
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "approver");
        Assert.Null(free.SignIn("guest", "guest@example.invalid", true, "Guest"));
        Assert.Equal(free.CompanyId, Store().InvitationCompany(invite.Token));
        Assert.Equal("approver", free.PreviewInvitation(invite.Token).Role);
        Assert.Throws<WorkspaceException>(() => free.Actor("guest"));
        Assert.Throws<WorkspaceException>(() => other.PreviewInvitation(invite.Token));
        Assert.Equal("approver", Accept(free, invite).Role);
        Assert.Equal("approver", free.Actor("guest").Role);
        Assert.Throws<WorkspaceException>(() => other.Actor("guest"));
        Assert.DoesNotContain(free.Members(), m => m.State == "invited");
        var joined = Assert.Single(free.Feed(0), e => e.Type == "member.joined");
        Assert.Equal("guest", joined.Actor);
        Assert.True(free.VerifyAudit().Intact);
        Assert.Throws<WorkspaceException>(() => Accept(free, invite));
        Assert.Single(free.Feed(0), e => e.Type == "member.joined");
    }

    [Fact]
    public void WrongOrUnverifiedAccountsCannotConsumeTheLinkAndExpiryIsExact()
    {
        var free = Free();
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "viewer");
        Assert.Throws<WorkspaceException>(() => Accept(free, invite, "wrong"));
        Assert.Throws<WorkspaceException>(() =>
            free.AcceptInvitation(invite.Token, "guest", "guest@example.invalid", false, "Guest", 1)
        );
        Assert.True(free.InvitationMatches(invite.Token, "GUEST@EXAMPLE.INVALID", true));
        _now = _now.AddDays(7);
        Assert.Equal(410, Assert.Throws<WorkspaceException>(() => Accept(free, invite)).Status);
        Assert.Throws<WorkspaceException>(() => free.Actor("guest"));
        Assert.Throws<WorkspaceException>(() => free.PreviewInvitation("invalid"));
        Assert.Throws<WorkspaceException>(() => CompanyStore.InvitationHash(null!));
    }

    [Fact]
    public void ReplacementRevocationAndPendingRoleChangesInvalidateOldLinks()
    {
        var free = Free();
        var first = free.InviteSignupMember("founder", "guest@example.invalid", "viewer");
        var replacement = free.InviteSignupMember("founder", "guest@example.invalid", "approver", first.Member.Version);
        Assert.Throws<WorkspaceException>(() => Accept(free, first));
        Assert.Throws<WorkspaceException>(() =>
            free.InviteSignupMember("founder", "guest@example.invalid", "viewer", first.Member.Version)
        );
        free.Apply(Command("member.role", replacement.Member) with { Decision = "viewer" }, "founder", out _);
        Assert.Throws<WorkspaceException>(() => Accept(free, replacement));
        var pending = free.Members().Single(m => m.State == "invited");
        var third = free.InviteSignupMember("founder", pending.Email, "viewer", pending.Version);
        free.Apply(Command("member.revoke", third.Member), "founder", out _);
        Assert.Throws<WorkspaceException>(() => Accept(free, third));
        Assert.Contains(free.Feed(0), e => e.Type == "member.revoked");
        Assert.Contains(free.Feed(0), e => e.Type == "member.invitation-replaced");
        Assert.True(free.VerifyAudit().Intact);
    }

    [Fact]
    public async Task FivePendingLimitIsAtomicAndExpiredOrRevokedInvitesReleaseSlots()
    {
        var free = Free();
        var invitations = await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(i =>
                    Task.Run(() =>
                    {
                        try
                        {
                            return free.InviteSignupMember("founder", $"guest{i}@example.invalid", "viewer");
                        }
                        catch (WorkspaceException)
                        {
                            return null;
                        }
                    })
                )
        );
        Assert.Equal(5, invitations.Count(i => i is not null));
        var first = invitations.First(i => i is not null)!;
        // Replacing one of five does not require a sixth slot.
        var replacement = free.InviteSignupMember("founder", first.Member.Email, "viewer", first.Member.Version);
        free.Apply(Command("member.revoke", replacement.Member), "founder", out _);
        free.InviteSignupMember("founder", "replacement@example.invalid", "viewer");
        _now = _now.AddDays(7);
        free.InviteSignupMember("founder", "new@example.invalid", "viewer");
    }

    [Fact]
    public async Task ConcurrentAcceptanceUsesTheTokenExactlyOnce()
    {
        var free = Free();
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "viewer");
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ =>
                    Task.Run(() =>
                    {
                        try
                        {
                            Accept(free, invite);
                            return true;
                        }
                        catch (WorkspaceException)
                        {
                            return false;
                        }
                    })
                )
        );
        Assert.Single(results, result => result);
        Assert.Single(free.Members(), m => m.Id == "guest");
        Assert.Single(free.Feed(0), e => e.Type == "member.joined");
    }

    [Fact]
    public void DisablingInvitationsPreservesMembersButBlocksNewJoinsAndKeepsViewerLeaveAvailable()
    {
        var free = Free();
        Accept(free, free.InviteSignupMember("founder", "guest@example.invalid", "viewer"));
        var pending = free.InviteSignupMember("founder", "next@example.invalid", "viewer");
        var stopped = Store(free.CompanyId, "founder", false);
        Assert.Equal("viewer", stopped.SignIn("guest", "guest@example.invalid", true, "Guest")!.Role);
        Assert.Throws<WorkspaceException>(() => Accept(stopped, pending, "next"));
        var guest = stopped.Members().Single(m => m.Id == "guest");
        Assert.Throws<WorkspaceException>(() =>
            stopped.Apply(Command("member.leave", guest) with { Id = "founder" }, "guest", out _)
        );
        var owner = stopped.Members().Single(m => m.Id == "founder");
        Assert.Throws<WorkspaceException>(() => stopped.Apply(Command("member.leave", owner), "founder", out _));
        stopped.Apply(Command("member.leave", guest), "guest", out _);
        Assert.Throws<WorkspaceException>(() => stopped.Actor("guest"));
        Assert.Null(stopped.SignIn("guest", "guest@example.invalid", true, "Guest"));
        Assert.Equal("owner", stopped.Actor("founder").Role);
        Assert.Single(stopped.Feed(0), e => e.Type == "member.left" && e.Actor == "guest");
        Assert.True(stopped.VerifyAudit().Intact);
        Assert.Throws<WorkspaceException>(() => Accept(free, pending with { Token = new string('0', 64) }, "next"));
        // A deliberate new invitation can restore a departed member; a stale link cannot.
        var renewed = free.InviteSignupMember("founder", "guest@example.invalid", "approver");
        Accept(free, renewed);
        Assert.Equal("approver", free.Actor("guest").Role);
    }

    [Fact]
    public void MembershipDoesNotSpendSignupEligibilityOrMultiplyTheCompanyAllowance()
    {
        var free = Free();
        free.ReserveModelSpend("existing-work", .10m);
        var before = free.FreeAllowance();
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "approver");
        Accept(free, invite);
        Assert.Equal(before, free.FreeAllowance());
        var root = Store();
        Assert.False(root.HasSignup("guest"));
        var own = root.Signup("guest", "guest@example.invalid", true, "Guest", new());
        Assert.True(root.HasSignup("guest"));
        Assert.Equal(own, root.Signup("guest", "guest@example.invalid", true, "Guest", new()));
        Assert.Throws<WorkspaceException>(() => free.CreateCompany("Another", Guid.NewGuid().ToString("D"), "guest"));
        Assert.Equal(40m, free.FreeAllowance()!.CompanyRemainingCents);
        Assert.Equal(50m, Store(own.Id, "guest").FreeAllowance()!.CompanyRemainingCents);
        var next = free.InviteSignupMember("founder", "next@example.invalid", "viewer");
        root.Signup("next", "next@example.invalid", true, "Next", new());
        Accept(free, next, "next");
        Assert.Equal(490m, free.FreeAllowance()!.DailyRemainingCents);
    }

    [Fact]
    public void OnlyTheTokenHashIsStoredAndLegacyInvitationsStillBindAtSignIn()
    {
        var free = Free();
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "viewer");
        using var db = new StoreConnection(free.Database.Open(), free.Database);
        Assert.Equal(
            CompanyStore.InvitationHash(invite.Token),
            db.Scalar("SELECT token_hash FROM co_member_invites WHERE company_id=$0", free.CompanyId)
        );
        Assert.Equal("0", db.Scalar("SELECT count(*) FROM co_operations WHERE company_id=$0", free.CompanyId));
        Assert.DoesNotContain(invite.Token, CompanyJson.Encode(free.Members()));
        Assert.DoesNotContain(invite.Token, CompanyJson.Encode(free.Feed(0)));
        var legacy = Store();
        legacy.Apply(
            new(Guid.NewGuid().ToString("D"), "member.invite") { Body = "legacy@example.invalid", Decision = "owner" }
        );
        Assert.Equal("owner", legacy.SignIn("legacy", "legacy@example.invalid", true, "Legacy")!.Role);
    }

    [Fact]
    public void InvitationMigrationPreservesExistingMembersAndAuditWithoutCreatingInvitations()
    {
        var legacy = Store();
        legacy.Apply(
            new(Guid.NewGuid().ToString("D"), "member.invite") { Body = "legacy@example.invalid", Decision = "viewer" }
        );
        var free = Free();
        var members = legacy.Members();
        var audit = legacy.Feed(0);
        string[] checksums;
        using (var db = new StoreConnection(legacy.Database.Open(), legacy.Database))
        {
            checksums = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                row => row.GetString(0)
            );
            // Reconstruct the immediately preceding schema; the old company and its guest book stay intact.
            db.Execute(
                "DROP TABLE co_member_invites; DELETE FROM module_schema_migrations WHERE owner='company' AND version=32;"
            );
            Assert.Equal("31", db.Scalar("SELECT max(version) FROM module_schema_migrations WHERE owner='company'"));
        }
        var reopened = Store(enabled: false);
        Assert.Equal(members, reopened.Members());
        Assert.Equal(audit, reopened.Feed(0));
        Assert.True(reopened.VerifyAudit().Intact);
        Assert.Equal("owner", Store(free.CompanyId, "founder", false).Actor("founder").Role);
        Assert.Equal(free.FreeAllowance(), Store(free.CompanyId, "founder", false).FreeAllowance());
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal("0", check.Scalar("SELECT count(*) FROM co_member_invites"));
        Assert.Equal(
            checksums,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                row => row.GetString(0)
            )
        );
        Assert.Equal("viewer", reopened.SignIn("legacy", "legacy@example.invalid", true, "Legacy")!.Role);
    }

    [Fact]
    public async Task ConcurrentRevocationAndAcceptanceHaveOneWinner()
    {
        var free = Free();
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "viewer");
        var outcomes = await Task.WhenAll(
            Task.Run(() =>
            {
                try
                {
                    Accept(free, invite);
                    return true;
                }
                catch (WorkspaceException)
                {
                    return false;
                }
            }),
            Task.Run(() =>
            {
                try
                {
                    free.Apply(Command("member.revoke", invite.Member), "founder", out _);
                    return true;
                }
                catch (WorkspaceException)
                {
                    return false;
                }
            })
        );
        Assert.Single(outcomes, success => success);
        Assert.Equal(outcomes[0], free.Members().Any(member => member.Id == "guest" && member.State == "active"));
        Assert.Throws<WorkspaceException>(() => Accept(free, invite));
        Assert.True(free.VerifyAudit().Intact);
    }

    [Fact]
    public void FailedAuditRollsBackMembershipAndLeavesTheInvitationUnused()
    {
        var free = Free();
        var invite = free.InviteSignupMember("founder", "guest@example.invalid", "viewer");
        using var db = new StoreConnection(free.Database.Open(), free.Database);
        db.Execute(
            "CREATE TRIGGER fail_invite_audit BEFORE INSERT ON co_audit BEGIN SELECT RAISE(ABORT,'Fictional audit failure'); END;"
        );
        Assert.Throws<SqliteException>(() => Accept(free, invite));
        Assert.Throws<WorkspaceException>(() => free.Actor("guest"));
        Assert.Equal("viewer", free.PreviewInvitation(invite.Token).Role);
        Assert.DoesNotContain(free.Feed(0), e => e.Type == "member.joined");
        db.Execute("DROP TRIGGER fail_invite_audit;");
        Accept(free, invite);
        Assert.True(free.VerifyAudit().Intact);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }
}
