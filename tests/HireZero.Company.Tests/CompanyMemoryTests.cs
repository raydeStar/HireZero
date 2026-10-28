using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed partial class CompanyMemoryTests : IDisposable
{
    private readonly TemporaryRoot _root = new("company-memory-");
    private DateTimeOffset _now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-runtime",
            companyId: company
        )
        {
            Clock = () => _now,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static CompanyMemoryInput Input(string title = "Cobalt campaign decision") =>
        new("decision", title, "Use patient cobalt campaign demonstrations.", "Owner chose the educational approach.");

    private static CompanyMemoryItem Save(
        CompanyStore store,
        CompanyMemoryInput? input = null,
        CompanyMemoryItem? previous = null
    ) =>
        Apply<CompanyMemoryItem>(
            store,
            C("memory.save") with
            {
                Id = previous?.Id,
                ExpectedVersion = previous?.Version ?? 0,
                Body = CompanyJson.Encode(input ?? Input()),
            }
        );

    private static CompanyCommand Review(CompanyMemoryItem item, string decision = "approve") =>
        C("memory.review") with
        {
            Id = item.Id,
            ExpectedVersion = item.Version,
            Digest = item.Digest,
            Decision = decision,
        };

    private static CompanyMemoryItem Approve(CompanyStore store, CompanyMemoryItem item) =>
        Apply<CompanyMemoryItem>(store, Review(item, item.Replacement is null ? "approve" : "approve-and-replace"));

    private static CompanyAttempt Prepare(CompanyStore store, CompanyTask work)
    {
        using var result = JsonDocument.Parse(
            store.Apply(
                C("conversation.prepare") with
                {
                    EmployeeId = work.EmployeeId,
                    ThreadId = work.ThreadId,
                    Scope = "employee",
                    Body = "Draft a cobalt campaign note",
                    CreditAllowance = 1,
                }
            )
        );
        return result.RootElement.GetProperty("attempt").Deserialize<CompanyAttempt>(CompanyJson.Options)!;
    }

    private static CompanyCommand Run(CompanyAttempt attempt) =>
        C("measured.run") with
        {
            Id = attempt.Id,
            ExpectedVersion = attempt.Version,
            Digest = attempt.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    private static CompanyArtifact Result(CompanyStore store, CompanyTask work)
    {
        var artifact = Apply<CompanyArtifact>(
            store,
            C("artifact.create") with
            {
                Id = work.Id,
                ExpectedVersion = work.Version,
                Body = "Cobalt campaign used patient paper demonstrations.",
            }
        );
        return Apply<CompanyArtifact>(
            store,
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
    }

    [Fact]
    public void OnlyExactOwnerApprovalIndexesMemoryAndReviewedRecordsStayImmutable()
    {
        var store = Store();
        store.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = "approver" });
        var member = store.SignIn("fictional-memory-reviewer", "reviewer@example.invalid", true, "Fictional Reviewer")!;
        foreach (var action in new[] { "memory.save", "memory.review" })
            Assert.Equal(
                403,
                Assert.Throws<WorkspaceException>(() => store.Apply(C(action), member.Subject, out _)).Status
            );
        var draft = Save(store);
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(draft) with { Digest = "wrong" }));
        var edited = Save(store, Input() with { Rationale = "Owner corrected the reason." }, draft);
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(draft)));
        var approved = Approve(store, edited);
        var match = Assert.Single(Store().RetrieveMemory("cobalt").Items);
        Assert.Equal(approved.Id, match.MemoryId);
        Assert.Equal(approved.Version, match.Version);
        Assert.Equal(approved.Digest, match.Digest);
        Assert.Throws<WorkspaceException>(() => Save(store, Input(), approved));
        Assert.Throws<WorkspaceException>(() => Approve(store, Save(store)));
        store.Apply(Review(approved, "revoke"));
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        Assert.Equal("revoked", Store().Memory(approved.Id).Memory.State);
    }

    [Fact]
    public void RetrievalEnforcesCompanyDepartmentEmployeeAndCompanyConversationAudiences()
    {
        var store = Store();
        var first = MeasuredQueueChecks.Seed(store);
        var second = MeasuredQueueChecks.Seed(store);
        var employee = MeasuredQueueChecks
            .Items<CompanyEmployee>(store, "employees")
            .Single(e => e.Id == first.EmployeeId);
        var common = Approve(store, Save(store));
        var department = Approve(store, Save(store, Input() with { DepartmentId = employee.DepartmentId }));
        var personal = Approve(store, Save(store, Input() with { EmployeeId = first.EmployeeId }));
        var ids = store.RetrieveMemory("cobalt", first.EmployeeId).Items.Select(m => m.MemoryId).ToHashSet();
        Assert.Equal(3, ids.Count);
        Assert.Contains(department.Id, ids);
        Assert.Contains(personal.Id, ids);
        Assert.Equal(common.Id, Assert.Single(store.RetrieveMemory("cobalt", second.EmployeeId).Items).MemoryId);
        Assert.Equal(common.Id, Assert.Single(store.RetrieveMemory("cobalt", first.EmployeeId, true).Items).MemoryId);
        var other = Store("co-other-memory");
        Assert.Empty(other.Memories());
        Assert.Empty(other.RetrieveMemory("cobalt").Items);
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.Memory(common.Id)).Status);
        Assert.Equal(
            404,
            Assert.Throws<WorkspaceException>(() => other.RetrieveMemory("cobalt", first.EmployeeId)).Status
        );
        Assert.Equal(
            404,
            Assert
                .Throws<WorkspaceException>(() => Save(other, Input() with { DepartmentId = employee.DepartmentId }))
                .Status
        );
        Assert.Equal(
            404,
            Assert.Throws<WorkspaceException>(() => Save(other, Input() with { EmployeeId = first.EmployeeId })).Status
        );
    }

    [Fact]
    public void ReplacementIsAtomicAndPinsTheExactEarlierVersion()
    {
        var store = Store();
        var original = Approve(store, Save(store));
        var input = Input() with
        {
            Statement = "Use cobalt workshops for the next campaign.",
            ReplacesId = original.Id,
        };
        var draft = Save(store, input);
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(draft)));
        Assert.Equal(original.Id, Assert.Single(store.RetrieveMemory("cobalt").Items).MemoryId);
        var revised = Approve(store, draft);
        Assert.Equal(revised.Id, Assert.Single(store.RetrieveMemory("cobalt").Items).MemoryId);
        Assert.Equal("superseded", store.Memory(original.Id).Memory.State);
        Assert.Equal(original.Definition, store.Memory(original.Id).Memory.Definition);
        var next = Save(store, input with { ReplacesId = revised.Id });
        store.Apply(Review(revised, "revoke"));
        Assert.Throws<WorkspaceException>(() => Approve(store, next));
        Assert.Equal("draft", store.Memory(next.Id).Memory.State);
        Approve(store, Save(store, next.Definition, next));
        Assert.Equal("superseded", store.Memory(revised.Id).Memory.State);
    }

    [Fact]
    public void ReviewDatesFenceImmediatelyAndMaintenanceEmitsOnlyOneDurableNotice()
    {
        var store = Store();
        var original = Approve(store, Save(store, Input() with { ReviewOn = "2026-10-26" }));
        Assert.True(store.Memory(original.Id).Usable);
        _now = _now.AddDays(1);
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        Assert.False(store.Memory(original.Id).Usable);
        Assert.Equal(1, store.MaintainMemory());
        Assert.Equal(0, Store().MaintainMemory());
        var due = Store().Memory(original.Id).Memory;
        Assert.Equal("needs_review", due.State);
        Assert.Equal(original.Definition, due.Definition);
        using var db = new StoreConnection(store.Database.Open(), store.Database);
        Assert.Equal("1", db.Scalar("SELECT COUNT(*) FROM co_notices WHERE company_id=$0", store.CompanyId));
        var renewal = Save(store, due.Definition with { ReplacesId = due.Id, ReviewOn = "2026-11-25" });
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        var approved = Approve(store, renewal);
        Assert.Equal(approved.Id, Assert.Single(Store().RetrieveMemory("cobalt").Items).MemoryId);
    }

    [Fact]
    public void PriorWorkNeedsExactApprovedEvidenceAndRevocationFencesItsMemory()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        Assert.Throws<WorkspaceException>(() => Save(store, Input() with { Kind = "prior-work" }));
        var artifact = Result(store, task);
        var input = Input() with
        {
            Kind = "prior-work",
            ArtifactId = artifact.Id,
            Quote = "patient paper demonstrations",
        };
        Assert.Throws<WorkspaceException>(() => Save(store, input with { Quote = "Unsubstantiated claim" }));
        var memory = Approve(store, Save(store, input));
        Assert.Equal(artifact.Version, Assert.Single(store.RetrieveMemory("cobalt").Items).Origin!.Version);
        Assert.Null(Assert.Single(store.MemoryWorks()).Text);
        Assert.Equal(artifact.Body, store.MemoryWork(artifact.Id).Text);
        store.Apply(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "revoke",
            }
        );
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        Assert.False(store.Memory(memory.Id).Usable);
        Assert.Equal(1, store.MaintainMemory());
        Assert.Throws<WorkspaceException>(() => Save(store, input));
        Assert.Empty(store.MemoryWorks());
        Assert.Throws<WorkspaceException>(() => store.MemoryWork(artifact.Id));
    }

    [Fact]
    public void ResultFeedbackEntersOnlyItsEmployeesFutureContextAfterOwnerReview()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var otherTask = MeasuredQueueChecks.Seed(store);
        var artifact = Result(store, task);
        var draft = Save(
            store,
            Input("Cobalt campaign feedback") with
            {
                Kind = "feedback",
                Statement = "Keep the patient cobalt demonstrations and add one concise next step.",
                EmployeeId = task.EmployeeId,
                ArtifactId = artifact.Id,
                Quote = "patient paper demonstrations",
            }
        );
        Assert.Empty(store.RetrieveMemory("cobalt", task.EmployeeId).Items);
        var other = Store("co-other-feedback");
        Assert.Empty(other.MemoryWorks());
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.MemoryWork(artifact.Id)).Status);
        var approved = Approve(store, draft);
        Assert.Empty(store.RetrieveMemory("cobalt", otherTask.EmployeeId).Items);
        Assert.Empty(store.RetrieveMemory("cobalt", task.EmployeeId, true).Items);
        var next = Prepare(store, task);
        var remembered = Assert.Single(store.TextCall(next.Id).Context.Memory!.Items);
        Assert.Equal(approved.Id, remembered.MemoryId);
        Assert.Equal("feedback", remembered.Kind);
        Assert.Equal(artifact.Digest, remembered.Origin!.Digest);
        Assert.Equal(draft.Definition.Statement, remembered.Statement);
        store.Apply(Review(approved, "revoke"));
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(next)));
        Assert.Empty(store.RetrieveMemory("cobalt", task.EmployeeId).Items);
    }

    [Fact]
    public void RevocationStopsPreparedWorkAndWithholdsLateOutputWhileSettlingUsage()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var memory = Approve(store, Save(store));
        var attempt = Prepare(store, task);
        var packet = store.TextCall(attempt.Id);
        Assert.Equal(memory.Id, Assert.Single(packet.Context.Memory!.Items).MemoryId);
        store.Apply(Review(memory, "revoke"));
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(attempt)));
        Assert.Equal(CompanyJson.Encode(packet.Context), CompanyJson.Encode(Store().TextCall(attempt.Id).Context));
        var lateStore = Store("co-late-memory");
        var lateTask = MeasuredQueueChecks.Seed(lateStore);
        var lateMemory = Approve(lateStore, Save(lateStore));
        var late = Prepare(lateStore, lateTask);
        lateStore.Apply(Run(late));
        Assert.True(lateStore.IsCurrent(late.Id));
        Approve(
            lateStore,
            Save(
                lateStore,
                lateMemory.Definition with
                {
                    ReplacesId = lateMemory.Id,
                    Statement = "Cobalt now uses workshops.",
                }
            )
        );
        Assert.False(lateStore.IsCurrent(late.Id));
        lateStore.Finish(late.Id, new("Fictional late reply", 30, "fictional-late-memory-receipt", new(20, 10)));
        Assert.Equal("withheld", lateStore.Attempt(late.Id).Phase);
        Assert.Equal(30, lateStore.Attempt(late.Id).ReportedUsage);
        Assert.Empty(MeasuredQueueChecks.Items<CompanyArtifact>(lateStore, "artifacts"));
        Assert.All(MeasuredQueueChecks.Items<CompanyBudget>(lateStore, "budgets"), b => Assert.Equal(0, b.Reserved));
    }

    [Fact]
    public void MemoryIsBoundedAndCannotLaunderRevokedContextThroughApprovedWork()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        var initial = Approve(store, Save(store, Input("Cobalt paper decision")));
        var attempt = store.Claim(task.EmployeeId, true, task.Id)!;
        Assert.Equal(initial.Id, Assert.Single(store.Capture(attempt.Id).Memory!.Items).MemoryId);
        Assert.True(store.AuthorizeDispatch(attempt.Id));
        store.Finish(
            attempt.Id,
            new("Cobalt work used patient demonstrations.", 30, "fictional-memory-receipt", new(20, 10))
        );
        var artifact = Assert.Single(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        artifact = Apply<CompanyArtifact>(
            store,
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        var source = Input("Cobalt previous work") with
        {
            Kind = "prior-work",
            ArtifactId = artifact.Id,
            Quote = "patient demonstrations",
        };
        var replacement = Save(store, source with { ReplacesId = initial.Id });
        Assert.Throws<WorkspaceException>(() => Approve(store, replacement));
        Assert.Equal("approved", store.Memory(initial.Id).Memory.State);
        var derived = Approve(store, Save(store, source));
        store.Apply(Review(initial, "revoke"));
        Assert.False(store.Memory(derived.Id).Usable);
        Assert.Empty(store.MemoryWorks());
        Assert.Throws<WorkspaceException>(() => store.MemoryWork(artifact.Id));
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        for (var i = 0; i < 5; i++)
            Approve(store, Save(store, Input("Cobalt record " + i)));
        Assert.Equal(3, store.RetrieveMemory("cobalt").Items.Length);
        Assert.Empty(store.RetrieveMemory("the and of").Items);
        Assert.Empty(store.RetrieveMemory("quartzophone").Items);
        Assert.Throws<WorkspaceException>(() => Save(store, Input() with { Statement = new string('x', 1201) }));
        Assert.Throws<WorkspaceException>(() => Save(store, Input() with { ReviewOn = "2026-01-01" }));
    }

    [Fact]
    public void AutomaticMemoryFitsTheExistingPacketAllowanceAndOldPacketsKeepTheirShape()
    {
        var store = Store();
        var task = MeasuredQueueChecks.Seed(store);
        for (var i = 0; i < 3; i++)
            Approve(
                store,
                Save(
                    store,
                    Input("Cobalt paper record " + i) with
                    {
                        Statement = string.Concat(Enumerable.Repeat("Paper kit guidance. ", 60)),
                        Rationale = new string('r', 600),
                    }
                )
            );
        Assert.Equal(3, store.RetrieveMemory("paper").Items.Length);
        var attempt = Apply<CompanyAttempt>(
            store,
            C("measured.prepare") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                CreditAllowance = 1,
            }
        );
        Assert.True(attempt.Measured!.ApplicationBytes <= CompanyMeasuredPolicy.Current.ApplicationBytes);
        var packet = store.TextCall(attempt.Id);
        Assert.True(packet.Context.Memory!.Items.Length < 3);
        Assert.Contains("omitted", packet.Context.Memory.Notice);
        Assert.Equal(task.Input, packet.Context.Input);
        Assert.Contains("Adult hobbyists", packet.Context.DepartmentKnowledge);
        var old = packet.Context with { Memory = null };
        Assert.DoesNotContain("\"memory\":", CompanyJson.Encode(old));
        Assert.Equal(
            CompanyJson.Digest(old),
            CompanyJson.Digest(CompanyJson.Decode<CompanyContext>(CompanyJson.Encode(old)))
        );
    }

    [Fact]
    public void MigrationPreservesPriorDataAndChecksums()
    {
        var store = Store();
        var employee = MeasuredQueueChecks.Seed(store).EmployeeId;
        string[] checksums;
        using (var db = new StoreConnection(store.Database.Open(), store.Database))
        {
            checksums = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_decisions; DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE IF EXISTS co_sandbox_jobs; DROP TABLE IF EXISTS co_compute_policy; DROP TABLE IF EXISTS co_workspace_leases; DROP TABLE IF EXISTS co_usage_entries; DROP TABLE IF EXISTS co_usage_policy; DROP TABLE co_memory_terms; DROP TABLE co_memory; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=18;"
            );
        }
        var reopened = Store();
        Assert.Contains(MeasuredQueueChecks.Items<CompanyEmployee>(reopened, "employees"), e => e.Id == employee);
        Assert.Empty(reopened.Memories());
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            checksums,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            )
        );
        Approve(reopened, Save(reopened));
        Assert.Single(Store().RetrieveMemory("cobalt").Items);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
