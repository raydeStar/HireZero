using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

public sealed partial class BusinessDraftTests : IDisposable
{
    private const string Owner = "owner@example.invalid",
        Runtime = "fixture-business-v1",
        SourceText = "Fictional paper kits for adult hobbyists.";
    private readonly TemporaryRoot _root = new("business-draft-");

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: Runtime,
            companyId: company
        );

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static T[] Items<T>(CompanyStore store, string key) =>
        JsonSerializer
            .SerializeToElement(store.Dashboard(), CompanyJson.Options)
            .GetProperty(key)
            .Deserialize<T[]>(CompanyJson.Options)!;

    private static (CompanyBusinessIdentity Identity, CompanyBusinessSource Source) Setup(CompanyStore store)
    {
        var identity = Apply<CompanyBusinessIdentity>(
            store,
            Cmd("business.identify") with
            {
                Body = CompanyJson.Encode(
                    new CompanyBusinessInput(
                        "Fictional Paper Studio",
                        "https://paper.example.invalid/",
                        "Plan a small launch"
                    )
                ),
            }
        );
        identity = Apply<CompanyBusinessIdentity>(
            store,
            Cmd("business.confirm") with
            {
                ExpectedVersion = identity.Version,
                Digest = identity.Digest,
            }
        );
        return (
            identity,
            store.RecordBusinessSource(Owner, identity.Version, identity.Website!, identity.Name, SourceText, false)
        );
    }

    private static CompanyBusinessDraft Prepare(
        CompanyStore store,
        CompanyBusinessIdentity identity,
        CompanyBusinessSource source
    ) =>
        Apply<CompanyBusinessDraft>(
            store,
            Cmd("business.draft.prepare") with
            {
                ExpectedVersion = identity.Version,
                Digest = identity.Digest,
                Evidence = [source.Id],
                CreditAllowance = 1,
            }
        );

    private static CompanyCommand Run(CompanyBusinessDraft draft) =>
        Cmd("business.draft.run") with
        {
            Id = draft.Id,
            ExpectedVersion = draft.Version,
            Digest = draft.Job.Digest,
            Decision = "accept-soft-limits",
        };

    private static string Answer(ModelCall call, string verification = "sourced", string? quote = null) =>
        CompanyJson.Encode(
            new
            {
                fields = CompanyStore
                    .BusinessProfileFields.Select(key =>
                        key == "business"
                            ? new CompanyProfileField(
                                key,
                                "A fictional craft business",
                                verification,
                                [new(call.Context.Evidence[0].Id, quote ?? SourceText)]
                            )
                            : new CompanyProfileField(key, "Unknown", "unknown", [])
                    )
                    .ToArray(),
            }
        );

    private sealed class Model(Func<ModelCall, CancellationToken, Task<ModelReply>>? reply = null)
        : IModelProvider,
            IMeasuredModelProvider
    {
        public int Calls;
        public ModelCall? Last;
        public bool Qualified => true;
        public string Status => "Fictional provider";
        public string RuntimeIdentity => Runtime;

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            Assert.Equal("dispatched", job.State);
            Assert.Equal(CompanyMeasuredPolicy.BusinessProfile, job.Policy);
            Assert.Equal(job.ContextDigest, CompanyJson.Digest(call.Context));
            Calls++;
            Last = call;
            return reply?.Invoke(call, token)
                ?? Task.FromResult(
                    new ModelReply(Answer(call), 125, "fictional-receipt", new(100, 25, BilledUsd: 0.001m))
                );
        }
    }

    [Fact]
    public async Task NativeAdapterAcceptsOnlyTheExplicitPreHirePolicyWithoutAnEmployee()
    {
        var model = MeasuredQueueChecks.Model(_root.Path, "company-business-profile");
        var store = new CompanyStore(
            Path.Combine(_root.Path, "native.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: model.RuntimeIdentity
        );
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        using var scheduler = new CompanyScheduler(store, model);
        await scheduler.RunBusinessDraftOnceAsync(Run(prepared), Owner, model, TestContext.Current.CancellationToken);
        var result = store.BusinessDraft(prepared.Id);
        Assert.Equal("completed", result.State);
        Assert.NotNull(result.Usage);
        Assert.StartsWith("codex:", result.ProviderReceipt);
        Assert.Equal("draft", Assert.Single(Items<CompanyBusinessProfile>(store, "businessProfiles")).State);
        Assert.Empty(Items<CompanyEmployee>(store, "employees"));
        Assert.Empty(Items<CompanyKnowledge>(store, "knowledge"));
        // The exception does not open ordinary employee jobs to an anonymous context.
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            model.CompleteMeasuredAsync(
                prepared.Call,
                prepared.Job with
                {
                    State = "dispatched",
                    Policy = CompanyMeasuredPolicy.Current,
                },
                TestContext.Current.CancellationToken
            )
        );
        var wrongScope = prepared.Call with { Context = prepared.Call.Context with { Scope = "employee" } };
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            model.CompleteMeasuredAsync(
                wrongScope,
                prepared.Job with
                {
                    State = "dispatched",
                    ContextDigest = CompanyJson.Digest(wrongScope.Context),
                },
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task AConfirmedPreSendRefusalReleasesTheAllowanceWithoutInventingAReceiptOrProfile()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        var model = new Model(
            (_, _) => throw new ModelTransportException("Fictional spend cap is too small.", 0, usage: new(0, 0))
        );
        using var scheduler = new CompanyScheduler(store, model);
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            scheduler.RunBusinessDraftOnceAsync(
                Run(prepared) with
                {
                    Decision = null,
                },
                Owner,
                model,
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(0, model.Calls);
        await scheduler.RunBusinessDraftOnceAsync(Run(prepared), Owner, model, TestContext.Current.CancellationToken);
        Assert.Equal("withheld", store.BusinessDraft(prepared.Id).State);
        Assert.Null(store.BusinessDraft(prepared.Id).ProviderReceipt);
        Assert.Empty(Items<CompanyBusinessProfile>(store, "businessProfiles"));
        Assert.All(Items<CompanyBudget>(store, "budgets"), b => Assert.Equal((0L, 0L), (b.Used, b.Reserved)));
    }

    [Fact]
    public void RevokingTheApprovedBriefInvalidatesAnAlreadyPreparedFirstEmployeeReply()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var fields = CompanyStore
            .BusinessProfileFields.Select(key => new CompanyProfileField(
                key,
                key == "business" ? "Fictional paper kits" : "Unknown",
                key == "business" ? "sourced" : "unknown",
                key == "business" ? [new(source.Id, SourceText)] : []
            ))
            .ToArray();
        var profile = Apply<CompanyBusinessProfile>(
            store,
            Cmd("business.profile.save") with
            {
                Body = CompanyJson.Encode(new CompanyProfileInput(identity.Version, fields)),
            }
        );
        profile = Apply<CompanyBusinessProfile>(
            store,
            Cmd("business.profile.review") with
            {
                Id = profile.Id,
                ExpectedVersion = profile.Version,
                Digest = profile.Digest,
                Decision = "approve",
            }
        );
        var work = MeasuredQueueChecks.Seed(store);
        var knowledge = Items<CompanyKnowledge>(store, "knowledge").Single(k => k.Id == profile.KnowledgeId);
        var firstRequest = Cmd("conversation.prepare") with
        {
            Id = knowledge.Id,
            ExpectedVersion = knowledge.Version,
            Digest = knowledge.Digest,
            EmployeeId = work.EmployeeId,
            ThreadId = work.ThreadId,
            Scope = "employee",
            Body = "Prepare a small first marketing plan from approved facts.",
            CreditAllowance = 1,
        };
        Assert.Throws<WorkspaceException>(() => store.Apply(firstRequest with { Digest = "changed" }));
        using var prepared = JsonDocument.Parse(store.Apply(firstRequest));
        var attempt = prepared.RootElement.GetProperty("attempt").Deserialize<CompanyAttempt>(CompanyJson.Options)!;
        Assert.Contains("Fictional paper kits", store.TextCall(attempt.Id).Context.CompanyKnowledge);
        var packet = store.TextCall(attempt.Id);
        var provenance = Assert.IsType<CompanyBusinessProvenance>(packet.Context.BusinessBrief);
        Assert.Equal(profile.Id, provenance.ProfileId);
        Assert.Equal(profile.Digest, provenance.Digest);
        Assert.Equal(profile.UpdatedAt, provenance.ApprovedAt);
        var evidence = Assert.Single(provenance.Sources);
        Assert.Equal(source.Url, evidence.Url);
        Assert.Equal(source.Digest, evidence.Digest);
        Assert.Equal(source.RetrievedAt, evidence.RetrievedAt);
        Assert.Equal(SourceText, Assert.Single(provenance.Fields.Single(f => f.Key == "business").Citations).Quote);
        Assert.Contains("hypotheses are ideas and unknowns remain unknown", packet.Instructions);
        Assert.Equal(
            CompanyJson.Encode(provenance),
            CompanyJson.Encode(Store().TextCall(attempt.Id).Context.BusinessBrief)
        );
        Assert.Equal(profile.KnowledgeId, store.Work(attempt.TaskId).CompanyKnowledgeId);
        store.Apply(
            Cmd("business.profile.review") with
            {
                Id = profile.Id,
                ExpectedVersion = profile.Version,
                Digest = profile.Digest,
                Decision = "revoke",
            }
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(firstRequest with { RequestId = Guid.NewGuid().ToString("D") })
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                Cmd("measured.run") with
                {
                    Id = attempt.Id,
                    ExpectedVersion = attempt.Version,
                    Digest = attempt.Measured!.Digest,
                    Decision = "accept-soft-limits",
                }
            )
        );
        Assert.All(Items<CompanyBudget>(store, "budgets"), b => Assert.Equal(0, b.Reserved));
    }

    [Fact]
    public async Task ExactAuthorizedDraftRunsBeforeAnyEmployeeAndReplayNeverCallsTheProviderAgain()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var model = new Model();
        using var scheduler = new CompanyScheduler(store, model);
        var prepared = Prepare(store, identity, source);
        Assert.Equal(0, model.Calls);
        Assert.Empty(Items<CompanyBudget>(store, "budgets"));
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            scheduler.RunBusinessDraftOnceAsync(
                Run(prepared) with
                {
                    Digest = "wrong",
                },
                Owner,
                model,
                TestContext.Current.CancellationToken
            )
        );
        var run = Run(prepared);
        await scheduler.RunBusinessDraftOnceAsync(run, Owner, model, TestContext.Current.CancellationToken);
        await scheduler.RunBusinessDraftOnceAsync(run, Owner, model, TestContext.Current.CancellationToken);
        Assert.Equal(1, model.Calls);
        var result = store.BusinessDraft(prepared.Id);
        Assert.Equal("completed", result.State);
        var profile = Assert.Single(Items<CompanyBusinessProfile>(store, "businessProfiles"));
        Assert.Equal(result.ProfileId, profile.Id);
        var revision = Assert.Single(store.BusinessProfileHistory().Items);
        Assert.Equal("model", revision.Origin);
        Assert.Equal(result.Id, revision.DraftId);
        Assert.Equal("Profile drafting service", revision.ActorName);
        Assert.Equal("draft", profile.State);
        Assert.Equal("owner", profile.Fields.Single(f => f.Key == "goals").Verification);
        Assert.Empty(Items<CompanyEmployee>(store, "employees"));
        Assert.Empty(Items<CompanyThread>(store, "threads"));
        Assert.Empty(Items<CompanyKnowledge>(store, "knowledge"));
        Assert.Equal("business-profile", model.Last!.Context.Scope);
        Assert.Equal("", model.Last.Context.EmployeeId);
        Assert.Equal("", model.Last.Context.Notes);
        Assert.Empty(model.Last.Context.History);
        Assert.All(
            Items<CompanyBudget>(store, "budgets"),
            b =>
            {
                Assert.Equal(0, b.Reserved);
                Assert.Equal(125, b.Used);
            }
        );
        var spend = JsonSerializer.SerializeToElement(store.Dashboard(), CompanyJson.Options).GetProperty("spend");
        Assert.Equal(0.001m, spend.GetProperty("usd").GetDecimal());
        Assert.Equal("company-research", spend.GetProperty("employees")[0].GetProperty("employeeId").GetString());
        Assert.Equal("completed", Store().BusinessDraft(prepared.Id).State);
    }

    [Fact]
    public async Task InventedQuotesAndForgedOwnerAuthorityAreWithheldButUsageIsSettled()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        foreach (
            var response in new Func<ModelCall, string>[]
            {
                c => Answer(c, quote: "Invented evidence"),
                c => Answer(c, "owner"),
                _ => "not JSON",
            }
        )
        {
            var model = new Model(
                (call, _) => Task.FromResult(new ModelReply(response(call), 125, "fictional-invalid", new(100, 25)))
            );
            using var scheduler = new CompanyScheduler(store, model);
            var prepared = Prepare(store, identity, source);
            await scheduler.RunBusinessDraftOnceAsync(
                Run(prepared),
                Owner,
                model,
                TestContext.Current.CancellationToken
            );
            Assert.Equal("invalid", store.BusinessDraft(prepared.Id).State);
        }
        Assert.Empty(Items<CompanyBusinessProfile>(store, "businessProfiles"));
        Assert.Equal(375, Items<CompanyBudget>(store, "budgets").Single(b => b.Id == "company-total").Used);
        Assert.All(Items<CompanyBudget>(store, "budgets"), b => Assert.Equal(0, b.Reserved));
    }

    [Fact]
    public async Task CapturedOwnerCorrectionsSurviveExtractionAndConcurrentEditsFenceTheResponse()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var fields = CompanyStore
            .BusinessProfileFields.Select(key => new CompanyProfileField(
                key,
                key == "services" ? "Owner-only workshop correction" : "Unknown",
                key == "services" ? "owner" : "unknown",
                []
            ))
            .ToArray();
        var original = Apply<CompanyBusinessProfile>(
            store,
            Cmd("business.profile.save") with
            {
                Body = CompanyJson.Encode(new CompanyProfileInput(identity.Version, fields)),
            }
        );
        var first = Prepare(store, identity, source);
        var model = new Model();
        using var scheduler = new CompanyScheduler(store, model);
        await scheduler.RunBusinessDraftOnceAsync(Run(first), Owner, model, TestContext.Current.CancellationToken);
        var profile = Items<CompanyBusinessProfile>(store, "businessProfiles")
            .Single(p => p.Id == store.BusinessDraft(first.Id).ProfileId);
        Assert.Equal("Owner-only workshop correction", profile.Fields.Single(f => f.Key == "services").Value);
        var stale = Prepare(store, identity, source);
        var editingModel = new Model(
            (call, _) =>
            {
                var updated = profile
                    .Fields.Select(f => f.Key == "services" ? f with { Value = "Newer owner correction" } : f)
                    .ToArray();
                store.Apply(
                    Cmd("business.profile.save") with
                    {
                        Id = profile.Id,
                        ExpectedVersion = profile.Version,
                        Body = CompanyJson.Encode(new CompanyProfileInput(identity.Version, updated)),
                    }
                );
                return Task.FromResult(new ModelReply(Answer(call), 125, "fictional-stale", new(100, 25)));
            }
        );
        using var editingScheduler = new CompanyScheduler(store, editingModel);
        await editingScheduler.RunBusinessDraftOnceAsync(
            Run(stale),
            Owner,
            editingModel,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("withheld", store.BusinessDraft(stale.Id).State);
        Assert.Equal(2, Items<CompanyBusinessProfile>(store, "businessProfiles").Length);
    }

    [Fact]
    public async Task UnknownOutcomeSurvivesRestartHoldsAllowanceAndNeedsExactOwnerReconciliation()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        var model = new Model((_, _) => throw new HttpRequestException("Synthetic transport loss"));
        using var scheduler = new CompanyScheduler(store, model);
        var run = Run(prepared);
        await scheduler.RunBusinessDraftOnceAsync(run, Owner, model, TestContext.Current.CancellationToken);
        var reopened = Store();
        reopened.Recover();
        var uncertain = reopened.BusinessDraft(prepared.Id);
        Assert.Equal("unknown", uncertain.State);
        Assert.Equal(
            CompanyMeasuredPolicy.BusinessProfile.PlanningTokens,
            Items<CompanyBudget>(reopened, "budgets").Single(b => b.Id == "company-total").Reserved
        );
        using var afterRestart = new CompanyScheduler(reopened, model);
        await afterRestart.RunBusinessDraftOnceAsync(run, Owner, model, TestContext.Current.CancellationToken);
        Assert.Equal(1, model.Calls);
        var other = Prepare(reopened, identity, source);
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            afterRestart.RunBusinessDraftOnceAsync(Run(other), Owner, model, TestContext.Current.CancellationToken)
        );
        var reconcile = Cmd("business.draft.reconcile") with
        {
            Id = uncertain.Id,
            ExpectedVersion = uncertain.Version,
            Digest = uncertain.Job.Digest,
            Decision = "confirmed-usage-and-termination",
            Body = CompanyJson.Encode(
                new CompanyBusinessReconciliation(
                    "fictional-confirmed",
                    new(150, 50),
                    "Checked the fictional provider receipt and stopped request",
                    true
                )
            ),
        };
        Assert.Throws<WorkspaceException>(() => reopened.Apply(reconcile with { Digest = "wrong" }));
        reopened.Apply(reconcile);
        Assert.Equal("reconciled", reopened.BusinessDraft(uncertain.Id).State);
        var budget = Items<CompanyBudget>(reopened, "budgets").Single(b => b.Id == "company-total");
        Assert.Equal(0, budget.Reserved);
        Assert.Equal(200, budget.Used);
        Assert.Empty(Items<CompanyBusinessProfile>(reopened, "businessProfiles"));
    }

    [Fact]
    public void RestartAfterDispatchNeverResumesAndLateOutputOnlySettlesItsReceipt()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        store.Apply(Run(prepared));
        var reopened = Store();
        reopened.Recover();
        Assert.Equal("unknown", reopened.BusinessDraft(prepared.Id).State);
        reopened.FinishBusinessDraft(prepared.Id, new(Answer(prepared.Call), 125, "fictional-late", new(100, 25)));
        Assert.Equal("withheld", reopened.BusinessDraft(prepared.Id).State);
        Assert.Empty(Items<CompanyBusinessProfile>(reopened, "businessProfiles"));
        Assert.All(Items<CompanyBudget>(reopened, "budgets"), b => Assert.Equal(0, b.Reserved));
    }

    [Fact]
    public async Task ObservedOverBudgetUsageStaysHeldUntilExplicitlyAccountedForWithoutHidingTheOverrun()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        var model = new Model(
            (call, _) => Task.FromResult(new ModelReply(Answer(call), 30000, "fictional-overrun", new(27000, 3000)))
        );
        using var scheduler = new CompanyScheduler(store, model);
        await scheduler.RunBusinessDraftOnceAsync(Run(prepared), Owner, model, TestContext.Current.CancellationToken);
        var over = store.BusinessDraft(prepared.Id);
        Assert.Equal("unknown", over.State);
        var reconcile = Cmd("business.draft.reconcile") with
        {
            Id = over.Id,
            ExpectedVersion = over.Version,
            Digest = over.Job.Digest,
            Decision = "confirmed-usage-and-termination",
            Body = CompanyJson.Encode(
                new CompanyBusinessReconciliation(
                    "fictional-overrun",
                    new(27000, 3000),
                    "Confirmed full usage and termination",
                    true
                )
            ),
        };
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                reconcile with
                {
                    Body = CompanyJson.Encode(
                        new CompanyBusinessReconciliation("fictional-overrun", new(1, 1), "Attempted reduction", true)
                    ),
                }
            )
        );
        store.Apply(reconcile);
        Assert.Equal(30000, Items<CompanyBudget>(store, "budgets").Single(b => b.Id == "company-total").Used);
        Assert.Empty(Items<CompanyBusinessProfile>(store, "businessProfiles"));
    }

    [Fact]
    public void RequestsCannotBorrowSourcesOrJobsFromAnotherCompanyOrRunAsAnApprover()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        var other = Store("other-fictional");
        var (otherIdentity, _) = Setup(other);
        Assert.Throws<WorkspaceException>(() => Prepare(other, otherIdentity, source));
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => other.BusinessDraft(prepared.Id)).Status);
        Assert.Throws<WorkspaceException>(() => other.Apply(Run(prepared)));
        store.Apply(Cmd("member.invite") with { Body = "approver@example.invalid", Decision = "approver" });
        var member = store.SignIn("fictional-approver", "approver@example.invalid", true, "Fictional Approver")!;
        foreach (var action in new[] { "business.draft.prepare", "business.draft.run", "business.draft.reconcile" })
            Assert.Equal(
                403,
                Assert.Throws<WorkspaceException>(() => store.Apply(Cmd(action), member.Subject, out _)).Status
            );
    }

    [Fact]
    public void ExpiredAndStaleRequestsAndInsufficientBudgetsCannotConsumeAnAuthorization()
    {
        var now = DateTimeOffset.UtcNow;
        var timed = new CompanyStore(
            Path.Combine(_root.Path, "expiry.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: Runtime
        )
        {
            Clock = () => now,
        };
        var (identity, source) = Setup(timed);
        var expired = Prepare(timed, identity, source);
        now = now.AddMinutes(11);
        Assert.Throws<WorkspaceException>(() => timed.Apply(Run(expired)));
        Assert.Empty(Items<CompanyBudget>(timed, "budgets"));
        var stale = Prepare(timed, identity, source);
        timed.Apply(
            Cmd("business.identify") with
            {
                ExpectedVersion = identity.Version,
                Body = CompanyJson.Encode(
                    new CompanyBusinessInput("Changed Fictional Studio", identity.Website, identity.Goal)
                ),
            }
        );
        Assert.Throws<WorkspaceException>(() => timed.Apply(Run(stale)));
        var limited = Store();
        var (limitedIdentity, limitedSource) = Setup(limited);
        limited.Apply(Cmd("budget.configure") with { Id = "company-total", Budget = 1000 });
        var prepared = Prepare(limited, limitedIdentity, limitedSource);
        Assert.Throws<WorkspaceException>(() => limited.Apply(Run(prepared)));
        Assert.Equal(0, Assert.Single(Items<CompanyBudget>(limited, "budgets")).Reserved);
        Assert.Equal("proposed", limited.BusinessDraft(prepared.Id).State);
    }

    [Fact]
    public async Task IncompleteAndExplicitlyDisputedAccountingNeverReleasesTheReservation()
    {
        foreach (var disputed in new[] { false, true })
        {
            var store = Store(disputed ? "disputed-fictional" : "incomplete-fictional");
            var (identity, source) = Setup(store);
            var prepared = Prepare(store, identity, source);
            var model = new Model(
                (call, _) =>
                    disputed
                        ? throw new ModelTransportException(
                            "Fictional disputed accounting",
                            125,
                            "fictional-disputed",
                            new(100, 25, BilledUsd: 0.1m),
                            accountingViolation: true
                        )
                        : Task.FromResult(new ModelReply(Answer(call), 125, "fictional-incomplete"))
            );
            using var scheduler = new CompanyScheduler(store, model);
            await scheduler.RunBusinessDraftOnceAsync(
                Run(prepared),
                Owner,
                model,
                TestContext.Current.CancellationToken
            );
            Assert.Equal("unknown", store.BusinessDraft(prepared.Id).State);
            Assert.Equal(26000, Items<CompanyBudget>(store, "budgets").Single(b => b.Id == "company-total").Reserved);
            Assert.Empty(Items<CompanyBusinessProfile>(store, "businessProfiles"));
        }
    }

    [Fact]
    public async Task CancellationFencesLateSuccessfulContentAndWaitsForItsUsageReceipt()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        var completion = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new Model((_, _) => completion.Task);
        using var scheduler = new CompanyScheduler(store, model);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = scheduler.RunBusinessDraftOnceAsync(Run(prepared), Owner, model, cancel.Token);
        cancel.Cancel();
        for (var i = 0; i < 100 && store.BusinessDraft(prepared.Id).State != "unknown"; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("unknown", store.BusinessDraft(prepared.Id).State);
        completion.SetResult(new(Answer(prepared.Call), 125, "fictional-late-canceled", new(100, 25)));
        await running;
        Assert.Equal("withheld", store.BusinessDraft(prepared.Id).State);
        Assert.Empty(Items<CompanyBusinessProfile>(store, "businessProfiles"));
        Assert.All(Items<CompanyBudget>(store, "budgets"), b => Assert.Equal(0, b.Reserved));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
