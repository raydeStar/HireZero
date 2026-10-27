using System.Globalization;

namespace HireZero.Core;

public sealed record CompanyNotice(
    string Id,
    long Sequence,
    string Category,
    string Title,
    string Detail,
    string Destination,
    string? TargetId,
    DateTimeOffset At
);

public sealed record CompanyNoticeView(CompanyNotice Notice, bool Read);

public sealed record CompanyNoticePreferences(
    string Id,
    bool EmailEnabled = false,
    bool Completed = true,
    bool NeedsAttention = true,
    int DailyLimit = 5,
    int Version = 0,
    DateTimeOffset? Until = null,
    string? Address = null,
    string? ServiceDigest = null
);

public sealed record CompanyNoticePreferenceInput(
    bool EmailEnabled,
    bool Completed,
    bool NeedsAttention,
    int DailyLimit
);

public sealed record CompanyVerifiedEmail(string Id, string Address);

public sealed record CompanyNoticeDelivery(
    string Id,
    string NoticeId,
    string Subject,
    string Address,
    int PreferenceVersion,
    string ServiceDigest,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null,
    string? Reason = null
);

public sealed record CompanyNoticeEmailOffer(bool Available, bool Eligible, string? Address, string? ServiceDigest);

public sealed record CompanyNotificationInbox(
    CompanyNoticeView[] Items,
    long? NextBefore,
    int Unread,
    CompanyNoticePreferences Preferences,
    CompanyNoticeEmailOffer Email,
    CompanyNoticeDelivery[] Deliveries
);

public sealed record NotificationEnvelope(string Id, string Recipient, string Subject, string Body);

public interface INotificationSender
{
    string Identity { get; }
    Task SendAsync(NotificationEnvelope envelope, CancellationToken token);
}

public sealed partial class CompanyStore
{
    public string? NotificationServiceIdentity { get; init; }
    public string? NotificationPublicUrl { get; init; }
    private static string NotificationSchema =>
        string.Join(
            "",
            new[] { "co_notices", "co_notice_preferences", "co_notice_deliveries", "co_verified_emails" }.Select(
                table =>
                    $"CREATE TABLE {table}(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id));"
            )
        )
        + "CREATE UNIQUE INDEX co_notice_sequence ON co_notices(company_id,revision);"
        + "CREATE TABLE co_notice_reads(company_id TEXT NOT NULL,notice_id TEXT NOT NULL,subject TEXT NOT NULL,read_at TEXT NOT NULL,PRIMARY KEY(company_id,notice_id,subject),FOREIGN KEY(company_id,notice_id) REFERENCES co_notices(company_id,id));";

    private CompanyNoticePreferences NoticePreferences(StoreConnection db, string subject) =>
        Find<CompanyNoticePreferences>(db, subject) ?? new(subject);

    public int UnreadNotices(string subject)
    {
        Actor(subject);
        using var db = Open();
        return int.Parse(
            Scalar(
                db,
                "SELECT COUNT(*) FROM co_notices n WHERE n.company_id=$0 AND NOT EXISTS(SELECT 1 FROM co_notice_reads r WHERE r.company_id=n.company_id AND r.notice_id=n.id AND r.subject=$1)",
                CompanyId,
                subject
            )!,
            CultureInfo.InvariantCulture
        );
    }

    private CompanyNoticeEmailOffer NoticeEmailOffer(StoreConnection db, string subject)
    {
        var member = Need<CompanyMember>(db, subject);
        var verified = Find<CompanyVerifiedEmail>(db, subject);
        var available = NotificationServiceIdentity is not null && NotificationPublicUrl is not null;
        var eligible = member is { State: "active", Role: CompanyRoles.Owner } && verified?.Address == member.Email;
        return new(
            available,
            eligible,
            eligible ? member.Email : null,
            available && eligible
                ? CompanyJson.Digest(
                    new
                    {
                        NotificationServiceIdentity,
                        NotificationPublicUrl,
                        member.Id,
                        member.Email,
                        member.Version,
                    }
                )
                : null
        );
    }

    public CompanyNotificationInbox Inbox(string subject, long before = long.MaxValue, bool unreadOnly = false)
    {
        Actor(subject);
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var items = Rows(
                    db,
                    "SELECT n.payload,r.read_at FROM co_notices n LEFT JOIN co_notice_reads r ON r.company_id=n.company_id AND r.notice_id=n.id AND r.subject=$1 WHERE n.company_id=$0 AND n.revision<$2 AND ($3=0 OR r.read_at IS NULL) ORDER BY n.revision DESC LIMIT 31",
                    r => new CompanyNoticeView(CompanyJson.Decode<CompanyNotice>(r.GetString(0)), !r.IsDBNull(1)),
                    CompanyId,
                    subject,
                    before,
                    unreadOnly ? 1 : 0
                );
                var unread = int.Parse(
                    Scalar(
                        db,
                        "SELECT COUNT(*) FROM co_notices n WHERE n.company_id=$0 AND NOT EXISTS(SELECT 1 FROM co_notice_reads r WHERE r.company_id=n.company_id AND r.notice_id=n.id AND r.subject=$1)",
                        CompanyId,
                        subject
                    )!,
                    CultureInfo.InvariantCulture
                );
                return new CompanyNotificationInbox(
                    items.Take(30).ToArray(),
                    items.Length > 30 ? items[29].Notice.Sequence : null,
                    unread,
                    NoticePreferences(db, subject),
                    NoticeEmailOffer(db, subject),
                    Rows(
                        db,
                        "SELECT payload FROM co_notice_deliveries WHERE company_id=$0 AND employee_id=$1 ORDER BY revision DESC LIMIT 20",
                        r => CompanyJson.Decode<CompanyNoticeDelivery>(r.GetString(0)),
                        CompanyId,
                        subject
                    )
                );
            }
        );
    }

    private object MarkNotice(StoreConnection db, CompanyCommand command)
    {
        var notice = Need<CompanyNotice>(db, command.Id);
        var subject = CurrentActor.Value!.Subject;
        if (command.Decision == "read")
            Sql(
                db,
                "INSERT INTO co_notice_reads(company_id,notice_id,subject,read_at) VALUES($0,$1,$2,$3) ON CONFLICT(company_id,notice_id,subject) DO UPDATE SET read_at=excluded.read_at",
                CompanyId,
                notice.Id,
                subject,
                Clock().ToString("O")
            );
        else if (command.Decision == "unread")
            Sql(
                db,
                "DELETE FROM co_notice_reads WHERE company_id=$0 AND notice_id=$1 AND subject=$2",
                CompanyId,
                notice.Id,
                subject
            );
        else
            throw new WorkspaceException("Choose read or unread.", 422);
        return new { notice.Id, Read = command.Decision == "read" };
    }

    private object ConfigureNotices(StoreConnection db, CompanyCommand command)
    {
        var subject = CurrentActor.Value!.Subject;
        var previous = NoticePreferences(db, subject);
        Version(previous.Version, command.ExpectedVersion);
        var input = CompanyJson.Decode<CompanyNoticePreferenceInput>(command.Body ?? "null");
        if (input.DailyLimit is < 1 or > 20 || input.EmailEnabled && !input.Completed && !input.NeedsAttention)
            throw new WorkspaceException("Choose an alert category and a daily email limit between one and 20.", 422);
        var offer = NoticeEmailOffer(db, subject);
        if (
            input.EmailEnabled
            && (
                !offer.Available
                || !offer.Eligible
                || command.Digest != offer.ServiceDigest
                || command.Decision != "authorize-notification-email"
            )
        )
            throw new WorkspaceException(
                "Email needs a configured service, your verified sign-in address, and exact owner consent.",
                409
            );
        var preference = new CompanyNoticePreferences(
            subject,
            input.EmailEnabled,
            input.Completed,
            input.NeedsAttention,
            input.DailyLimit,
            previous.Version + 1,
            input.EmailEnabled ? Clock().AddDays(30) : null,
            input.EmailEnabled ? offer.Address : null,
            input.EmailEnabled ? offer.ServiceDigest : null
        );
        Save(db, preference, subject, version: preference.Version);
        Event(
            db,
            "notification.preferences",
            subject,
            null,
            input.EmailEnabled
                ? "Owner authorized bounded email notices for 30 days. No historical alerts are sent."
                : "Owner disabled email notices. The inbox remains available."
        );
        return preference;
    }

    private object CloseNoticeDelivery(StoreConnection db, CompanyCommand command)
    {
        var delivery = Need<CompanyNoticeDelivery>(db, command.Id);
        if (delivery.Subject != CurrentActor.Value!.Subject)
            throw new WorkspaceException("Delivery not found.", 404);
        if (delivery.State != "unknown" || command.Decision != "keep-inbox-no-resend")
            throw new WorkspaceException("Acknowledge this uncertain delivery without resending it.", 409);
        delivery = delivery with { State = "closed", Reason = "Owner kept the alert in the inbox without resending." };
        SaveNoticeDelivery(db, delivery);
        Event(
            db,
            "notification.closed",
            delivery.Id,
            null,
            "Owner acknowledged an uncertain email. It will not be sent again."
        );
        return delivery;
    }

    // Only selected outcomes become alerts. Details stay in the authenticated inbox, never in email.
    private void NoticeFromEvent(StoreConnection db, string type, string id, string text)
    {
        string category = "attention",
            title,
            destination;
        string? target = id;
        if (type is "plan.proposed" or "plan.completed")
        {
            var plan = Find<CompanyPlan>(db, id);
            if (plan is null)
                return;
            text = "Plan: " + ContentGuard.Clean(plan.Objective, 160).Text + ". " + text;
            category = type == "plan.completed" ? "completed" : "attention";
            title = category == "completed" ? "Coordinated work is complete" : "A company plan needs approval";
            destination = "plan";
        }
        else if (type is "meeting.outcome_proposed" or "meeting.awaiting_owner" or "meeting.closed")
        {
            var meeting = Find<CompanyMeeting>(db, id);
            if (meeting is null)
                return;
            text = "Meeting: " + ContentGuard.Clean(meeting.Title, 160).Text + ". " + text;
            category = type == "meeting.closed" ? "completed" : "attention";
            title = type switch
            {
                "meeting.outcome_proposed" => "Meeting discussion is ready for minutes",
                "meeting.awaiting_owner" => "Meeting minutes need approval",
                _ => "Meeting minutes accepted",
            };
            destination = "meeting";
        }
        else if (type is "browser.result" or "browser.release-attention" or "browser.unknown")
        {
            var job = Find<CompanyBrowserJob>(db, id);
            if (job is null)
                return;
            category = type == "browser.result" && job.State == "completed" ? "completed" : "attention";
            title =
                category == "completed" ? "Website captures are ready for review" : "Browser execution needs attention";
            destination = "task";
            target = job.TaskId;
        }
        else if (type is "sandbox.result" or "sandbox.release-attention" or "sandbox.unknown")
        {
            var job = Find<CompanySandboxJob>(db, id);
            if (job is null)
                return;
            category = type == "sandbox.result" && job.State == "completed" ? "completed" : "attention";
            title = category == "completed" ? "Script output is ready for review" : "Script execution needs attention";
            destination = "task";
            target = job.TaskId;
        }
        else if (type == "execution.release-attention")
        {
            var grant = Find<CompanyExecutionGrant>(db, id);
            if (grant is null)
                return;
            title = "Temporary runtime release needs attention";
            destination = "task";
            target = grant.TaskId;
        }
        else if (type == "memory.review-due")
        {
            title = "Organizational memory needs review";
            destination = "memory";
        }
        else if (type is "schedule.result" or "schedule.blocked" or "schedule.missed")
        {
            var run = type is "schedule.result" or "schedule.missed" ? Find<CompanyScheduleRun>(db, id) : null;
            var schedule = Find<CompanySchedule>(db, run?.ScheduleId ?? id);
            if (schedule is null)
                return;
            category = run?.State == "completed" ? "completed" : "attention";
            if (
                run?.RefreshId is { } refreshId
                && Find<CompanySourceRefresh>(db, refreshId)?.Checks?.Any(c => c.Status != "unchanged") == true
            )
                category = "attention";
            title = category == "completed" ? "Recurring work finished" : "Recurring work needs attention";
            destination = "schedule";
            target = schedule.Id;
        }
        else if (type == "artifact.ready")
        {
            var artifact = Find<CompanyArtifact>(db, id);
            if (artifact is null)
                return;
            // The chair collects discussion turns. Ring once for the minutes, not for every cleared throat.
            if (Find<CompanyTask>(db, artifact.TaskId)?.MeetingId is not null)
                return;
            title = "An employee result is ready for review";
            destination = "task";
            target = artifact.TaskId;
        }
        else if (
            type
            is "conversation.reply"
                or "task.completed"
                or "task.failed"
                or "task.unknown"
                or "task.blocked"
                or "task.withheld"
        )
        {
            var task = Find<CompanyTask>(db, id);
            if (task is null || task.ScheduleRunId is not null)
                return;
            category = type is "conversation.reply" or "task.completed" ? "completed" : "attention";
            if (task.MeetingId is not null && category == "completed")
                return;
            title =
                task.MeetingId is not null ? "Meeting work needs attention"
                : category == "completed" ? "An employee result is ready"
                : "Employee work needs attention";
            destination = "task";
        }
        else if (
            type
            is "business.refresh-completed"
                or "business.refresh-partial"
                or "business.refresh-unknown"
                or "business.refresh-failed"
        )
        {
            var refresh = Find<CompanySourceRefresh>(db, id);
            if (refresh is null || refresh.ScheduleRunId is not null)
                return;
            title = "Your source check is ready to review";
            destination = "business";
            target = refresh.ProfileId;
        }
        else
            return;
        var sequence = long.Parse(
            Scalar(db, "SELECT MAX(seq) FROM co_events WHERE company_id=$0", CompanyId)!,
            CultureInfo.InvariantCulture
        );
        var notice = new CompanyNotice(
            Id(),
            sequence,
            category,
            title,
            ContentGuard.Clean(text, 500).Text,
            destination,
            target,
            Clock()
        );
        Save(db, notice, notice.Id, state: category, version: checked((int)sequence));
        foreach (var preference in All<CompanyNoticePreferences>(db).Where(p => p.EmailEnabled))
        {
            if (
                !NoticePreferenceCurrent(db, preference)
                || !(category == "completed" ? preference.Completed : preference.NeedsAttention)
            )
                continue;
            var delivery = new CompanyNoticeDelivery(
                Id(),
                notice.Id,
                preference.Id,
                preference.Address!,
                preference.Version,
                preference.ServiceDigest!,
                "pending",
                Clock()
            );
            SaveNoticeDelivery(db, delivery, checked((int)sequence));
        }
    }

    private bool NoticePreferenceCurrent(StoreConnection db, CompanyNoticePreferences preference)
    {
        var member = Find<CompanyMember>(db, preference.Id);
        if (
            member is not { State: "active", Role: CompanyRoles.Owner }
            || !preference.EmailEnabled
            || preference.Until is null
            || preference.Until <= Clock()
        )
            return false;
        var offer = NoticeEmailOffer(db, preference.Id);
        return offer.Available
            && offer.Eligible
            && offer.Address == preference.Address
            && offer.ServiceDigest == preference.ServiceDigest;
    }

    private void SaveNoticeDelivery(StoreConnection db, CompanyNoticeDelivery delivery, int? sequence = null) =>
        Save(
            db,
            delivery,
            delivery.Id,
            delivery.NoticeId,
            delivery.Subject,
            delivery.State,
            sequence ?? checked((int)Need<CompanyNotice>(db, delivery.NoticeId).Sequence)
        );

    public CompanyNoticeDelivery? ClaimNoticeDelivery()
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                foreach (
                    var delivery in All<CompanyNoticeDelivery>(db)
                        .Where(d => d.State == "pending")
                        .OrderBy(d => d.CreatedAt)
                )
                {
                    var preference = NoticePreferences(db, delivery.Subject);
                    var read =
                        Scalar(
                            db,
                            "SELECT read_at FROM co_notice_reads WHERE company_id=$0 AND notice_id=$1 AND subject=$2",
                            CompanyId,
                            delivery.NoticeId,
                            delivery.Subject
                        )
                        is not null;
                    if (
                        read
                        || Clock() - delivery.CreatedAt > TimeSpan.FromHours(24)
                        || preference.Version != delivery.PreferenceVersion
                        || !NoticePreferenceCurrent(db, preference)
                    )
                    {
                        SaveNoticeDelivery(
                            db,
                            delivery with
                            {
                                State = "skipped",
                                FinishedAt = Clock(),
                                Reason = "Already read, expired, or notification authority changed.",
                            }
                        );
                        continue;
                    }
                    var previous = All<CompanyNoticeDelivery>(db).Where(d => d.Subject == delivery.Subject).ToArray();
                    if (previous.Any(d => d.State is "dispatching" or "unknown"))
                        continue;
                    var today = Clock().UtcDateTime.Date;
                    if (previous.Count(d => d.StartedAt?.UtcDateTime.Date == today) >= preference.DailyLimit)
                    {
                        SaveNoticeDelivery(
                            db,
                            delivery with
                            {
                                State = "skipped",
                                FinishedAt = Clock(),
                                Reason = "Daily email allowance reached. The alert remains in the inbox.",
                            }
                        );
                        continue;
                    }
                    var claimed = delivery with { State = "dispatching", StartedAt = Clock() };
                    SaveNoticeDelivery(db, claimed);
                    return claimed;
                }
                return null;
            }
        );
    }

    public NotificationEnvelope? NoticeEnvelope(string id)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var delivery = Need<CompanyNoticeDelivery>(db, id);
                var preference = NoticePreferences(db, delivery.Subject);
                if (delivery.State != "dispatching")
                    return null;
                if (
                    preference.Version != delivery.PreferenceVersion
                    || !NoticePreferenceCurrent(db, preference)
                    || Scalar(
                        db,
                        "SELECT read_at FROM co_notice_reads WHERE company_id=$0 AND notice_id=$1 AND subject=$2",
                        CompanyId,
                        delivery.NoticeId,
                        delivery.Subject
                    )
                        is not null
                )
                {
                    SaveNoticeDelivery(
                        db,
                        delivery with
                        {
                            State = "skipped",
                            FinishedAt = Clock(),
                            Reason = "Already read or notification authority changed before sending.",
                        }
                    );
                    return null;
                }
                var url =
                    NotificationPublicUrl!.TrimEnd('/')
                    + "/?company="
                    + Uri.EscapeDataString(CompanyId)
                    + "&view=departments&section=inbox";
                return new NotificationEnvelope(
                    delivery.Id,
                    delivery.Address,
                    "Your HireZero workspace has an update",
                    "Work finished or needs your attention. Sign in to your workspace inbox to review it.\n\n"
                        + url
                        + "\n\nThis notice contains no business facts or work content. Manage or disable email notices in the workspace inbox."
                );
            }
        );
    }

    public void FinishNoticeDelivery(string id, bool accepted)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                var delivery = Need<CompanyNoticeDelivery>(db, id);
                if (delivery.State != "dispatching")
                    return false;
                SaveNoticeDelivery(
                    db,
                    delivery with
                    {
                        State = accepted ? "accepted" : "unknown",
                        FinishedAt = Clock(),
                        Reason = accepted
                            ? "Accepted by the email server; inbox delivery is not confirmed."
                            : "Email outcome is uncertain. This notice will not be resent automatically.",
                    }
                );
                Event(
                    db,
                    "notification.delivery",
                    id,
                    null,
                    accepted
                        ? "Email server accepted one notice."
                        : "An email outcome is uncertain; automatic delivery is held for this recipient."
                );
                return true;
            }
        );
    }

    public void RecoverNotificationDelivery()
    {
        using var db = Open();
        foreach (var delivery in All<CompanyNoticeDelivery>(db).Where(d => d.State == "dispatching"))
            FinishNoticeDelivery(delivery.Id, false);
    }
}

public sealed partial class CompanyScheduler
{
    public INotificationSender? NotificationSender { get; init; }
    private readonly SemaphoreSlim _noticeDelivery = new(1, 1);

    public async Task<bool> TickNotificationsAsync(CancellationToken token = default)
    {
        if (
            NotificationSender is null
            || NotificationSender.Identity != store.NotificationServiceIdentity
            || !await _noticeDelivery.WaitAsync(0, token)
        )
            return false;
        try
        {
            var delivery = store.ClaimNoticeDelivery();
            if (delivery is null)
                return false;
            var envelope = store.NoticeEnvelope(delivery.Id);
            if (envelope is null)
                return false;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                await NotificationSender.SendAsync(envelope, deadline.Token).WaitAsync(deadline.Token);
                store.FinishNoticeDelivery(delivery.Id, true);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                store.FinishNoticeDelivery(delivery.Id, false);
            }
            return true;
        }
        finally
        {
            _noticeDelivery.Release();
        }
    }
}
