using System.Net;
using System.Net.Sockets;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Notifications;

public sealed class CompanyNotificationTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-notifications-");
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId, string service = "fictional-mail-v1") =>
        new(
            Path.Combine(_root.Path, "workspace.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-model",
            companyId: company
        )
        {
            Clock = () => _now,
            NotificationServiceIdentity = service,
            NotificationPublicUrl = "https://workspace.example.invalid",
        };

    private static CompanyCommand C(string action) => MeasuredQueueChecks.C(action);

    private static CompanyCommand Configure(CompanyStore s, string subject = Owner, bool enabled = true, int limit = 5)
    {
        var inbox = s.Inbox(subject);
        return C("notification.preferences") with
        {
            ExpectedVersion = inbox.Preferences.Version,
            Digest = inbox.Email.ServiceDigest,
            Decision = "authorize-notification-email",
            Body = CompanyJson.Encode(new CompanyNoticePreferenceInput(enabled, true, true, limit)),
        };
    }

    private static void Verify(CompanyStore s, string subject = Owner) =>
        Assert.NotNull(s.SignIn(subject, subject, true, "Fictional Owner"));

    private static CompanyNotice Alert(CompanyStore s)
    {
        var task = MeasuredQueueChecks.Seed(s);
        s.Apply(
            C("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = "Private fictional campaign result; never include this body in a notification email.",
            }
        );
        return s.Inbox(Owner).Items[0].Notice;
    }

    private sealed class Sender(bool fail = false) : INotificationSender
    {
        public string Identity => "fictional-mail-v1";
        public List<NotificationEnvelope> Sent { get; } = [];

        public Task SendAsync(NotificationEnvelope envelope, CancellationToken token)
        {
            Sent.Add(envelope);
            return fail ? Task.FromException(new IOException("Fictional ambiguous delivery")) : Task.CompletedTask;
        }
    }

    private sealed class NoModel : IModelProvider
    {
        public bool Qualified => false;
        public string Status => "No model in notification checks.";

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void InboxIsDurableTenantScopedAndReadMarksBelongToEachMember()
    {
        var s = Store();
        var notice = Alert(s);
        Assert.Single(s.Inbox(Owner).Items);
        Assert.Empty(s.Inbox(Owner).Deliveries);
        s.Apply(C("member.invite") with { Body = "viewer@example.invalid", Decision = "viewer" });
        var viewer = s.SignIn("viewer-subject", "viewer@example.invalid", true, "Fictional Viewer")!;
        var mark = C("notification.mark") with { Id = notice.Id, Decision = "read" };
        var result = s.Apply(mark, viewer.Subject, out _);
        Assert.Equal(result, s.Apply(mark, viewer.Subject, out _));
        Assert.Equal(0, s.UnreadNotices(viewer.Subject));
        Assert.Equal(1, s.UnreadNotices(Owner));
        Assert.True(Assert.Single(Store().Inbox(viewer.Subject).Items).Read);
        var other = Store("co-other-notices");
        Assert.Empty(other.Inbox(Owner).Items);
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.Apply(mark)).Status);
        Assert.Equal(
            403,
            Assert.Throws<WorkspaceException>(() => s.Apply(Configure(s, viewer.Subject), viewer.Subject, out _)).Status
        );
        Assert.True(s.VerifyAudit().Intact);
    }

    [Fact]
    public void EmailNeedsVerifiedOwnerExactConsentAndNeverSendsHistoricalAlerts()
    {
        var s = Store();
        Alert(s);
        Assert.False(s.Inbox(Owner).Email.Eligible);
        Assert.Throws<WorkspaceException>(() => s.Apply(Configure(s)));
        Verify(s);
        Assert.Throws<WorkspaceException>(() => s.Apply(Configure(s) with { Digest = "stale-service" }));
        Assert.Throws<WorkspaceException>(() => s.Apply(Configure(s) with { Decision = "save" }));
        var command = Configure(s);
        var first = s.Apply(command);
        Assert.Equal(first, s.Apply(command));
        Assert.Empty(s.Inbox(Owner).Deliveries);
        Alert(s);
        var delivery = Assert.Single(s.Inbox(Owner).Deliveries);
        Assert.Equal("pending", delivery.State);
        Assert.Equal(Owner, delivery.Address);
        Assert.Equal(_now.AddDays(30), s.Inbox(Owner).Preferences.Until);
    }

    [Fact]
    public async Task OneEmailAttemptHasGenericContentAndTheCorrectCompanyLink()
    {
        var s = Store("co-fictional-notice");
        Verify(s);
        s.Apply(Configure(s));
        Alert(s);
        var sender = new Sender();
        var scheduler = new CompanyScheduler(s, new NoModel()) { NotificationSender = sender };
        Assert.True(await scheduler.TickNotificationsAsync(TestContext.Current.CancellationToken));
        Assert.False(await scheduler.TickNotificationsAsync(TestContext.Current.CancellationToken));
        var envelope = Assert.Single(sender.Sent);
        Assert.Contains("company=co-fictional-notice", envelope.Body);
        Assert.DoesNotContain("campaign", envelope.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Owner, envelope.Recipient);
        Assert.Equal("accepted", Assert.Single(s.Inbox(Owner).Deliveries).State);
        Store("co-fictional-notice").RecoverNotificationDelivery();
        Assert.False(await scheduler.TickNotificationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReadDisabledExpiredAndQuotaExceededNoticesStayInTheInbox()
    {
        var s = Store();
        Verify(s);
        s.Apply(Configure(s, limit: 1));
        var read = Alert(s);
        s.Apply(C("notification.mark") with { Id = read.Id, Decision = "read" });
        Assert.Null(s.ClaimNoticeDelivery());
        Alert(s);
        var first = s.ClaimNoticeDelivery()!;
        s.FinishNoticeDelivery(first.Id, true);
        Alert(s);
        Assert.Null(s.ClaimNoticeDelivery());
        Assert.Contains(s.Inbox(Owner).Deliveries, d => d.Reason!.Contains("Daily email"));
        _now = _now.AddDays(1);
        Alert(s);
        s.Apply(Configure(s, enabled: false));
        Assert.Null(s.ClaimNoticeDelivery());
        s.Apply(Configure(s));
        Alert(s);
        _now = _now.AddHours(25);
        Assert.Null(s.ClaimNoticeDelivery());
        _now = _now.AddDays(31);
        Alert(s);
        Assert.DoesNotContain(s.Inbox(Owner).Deliveries, d => d.State == "pending");
        Assert.Equal(6, s.Inbox(Owner).Items.Length);
    }

    [Fact]
    public void RevokedMembershipChangedServiceAndUnverifiedSignInFenceEmail()
    {
        var s = Store();
        Verify(s);
        s.Apply(Configure(s));
        Alert(s);
        var changed = Store(service: "changed-mail-v2");
        Assert.Null(changed.ClaimNoticeDelivery());
        s.Apply(Configure(s));
        Alert(s);
        var claimed = s.ClaimNoticeDelivery()!;
        s.SignIn(Owner, Owner, false, "Unverified fixture");
        Assert.Null(s.NoticeEnvelope(claimed.Id));
        s.Apply(C("member.invite") with { Body = "second@example.invalid", Decision = "owner" });
        var second = s.SignIn("second@example.invalid", "second@example.invalid", true, "Second Fictional Owner")!;
        s.Apply(Configure(s, second.Subject), second.Subject, out _);
        Alert(s);
        var member = s.Members().Single(m => m.Id == second.Subject);
        s.Apply(C("member.revoke") with { Id = member.Id, ExpectedVersion = member.Version });
        Assert.Null(s.ClaimNoticeDelivery());
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => s.Inbox(second.Subject)).Status);
    }

    [Fact]
    public async Task UnknownAndInterruptedSendsAreNeverAutomaticallyReplayed()
    {
        var s = Store();
        Verify(s);
        s.Apply(Configure(s));
        Alert(s);
        var sender = new Sender(fail: true);
        var scheduler = new CompanyScheduler(s, new NoModel()) { NotificationSender = sender };
        Assert.True(await scheduler.TickNotificationsAsync(TestContext.Current.CancellationToken));
        var unknown = Assert.Single(s.Inbox(Owner).Deliveries);
        Assert.Equal("unknown", unknown.State);
        Alert(s);
        Assert.False(await scheduler.TickNotificationsAsync(TestContext.Current.CancellationToken));
        s.Apply(C("notification.close") with { Id = unknown.Id, Decision = "keep-inbox-no-resend" });
        var next = s.ClaimNoticeDelivery()!;
        Assert.NotEqual(unknown.Id, next.Id);
        var reopened = Store();
        reopened.RecoverNotificationDelivery();
        Assert.Equal("unknown", reopened.Inbox(Owner).Deliveries.Single(d => d.Id == next.Id).State);
        Assert.Null(reopened.ClaimNoticeDelivery());
        Assert.Single(sender.Sent);
    }

    [Fact]
    public void InboxPagesDoNotLoseAlertsWhenNewWorkArrives()
    {
        var s = Store();
        var task = MeasuredQueueChecks.Seed(s);
        for (var i = 0; i < 33; i++)
        {
            task = s.Work(task.Id);
            s.Apply(
                C("artifact.create") with
                {
                    Id = task.Id,
                    ExpectedVersion = task.Version,
                    Body = "Fictional revision " + i,
                }
            );
        }
        var first = s.Inbox(Owner);
        Assert.Equal(30, first.Items.Length);
        Assert.NotNull(first.NextBefore);
        var second = s.Inbox(Owner, first.NextBefore!.Value);
        Assert.Equal(3, second.Items.Length);
        Assert.DoesNotContain(second.Items, item => first.Items.Any(old => old.Notice.Id == item.Notice.Id));
        Assert.Equal(33, s.UnreadNotices(Owner));
    }

    [Fact]
    public async Task SmtpRefusesToSendCredentialsOrContentWithoutStartTls()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var transcript = new List<string>();
        var server = Task.Run(
            async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, Encoding.ASCII)
                {
                    AutoFlush = true,
                    NewLine = "\r\n",
                };
                await writer.WriteLineAsync("220 fictional smtp".AsMemory(), TestContext.Current.CancellationToken);
                var line = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
                transcript.Add(line ?? "");
                await writer.WriteLineAsync(
                    "250 fictional.example.invalid".AsMemory(),
                    TestContext.Current.CancellationToken
                );
                while ((line = await reader.ReadLineAsync(TestContext.Current.CancellationToken)) is not null)
                {
                    transcript.Add(line);
                    if (line == "QUIT")
                    {
                        await writer.WriteLineAsync("221 bye".AsMemory(), TestContext.Current.CancellationToken);
                        break;
                    }
                }
            },
            TestContext.Current.CancellationToken
        );
        var sender = new SmtpNotifications(
            "127.0.0.1",
            port,
            "notices@example.invalid",
            "fictional",
            () => "fictional-password"
        );
        await Assert.ThrowsAsync<System.Net.Mail.SmtpException>(() =>
            sender.SendAsync(
                new("fictional-id", Owner, "Fictional notice", "Private fixture text"),
                TestContext.Current.CancellationToken
            )
        );
        await server.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            transcript,
            line =>
                line.StartsWith("AUTH") || line.StartsWith("MAIL") || line == "DATA" || line.Contains("Private fixture")
        );
    }

    [Fact]
    public void NotificationMigrationPreservesEarlierTasksAndChecksumsWithoutBackfillingEmail()
    {
        var s = Store();
        var task = MeasuredQueueChecks.Seed(s);
        string[] checksums;
        using (var db = new StoreConnection(s.Database.Open(), s.Database))
        {
            checksums = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_decisions; DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE IF EXISTS co_sandbox_jobs; DROP TABLE IF EXISTS co_compute_policy; DROP TABLE IF EXISTS co_workspace_leases; DROP TABLE IF EXISTS co_usage_entries; DROP TABLE IF EXISTS co_usage_policy; DROP TABLE IF EXISTS co_memory_terms; DROP TABLE IF EXISTS co_memory; DROP TABLE co_notice_reads; DROP TABLE co_notice_deliveries; DROP TABLE co_notice_preferences; DROP TABLE co_verified_emails; DROP TABLE co_notices; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=17;"
            );
        }
        var reopened = Store();
        Assert.Equal(task.Id, reopened.Work(task.Id).Id);
        Assert.Empty(reopened.Inbox(Owner).Items);
        Assert.Empty(reopened.Inbox(Owner).Deliveries);
        Alert(reopened);
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            checksums,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            )
        );
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
