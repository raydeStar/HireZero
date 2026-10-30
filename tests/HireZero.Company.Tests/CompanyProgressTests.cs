using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Marketing;
using Microsoft.Data.Sqlite;

public sealed class CompanyProgressTests : IDisposable
{
    private readonly TemporaryRoot _root = new("company-progress-");
    private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private string Database => Path.Combine(_root.Path, "company.sqlite");

    private CompanyStore Store(string id = CompanyStore.DefaultCompanyId, ModelPrice? price = null) =>
        new(
            Database,
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-progress",
            companyId: id
        )
        {
            Clock = () => _now,
            UsagePrice = _ => price,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore store, CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));

    private static T[] Items<T>(CompanyStore store, string key) => MeasuredQueueChecks.Items<T>(store, key);

    private static void Run(CompanyStore store, CompanyAttempt attempt) =>
        store.Apply(
            C("measured.run") with
            {
                Id = attempt.Id,
                ExpectedVersion = attempt.Version,
                Digest = attempt.Measured!.Digest,
                Decision = "accept-soft-limits",
            }
        );

    private static CompanyAttempt Prepare(CompanyStore store, CompanyTask task) =>
        A<CompanyAttempt>(
            store,
            C("measured.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                CreditAllowance = 1,
            }
        );

    private static void Complete(CompanyStore store, CompanyTask task)
    {
        var artifact = Items<CompanyArtifact>(store, "artifacts")
            .Single(a => a.TaskId == task.Id && a.Provenance == "model-output");
        store.Apply(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        var work = store.Work(task.Id);
        store.Apply(C("task.complete") with { Id = work.Id, ExpectedVersion = work.Version });
    }

    private void Sql(string query, params object[] parameters)
    {
        using var db = new SqliteConnection("Data Source=" + Database);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = query;
        for (var i = 0; i < parameters.Length; i++)
            command.Parameters.AddWithValue("$" + i, parameters[i]);
        command.ExecuteNonQuery();
    }

    [Fact]
    public void EmptyAndNeighboringCompaniesCannotBorrowMilestonesOrCosts()
    {
        var store = Store();
        var empty = store.Progress();
        Assert.Null(empty.Onboarding.SecondsToApprovedBrief);
        Assert.Null(empty.Work.FirstAssignment);
        Assert.Null(empty.Work.FirstCompletedAt);
        Assert.Null(empty.AverageRecordedModelUsdPerCompletion);
        Assert.Equal(0, empty.ModelCosts.Jobs);
        var task = MeasuredQueueChecks.Seed(store);
        var attempt = Prepare(store, task);
        Run(store, attempt);
        store.Finish(
            attempt.Id,
            new("An internal fictional result", 30, "fictional-progress", new(20, 10, BilledUsd: 0.02m))
        );
        Complete(store, task);
        Assert.Equal(1, Store().Progress().Work.Completed);
        Assert.Equal(0.02m, Store().Progress().AverageRecordedModelUsdPerCompletion);
        var other = Store("co-neighbor").Progress();
        Assert.Equal(0, other.Work.Tasks);
        Assert.Equal(0, other.ModelCosts.ReportedUsd);
        Assert.Null(other.Work.FirstCompletedAt);
        Assert.DoesNotContain("fictional result", CompanyJson.Encode(store.Progress()));
        Assert.DoesNotContain("owner@example.invalid", CompanyJson.Encode(store.Progress()));
    }

    [Fact]
    public void RepeatRequestsExcludeChatAndScheduledOccurrencesAndDoNotCountRefreshes()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var first = store.Progress();
        Assert.Equal(1, first.Work.MemberRequestDays);
        _now = _now.AddDays(1);
        store.Apply(
            C("conversation.prepare") with
            {
                EmployeeId = task.EmployeeId,
                ThreadId = task.ThreadId,
                Scope = "employee",
                Body = "A fictional chat question",
                CreditAllowance = 1,
            }
        );
        Assert.Equal(1, store.Progress().Work.Tasks);
        Assert.Equal(1, store.Progress().Work.MemberRequestDays);
        var second = MeasuredQueueChecks.Seed(store);
        Assert.Equal(2, store.Progress().Work.MemberRequestDays);
        // Historical scheduled-occurrence fixture: an owner's schedule is not a later visit by that owner.
        var occurrence = new CompanyScheduleRun(
            "fixture-run",
            "fixture-schedule",
            "fictional-digest",
            0,
            _now,
            _now,
            "completed",
            TaskId: second.Id
        );
        Sql(
            "INSERT INTO co_schedule_runs(id,company_id,state,revision,payload) VALUES($0,$1,$2,1,$3)",
            occurrence.Id,
            store.CompanyId,
            occurrence.State,
            CompanyJson.Encode(occurrence)
        );
        Assert.Equal(1, store.Progress().Work.MemberRequestDays);
        Assert.Equal(1, store.Progress().Work.ScheduledTasks);
        var before = store.AuditEntries().Length;
        Assert.Equal(CompanyJson.Encode(store.Progress()), CompanyJson.Encode(Store().Progress()));
        Assert.Equal(before, store.AuditEntries().Length);
        // Legacy tasks without assignment dates retain their totals without inventing dates or activity.
        Sql(
            "DELETE FROM co_events WHERE company_id=$0 AND json_extract(payload,'$.type')='task.assigned'",
            store.CompanyId
        );
        var earlier = store.Progress();
        Assert.True(earlier.HasEarlierRecords);
        Assert.Equal(2, earlier.Work.Tasks);
        Assert.Equal(0, earlier.Work.MemberRequestDays);
    }

    [Fact]
    public void GeneratedBriefEditsAndTrackedAssignmentHaveExactPersistedDenominators()
    {
        var store = Store();
        var identity = A<CompanyBusinessIdentity>(
            store,
            C("business.identify") with
            {
                Body = CompanyJson.Encode(
                    new CompanyBusinessInput(
                        "Fictional Paper Studio",
                        "https://paper.example.invalid/",
                        "Plan a launch"
                    )
                ),
            }
        );
        var start = _now;
        _now = _now.AddMinutes(1);
        identity = A<CompanyBusinessIdentity>(
            store,
            C("business.confirm") with
            {
                ExpectedVersion = identity.Version,
                Digest = identity.Digest,
            }
        );
        var source = store.RecordBusinessSource(
            "owner@example.invalid",
            identity.Version,
            identity.Website!,
            "Fictional source",
            "Fictional paper kits.",
            false
        );
        var draft = A<CompanyBusinessDraft>(
            store,
            C("business.draft.prepare") with
            {
                ExpectedVersion = identity.Version,
                Digest = identity.Digest,
                Evidence = [source.Id],
                CreditAllowance = 1,
            }
        );
        store.Apply(
            C("business.draft.run") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
                Digest = draft.Job.Digest,
                Decision = "accept-soft-limits",
            }
        );
        var fields = CompanyStore
            .BusinessProfileFields.Select(key =>
                key == "business"
                    ? new CompanyProfileField(
                        key,
                        "Fictional paper kits",
                        "sourced",
                        [new(source.Id, "Fictional paper kits.")]
                    )
                    : new CompanyProfileField(key, "Unknown", "unknown", [])
            )
            .ToArray();
        store.FinishBusinessDraft(
            draft.Id,
            new(CompanyJson.Encode(new { fields }), 125, "fictional-profile", new(100, 25, BilledUsd: 0.01m))
        );
        var profile = Assert.Single(Items<CompanyBusinessProfile>(store, "businessProfiles"));
        fields = [.. profile.Fields];
        fields[0] = new("business", "Owner's revised fictional description", "owner", []);
        var save = C("business.profile.save") with
        {
            Id = profile.Id,
            ExpectedVersion = profile.Version,
            Body = CompanyJson.Encode(new CompanyProfileInput(identity.Version, fields)),
        };
        profile = A<CompanyBusinessProfile>(store, save);
        store.Apply(save);
        Assert.Equal(1, store.Progress().Onboarding.OwnerFieldEdits);
        _now = start.AddMinutes(6);
        profile = A<CompanyBusinessProfile>(
            store,
            C("business.profile.review") with
            {
                Id = profile.Id,
                ExpectedVersion = profile.Version,
                Digest = profile.Digest,
                Decision = "approve",
            }
        );
        var measured = store.Progress();
        Assert.Equal(360, measured.Onboarding.SecondsToApprovedBrief);
        Assert.Equal(1, measured.Onboarding.GeneratedBriefs);
        Assert.Equal(1, measured.Onboarding.ApprovedBriefs);
        Assert.Equal(1, measured.Onboarding.EditedGeneratedBriefs);
        Assert.Equal(1, measured.Onboarding.OwnerFieldEdits);
        var seed = MeasuredQueueChecks.Seed(store);
        var knowledge = Items<CompanyKnowledge>(store, "knowledge").Single(k => k.Id == profile.KnowledgeId);
        var prepare = C("assignment.prepare") with
        {
            EmployeeId = seed.EmployeeId,
            ThreadId = seed.ThreadId,
            Skill = MarketingModule.StarterPlan,
            Body = "A fictional first plan",
            Id = knowledge.Id,
            ExpectedVersion = knowledge.Version,
            Digest = knowledge.Digest,
            CreditAllowance = 1,
        };
        using var receipt = JsonDocument.Parse(store.Apply(prepare));
        store.Apply(prepare);
        var task = receipt.RootElement.GetProperty("task").Deserialize<CompanyTask>(CompanyJson.Options)!;
        var attempt = receipt.RootElement.GetProperty("attempt").Deserialize<CompanyAttempt>(CompanyJson.Options)!;
        Assert.Empty(store.Progress("owner@example.invalid").Work.FirstAssignment!.Results!);
        Run(store, attempt);
        store.Finish(attempt.Id, new("A fictional starter plan", 30, "fictional-plan", new(20, 10, BilledUsd: 0.02m)));
        Assert.Equal("awaiting_review", store.Progress().Work.FirstAssignment!.State);
        Assert.Null(store.Progress().Work.FirstAssignment!.CompletedAt);
        var rawResult = Items<CompanyArtifact>(store, "artifacts").Single(a => a.TaskId == task.Id);
        var result = Assert.Single(store.Progress("owner@example.invalid").Work.FirstAssignment!.Results!);
        Assert.Equal(new(rawResult.Id, rawResult.Digest, "model-output", _now), result);
        Assert.Equal("needs_review", rawResult.State);
        Assert.Equal(0, store.Progress().Work.Completed);
        Assert.DoesNotContain(rawResult.Body, CompanyJson.Encode(store.Progress()));
        Assert.DoesNotContain(rawResult.Id, CompanyJson.Encode(Store("co-neighbor").Progress()));
        Assert.Null(store.Progress().Work.FirstAssignment!.Results);
        foreach (var role in new[] { "viewer", "approver" })
        {
            var email = role + "@example.invalid";
            store.Apply(C("member.invite") with { Body = email, Decision = role });
            var member = store.SignIn("fictional-" + role, email, true, "Fictional member")!;
            Assert.Null(store.Progress(member.Subject).Work.FirstAssignment!.Results);
        }
        _now = _now.AddSeconds(1);
        var edited = A<CompanyArtifact>(
            store,
            C("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = store.Work(task.Id).Version,
                Body = "A fictional editorial copy, never first-run acceptance evidence.",
            }
        );
        var references = store.Progress("owner@example.invalid").Work.FirstAssignment!.Results!;
        Assert.Equal(2, references.Length);
        Assert.Equal(result, references[0]);
        Assert.Equal(new(edited.Id, edited.Digest, "owner-authored", _now), references[1]);
        Assert.DoesNotContain(edited.Body, CompanyJson.Encode(store.Progress("owner@example.invalid")));
        _now = _now.AddMinutes(2);
        Complete(store, task);
        var done = Store().Progress();
        Assert.Equal(task.Id, done.Work.FirstAssignment!.TaskId);
        Assert.Equal(_now, done.Work.FirstAssignment.CompletedAt);
        Assert.Equal("completed", done.Work.FirstAssignment.State);
        Assert.Equal(references, Store().Progress("owner@example.invalid").Work.FirstAssignment!.Results!);
        Assert.Equal(0.03m, done.ModelCosts.ReportedUsd);
        Assert.Equal(0.02m, done.CompletedWorkModelCosts.ReportedUsd);
        Assert.Equal(0.02m, done.AverageRecordedModelUsdPerCompletion);
        // A later manual brief is neither a generated draft nor another correction to the old model output.
        store.Apply(
            C("business.profile.review") with
            {
                Id = profile.Id,
                ExpectedVersion = profile.Version,
                Digest = profile.Digest,
                Decision = "revoke",
            }
        );
        var manual = A<CompanyBusinessProfile>(
            store,
            C("business.profile.save") with
            {
                Body = CompanyJson.Encode(new CompanyProfileInput(identity.Version, fields)),
            }
        );
        store.Apply(
            C("business.profile.review") with
            {
                Id = manual.Id,
                ExpectedVersion = manual.Version,
                Digest = manual.Digest,
                Decision = "approve",
            }
        );
        Assert.Equal(1, store.Progress().Onboarding.GeneratedBriefs);
        Assert.Equal(1, store.Progress().Onboarding.OwnerFieldEdits);
        Assert.Equal(2, store.Progress().Onboarding.ApprovedBriefs);
        Assert.Equal(360, store.Progress().Onboarding.SecondsToApprovedBrief);
    }

    [Fact]
    public void IncompleteAndEstimatedCostsAreNotPresentedAsFreeOrComplete()
    {
        var store = Store(price: new(2, 10));
        var task = MeasuredQueueChecks.Seed(store);
        var attempt = Prepare(store, task);
        Run(store, attempt);
        Assert.Equal(1, store.Progress().ModelCosts.HeldJobs);
        Assert.Null(store.Progress().AverageRecordedModelUsdPerCompletion);
        store.Finish(attempt.Id, new("A fictional priced result", 30, "fictional-estimate", new(20, 10)));
        Complete(store, task);
        Assert.Equal(0.00014m, Store(price: new(999, 999)).Progress().AverageRecordedModelUsdPerCompletion);
        Assert.Equal(0, store.Progress().ModelCosts.ReportedUsd);
        Assert.Equal(0.00014m, store.Progress().ModelCosts.EstimatedUsd);
        var unpriced = Store(price: null);
        var second = MeasuredQueueChecks.Seed(unpriced);
        var next = Prepare(unpriced, second);
        Run(unpriced, next);
        unpriced.Finish(next.Id, new("An unpriced fictional result", 30, "fictional-unpriced", new(20, 10)));
        Complete(unpriced, second);
        Assert.Equal(1, unpriced.Progress().CompletedWorkModelCosts.UnpricedJobs);
        Assert.Null(unpriced.Progress().AverageRecordedModelUsdPerCompletion);
        // Older owner-authored work may have no model receipt at all. Zero spending must not be inferred.
        Sql("DELETE FROM co_usage_entries WHERE company_id=$0 AND id=$1", store.CompanyId, next.Id);
        Sql("DELETE FROM co_reservations WHERE company_id=$0 AND id=$1", store.CompanyId, next.Id);
        Assert.Equal(1, store.Progress().CompletedTasksWithoutModelReceipt);
        Assert.Null(store.Progress().AverageRecordedModelUsdPerCompletion);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
