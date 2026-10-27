using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed class CompanyWorkControlTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-work-control-");

    private CompanyStore Store(string id = CompanyStore.DefaultCompanyId) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-work-control",
            companyId: id
        )
        {
            SandboxRuntimeIdentity = "fictional-script",
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    internal static CompanyWorkControl Set(CompanyStore store, bool paused) =>
        CompanyJson.Decode<CompanyWorkControl>(
            store.Apply(
                C("company.work.configure") with
                {
                    ExpectedVersion = store.WorkControl().Version,
                    Decision = paused ? "pause-new-work" : "resume-new-work",
                }
            )
        );

    private static CompanyAttempt Prepare(CompanyStore store, CompanyTask task) =>
        JsonSerializer
            .SerializeToElement(
                CompanyJson.Decode<object>(
                    store.Apply(
                        C("conversation.prepare") with
                        {
                            EmployeeId = task.EmployeeId,
                            ThreadId = task.ThreadId,
                            Scope = "employee",
                            Body = "Draft an internal fictional campaign note",
                            CreditAllowance = 1,
                        }
                    )
                ),
                CompanyJson.Options
            )
            .GetProperty("attempt")
            .Deserialize<CompanyAttempt>(CompanyJson.Options)!;

    private static CompanyCommand Run(CompanyAttempt attempt) =>
        C("measured.run") with
        {
            Id = attempt.Id,
            ExpectedVersion = attempt.Version,
            Digest = attempt.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    [Fact]
    public void PausePersistsIsTenantScopedAndNeverReplacesOtherSafetyLimits()
    {
        var store = Store();
        Assert.False(store.WorkControl().Paused);
        var command = C("company.work.configure") with { ExpectedVersion = 0, Decision = "pause-new-work" };
        var receipt = store.Apply(command);
        Assert.Equal(receipt, store.Apply(command));
        Assert.Single(
            store.AuditEntries(),
            a =>
                a.Kind == "event"
                && JsonDocument.Parse(a.Body).RootElement.GetProperty("type").GetString() == "company.work-paused"
        );
        Assert.True(Store().WorkControl().Paused);
        Assert.False(Store("fictional-neighbor").WorkControl().Paused);
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(command with { RequestId = Guid.NewGuid().ToString("D"), Decision = "resume-new-work" })
        );
        store.Apply(
            C("usage.configure") with
            {
                ExpectedVersion = store.Usage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyUsageLimits(3, 9000, true)),
            }
        );
        Assert.True(store.WorkControl().Paused);
        Set(store, false);
        Assert.Equal(new CompanyUsageLimits(3, 9000, true), store.Usage().Policy.Limits);
        Assert.False(store.WorkControl().Paused);
        Assert.True(
            CompanyJson
                .Decode<CompanyUsagePolicy>(
                    "{\"id\":\"monthly\",\"limits\":{},\"version\":2,\"updatedAt\":\"2026-01-01T00:00:00Z\"}"
                )
                .WorkPaused == false
        );
        store.Apply(C("member.invite") with { Body = "viewer@example.invalid", Decision = "viewer" });
        var viewer = store.SignIn("fictional-viewer", "viewer@example.invalid", true, "Fictional Viewer")!;
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => store.Apply(command, viewer.Subject, out _)).Status);
    }

    [Fact]
    public void ModelAdmissionStopsButAlreadyAdmittedUsageStillSettles()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var prepared = Prepare(store, task);
        Set(store, true);
        Assert.Contains(
            "New work is paused",
            Assert.Throws<WorkspaceException>(() => store.Apply(Run(prepared))).Message
        );
        Assert.Empty(store.Usage().Entries);
        Assert.Equal("claimed", store.Attempt(prepared.Id).Phase);
        Set(store, false);
        store.Apply(Run(prepared));
        Set(store, true);
        Assert.True(store.IsCurrent(prepared.Id));
        store.Finish(prepared.Id, new("Fictional response", 30, "fictional-receipt", new(20, 10)));
        Assert.Equal("settled", Assert.Single(store.Usage().Entries).State);
        Assert.Equal(0, store.Usage().Current.HeldJobs);
        Assert.Null(store.Claim(task.EmployeeId, true));
        Assert.Equal("committed", store.Attempt(prepared.Id).Phase);
    }

    [Fact]
    public void ScriptAdmissionStopsWithoutRevokingAnEarlierAdmissionOrRecovery()
    {
        var store = Store();
        store.Apply(
            C("compute.configure") with
            {
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(new CompanyComputeLimits(Paused: false)),
            }
        );
        var task = MeasuredQueueChecks.Seed(store);
        var job = CompanyJson.Decode<CompanySandboxJob>(
            store.Apply(
                C("sandbox.prepare") with
                {
                    Id = task.Id,
                    ExpectedVersion = task.Version,
                    Body = CompanyJson.Encode(
                        new CompanySandboxInput(
                            "Fictional report",
                            "console.log('Fictional')",
                            [],
                            ["report.txt"],
                            new(5)
                        )
                    ),
                }
            )
        );
        var start = C("sandbox.run") with
        {
            Id = job.Id,
            ExpectedVersion = job.Version,
            Digest = job.Digest,
            Decision = "approve-once",
        };
        Set(store, true);
        Assert.Contains("New work is paused", Assert.Throws<WorkspaceException>(() => store.Apply(start)).Message);
        Assert.Equal(0, store.ComputeUsage().HeldJobs);
        Set(store, false);
        store.Apply(start);
        Set(store, true);
        Assert.NotNull(store.TakeSandboxDispatch(job.Id, "fictional-script"));
        Assert.True(store.SandboxAuthorized(job.Id));
        Assert.Single(store.PendingSandboxRecovery());
        Assert.Equal(1, store.ComputeUsage().HeldJobs);
        store.Apply(C("sandbox.revoke") with { Id = job.Id, ExpectedVersion = store.SandboxJob(job.Id).Version });
        Assert.True(store.SandboxJob(job.Id).Revoked);
        Assert.Equal(1, store.ComputeUsage().HeldJobs);
    }

    [Fact]
    public async Task PauseAndAdmissionAreSerializedWithoutOrphaningUsage()
    {
        var store = Store();
        var prepared = Prepare(store, MeasuredQueueChecks.Seed(store));
        var other = Store();
        var admission = Task.Run(() =>
        {
            try
            {
                other.Apply(Run(prepared));
                return true;
            }
            catch (WorkspaceException)
            {
                return false;
            }
        });
        var pause = Task.Run(() => Set(store, true));
        await Task.WhenAll(admission, pause);
        Assert.True(Store().WorkControl().Paused);
        if (await admission)
        {
            Assert.Equal("dispatching", store.Attempt(prepared.Id).Phase);
            Assert.Single(store.Usage().Entries);
            store.Finish(prepared.Id, new("Fictional response", 30, "fictional-race-receipt", new(20, 10)));
            Assert.Equal("settled", Assert.Single(store.Usage().Entries).State);
        }
        else
        {
            Assert.Equal("claimed", store.Attempt(prepared.Id).Phase);
            Assert.Empty(store.Usage().Entries);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

public sealed partial class CompanyScheduleTests
{
    [Fact]
    public void CompanyPausePreservesSchedulesAndResumesWithExistingMissedSlotRules()
    {
        var store = Store();
        var schedule = Approve(store, Draft(store, Definition(Employee(store))));
        CompanyWorkControlTests.Set(store, true);
        _now = DateTimeOffset.Parse("2026-10-26T09:00:00Z");
        Assert.Null(store.ClaimDueSchedule());
        Assert.Equal(0, store.ScheduleDetail(schedule.Id).Schedule.Consumed);
        _now = DateTimeOffset.Parse("2026-10-27T11:00:00Z");
        CompanyWorkControlTests.Set(store, false);
        Assert.Equal("missed", store.ClaimDueSchedule()!.State);
        Assert.Null(store.ClaimDueSchedule());
        Assert.Empty(store.Usage().Entries);
    }
}

public sealed partial class CompanyBrowserHostingTests
{
    [Fact]
    public void CompanyPauseBlocksNewBrowserAllowanceButKeepsAdmittedRecovery()
    {
        var store = Store();
        Enable(store);
        var job = Apply<CompanyBrowserJob>(store, Prepare(MeasuredQueueChecks.Seed(store)));
        CompanyWorkControlTests.Set(store, true);
        Assert.Contains("New work is paused", Assert.Throws<WorkspaceException>(() => store.Apply(Run(job))).Message);
        Assert.Equal(0, store.ComputeUsage().HeldJobs);
        CompanyWorkControlTests.Set(store, false);
        store.Apply(Run(job));
        CompanyWorkControlTests.Set(store, true);
        Assert.NotNull(store.TakeBrowserDispatch(job.Id, Runtime));
        Assert.True(store.BrowserAuthorized(job.Id));
        Assert.Single(store.PendingBrowserRecovery());
    }
}

public sealed partial class CompanyMemoryAnalysisTests
{
    [Fact]
    public void CompanyPausePreventsMemoryAnalysisAdmissionWithoutConsumingAllowance()
    {
        var store = Store();
        var analysis = Prepare(store, Sources(store));
        CompanyWorkControlTests.Set(store, true);
        Assert.Contains(
            "New work is paused",
            Assert.Throws<WorkspaceException>(() => store.Apply(Run(analysis))).Message
        );
        Assert.Empty(store.Usage().Entries);
        CompanyWorkControlTests.Set(store, false);
        store.Apply(Run(analysis));
        Assert.Single(store.Usage().Entries);
    }
}

public sealed partial class BusinessDraftTests
{
    [Fact]
    public void CompanyPausePreventsProfileModelAdmissionWithoutConsumingAllowance()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var draft = Prepare(store, identity, source);
        CompanyWorkControlTests.Set(store, true);
        Assert.Contains("New work is paused", Assert.Throws<WorkspaceException>(() => store.Apply(Run(draft))).Message);
        Assert.Empty(store.Usage().Entries);
        CompanyWorkControlTests.Set(store, false);
        store.Apply(Run(draft));
        Assert.Single(store.Usage().Entries);
    }
}

public sealed partial class BusinessLookupTests
{
    [Fact]
    public void CompanyPauseRefusesNewResearchButPreservesAlreadyAdmittedSearch()
    {
        var store = Store();
        var identity = Identity(store);
        var lookup = CompanyJson.Decode<CompanyBusinessLookup>(store.Apply(Start(identity)));
        CompanyWorkControlTests.Set(store, true);
        Assert.Contains(
            "New work is paused",
            Assert.Throws<WorkspaceException>(() => store.Apply(Start(identity))).Message
        );
        Assert.True(store.ConsumeBusinessSearch(lookup.Id, lookup.Query));
        Assert.False(store.ConsumeBusinessSearch(lookup.Id, lookup.Query));
    }
}

public sealed partial class WikiPublicationTests
{
    [Fact]
    public async Task CompanyPauseDoesNotConsumePublicationApprovalOrSendIt()
    {
        var company = await Hire();
        var publication = ApprovePublication(company, await Proposed(company, Draft(company, Content())));
        CompanyWorkControlTests.Set(company.Store, true);
        Assert.Contains(
            "New work is paused",
            (await Assert.ThrowsAsync<WorkspaceException>(() => Publish(company, publication))).Message
        );
        Assert.Equal("approved", company.Store.Publication(publication.Id).State);
        Assert.Empty(_contributions);
        Assert.Empty(_approvals);
        CompanyWorkControlTests.Set(company.Store, false);
        Assert.Equal("published", ((CompanyPublication)await Publish(company, publication)).State);
        Assert.Single(_contributions);
        Assert.Single(_approvals);
    }
}
