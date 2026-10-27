using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

public sealed partial class CompanyMemoryAnalysisTests : IDisposable
{
    private const string Owner = "owner@example.invalid",
        Runtime = "fictional-memory-analysis-v1";
    private readonly TemporaryRoot _root = new("memory-analysis-");
    private DateTimeOffset _now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private CompanyStore Store(
        string company = CompanyStore.DefaultCompanyId,
        string runtime = Runtime,
        Func<CompanyModelRouting>? routing = null
    ) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: runtime,
            companyId: company
        )
        {
            Clock = () => _now,
            CurrentModelRouting = routing,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    [Fact]
    public void MemoryChecksUseOptionalAnalysisRouteAndExpireWhenItChanges()
    {
        var routing = new CompanyModelRouting { AnalysisModel = "openai/fictional-analysis" };
        var s = Store(routing: () => routing);
        var sources = Sources(s);
        var prepared = Prepare(s, sources);
        Assert.Equal(routing.AnalysisModel, prepared.Call.ModelRoute);
        Assert.Equal(routing.AnalysisModel, prepared.Job.Route);
        routing = routing with { AnalysisModel = null };
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(prepared)));
        var fresh = Prepare(s, sources);
        Assert.Equal(routing.TextModel, fresh.Job.Route);
        Assert.Empty(s.Usage().Entries);
    }

    private static T Apply<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    private static CompanyMemoryItem Memory(
        CompanyStore s,
        string title,
        string statement,
        bool approved = true,
        string? reviewOn = null
    )
    {
        var item = Apply<CompanyMemoryItem>(
            s,
            C("memory.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyMemoryInput(
                        "decision",
                        title,
                        statement,
                        "Fictional owner guidance.",
                        ReviewOn: reviewOn
                    )
                ),
            }
        );
        return approved ? Apply<CompanyMemoryItem>(s, Review(item)) : item;
    }

    private static CompanyCommand Review(CompanyMemoryItem item, string decision = "approve") =>
        C("memory.review") with
        {
            Id = item.Id,
            ExpectedVersion = item.Version,
            Digest = item.Digest,
            Decision = decision,
        };

    private static CompanyMemoryItem[] Sources(CompanyStore s) =>
        [
            Memory(s, "Workshop duration", "Workshops last sixty minutes."),
            Memory(s, "Workshop planning", "Workshops last ninety minutes."),
        ];

    private static CompanyMemoryAnalysis Prepare(CompanyStore s, params CompanyMemoryItem[] sources) =>
        Apply<CompanyMemoryAnalysis>(
            s,
            C("memory.analysis.prepare") with
            {
                Evidence = sources.Select(m => m.Id).ToArray(),
                CreditAllowance = 1,
            }
        );

    private static CompanyCommand Run(CompanyMemoryAnalysis a) =>
        C("memory.analysis.run") with
        {
            Id = a.Id,
            ExpectedVersion = a.Version,
            Digest = a.Job.Digest,
            Decision = "accept-soft-limits",
        };

    private static string Answer(ModelCall call)
    {
        using var input = JsonDocument.Parse(call.Context.Input);
        var sources = input.RootElement.GetProperty("records").Deserialize<CompanyMemoryItem[]>(CompanyJson.Options)!;
        return CompanyJson.Encode(
            new
            {
                findings = new[]
                {
                    new CompanyMemoryFinding(
                        "possible-conflict",
                        "Check which duration applies to the current workshops.",
                        sources.Select(s => new CompanyMemoryCitation(s.Id, s.Definition.Statement)).ToArray()
                    ),
                },
            }
        );
    }

    private static ModelReply Reply(ModelCall call) =>
        new(Answer(call), 140, "fictional-analysis-receipt", new(100, 40, BilledUsd: .001m));

    private sealed class Model(Func<ModelCall, CancellationToken, Task<ModelReply>>? reply = null)
        : IModelProvider,
            IMeasuredModelProvider
    {
        public int Calls;
        public bool Qualified => false;
        public string Status => "Fictional memory analysis provider";
        public string RuntimeIdentity => Runtime;

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            Assert.Equal(CompanyMeasuredPolicy.MemoryAnalysis, job.Policy);
            Assert.Equal("memory-analysis", call.Context.Scope);
            Assert.Equal("dispatched", job.State);
            Calls++;
            return reply?.Invoke(call, token) ?? Task.FromResult(Reply(call));
        }
    }

    private async Task Execute(CompanyStore store, CompanyMemoryAnalysis report, Model? model = null)
    {
        model ??= new();
        using var scheduler = new CompanyScheduler(store, model);
        await scheduler.RunMemoryAnalysisOnceAsync(Run(report), Owner, model, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SelectedSourcesProduceCitedReviewAdviceWithoutChangingMemoryAndSettleUsage()
    {
        var s = Store();
        var sources = Sources(s);
        Memory(s, "Unselected secret", "Fictional private unrelated guidance.");
        var before = CompanyJson.Encode(s.Memories());
        var prepared = Prepare(s, sources);
        Assert.Empty(s.Usage().Entries);
        Assert.DoesNotContain("private unrelated", prepared.Call.Context.Input);
        Assert.Empty(prepared.Call.Context.Evidence);
        await Execute(s, prepared);
        var result = s.MemoryAnalysis(prepared.Id);
        Assert.True(result.SourcesCurrent);
        Assert.Equal("completed", result.Analysis.State);
        Assert.Equal(2, Assert.Single(result.Analysis.Findings!).Citations.Length);
        Assert.Equal(before, CompanyJson.Encode(s.Memories()));
        var usage = Assert.Single(s.Usage().Entries);
        Assert.Equal(
            ("memory-analysis", "settled", "completed", 140L),
            (usage.Kind, usage.State, usage.Outcome, usage.ActualTokens)
        );
        Assert.Equal(.001m, usage.ReportedUsd);
        Assert.Equal("fictional-analysis-receipt", usage.Receipt);
        Assert.Single(s.MemoryAnalyses());
    }

    [Fact]
    public void InvalidSelectionsAndNonOwnersAreRefusedBeforeAnyReservation()
    {
        var s = Store();
        var sources = Sources(s);
        Assert.Throws<WorkspaceException>(() => Prepare(s, sources[0]));
        Assert.Throws<WorkspaceException>(() => Prepare(s, sources[0], sources[0]));
        Assert.Throws<WorkspaceException>(() => Prepare(s, [.. Enumerable.Repeat(sources[0], 9)]));
        var draft = Memory(s, "Draft", "Not approved guidance.", false);
        Assert.Throws<WorkspaceException>(() => Prepare(s, sources[0], draft));
        var other = Store("other-company");
        Assert.Throws<WorkspaceException>(() => Prepare(other, sources));
        s.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = "approver" });
        var reviewer = s.SignIn("fictional-reviewer", "reviewer@example.invalid", true, "Reviewer")!;
        foreach (var action in new[] { "memory.analysis.prepare", "memory.analysis.run", "memory.analysis.reconcile" })
            Assert.Equal(
                403,
                Assert.Throws<WorkspaceException>(() => s.Apply(C(action), reviewer.Subject, out _)).Status
            );
        Assert.Empty(s.Usage().Entries);
    }

    [Fact]
    public async Task DueDatesCanBeExaminedButRevokedSourcesAndExpiredAuthorityCannotDispatch()
    {
        var s = Store();
        var sources = Sources(s);
        var due = Memory(s, "Seasonal guidance", "Use the autumn workshop format.", reviewOn: "2026-10-27");
        _now = _now.AddDays(2);
        Assert.Equal(1, s.MaintainMemory());
        due = s.Memory(due.Id).Memory;
        Assert.False(s.Memory(due.Id).Usable);
        var prepared = Prepare(s, sources[0], due);
        await Execute(s, prepared);
        Assert.Equal("completed", s.MemoryAnalysis(prepared.Id).Analysis.State);
        var expired = Prepare(s, sources);
        _now = _now.AddMinutes(11);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(expired)));
        var stale = Prepare(s, sources);
        s.Apply(Review(sources[0], "revoke"));
        Assert.False(s.MemoryAnalysis(stale.Id).SourcesCurrent);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(stale)));
        Assert.Single(s.Usage().Entries);
    }

    [Fact]
    public async Task InvalidEvidenceIsWithheldAndStillChargedForItsObservedUsage()
    {
        var s = Store();
        var sources = Sources(s);
        var good = new CompanyMemoryFinding(
            "possible-conflict",
            "Verify duration.",
            sources.Select(m => new CompanyMemoryCitation(m.Id, m.Definition.Statement)).ToArray()
        );
        string[] invalid =
        [
            "null",
            "{}",
            "{\"findings\":null}",
            "{\"findings\":[null]}",
            CompanyJson.Encode(new { findings = new[] { good with { Kind = "verified-fact" } } }),
            CompanyJson.Encode(new { findings = new[] { good with { Citations = [good.Citations[0]] } } }),
            CompanyJson.Encode(
                new { findings = new[] { good with { Citations = [good.Citations[0], good.Citations[0]] } } }
            ),
            CompanyJson.Encode(
                new
                {
                    findings = new[]
                    {
                        good with
                        {
                            Citations = [good.Citations[0] with { Quote = "Invented duration" }, good.Citations[1]],
                        },
                    },
                }
            ),
            CompanyJson.Encode(
                new
                {
                    findings = new[]
                    {
                        good with
                        {
                            Citations = [good.Citations[0] with { MemoryId = "foreign-source" }, good.Citations[1]],
                        },
                    },
                }
            ),
            CompanyJson.Encode(new { findings = new[] { good with { Explanation = new string('a', 601) } } }),
            CompanyJson.Encode(new { findings = Enumerable.Repeat(good, 7) }),
            new string('x', 14001),
        ];
        foreach (var body in invalid)
        {
            var prepared = Prepare(s, sources);
            await Execute(
                s,
                prepared,
                new Model((_, _) => Task.FromResult(new ModelReply(body, 140, "fictional-invalid", new(100, 40))))
            );
            var result = s.MemoryAnalysis(prepared.Id).Analysis;
            Assert.Equal("invalid", result.State);
            Assert.Null(result.Findings);
        }
        Assert.Equal(invalid.Length * 140, s.Usage().Current.Tokens);
        Assert.Equal(0, s.Usage().Current.HeldTokens);
        Assert.All(s.Memories(), m => Assert.Equal("approved", m.Memory.State));
    }

    [Fact]
    public async Task AChangedSourceDuringDispatchWithholdsOutputButKeepsTheCostReceipt()
    {
        var s = Store();
        var sources = Sources(s);
        var prepared = Prepare(s, sources);
        var model = new Model(
            (call, _) =>
            {
                s.Apply(Review(sources[0], "revoke"));
                return Task.FromResult(Reply(call));
            }
        );
        await Execute(s, prepared, model);
        Assert.Equal("withheld", s.MemoryAnalysis(prepared.Id).Analysis.State);
        Assert.Null(s.MemoryAnalysis(prepared.Id).Analysis.Findings);
        Assert.Equal(140, Assert.Single(s.Usage().Entries).ActualTokens);
    }

    [Fact]
    public async Task CompletedReportBecomesHistoricalAfterRevisionButDoesNotDependOnCurrentModelConnection()
    {
        var s = Store();
        var sources = Sources(s);
        var prepared = Prepare(s, sources);
        await Execute(s, prepared);
        Assert.True(Store(runtime: "changed-runtime").MemoryAnalysis(prepared.Id).SourcesCurrent);
        s.Apply(Review(sources[0], "revoke"));
        var report = s.MemoryAnalysis(prepared.Id);
        Assert.False(report.SourcesCurrent);
        Assert.Equal("completed", report.Analysis.State);
        Assert.Single(report.Analysis.Findings!);
        Assert.Equal("approved", report.Analysis.Sources[0].State);
    }

    [Fact]
    public async Task UnknownOutcomeIsNeverReplayedAndRetainsAllowanceUntilExactReconciliation()
    {
        var s = Store();
        var sources = Sources(s);
        var prepared = Prepare(s, sources);
        var model = new Model((_, _) => throw new HttpRequestException("Fictional uncertain outcome"));
        using var scheduler = new CompanyScheduler(s, model);
        var run = Run(prepared);
        await scheduler.RunMemoryAnalysisOnceAsync(run, Owner, model, TestContext.Current.CancellationToken);
        await scheduler.RunMemoryAnalysisOnceAsync(run, Owner, model, TestContext.Current.CancellationToken);
        Assert.Equal(1, model.Calls);
        var reopened = Store();
        reopened.Recover();
        var unknown = reopened.MemoryAnalysis(prepared.Id).Analysis;
        Assert.Equal("unknown", unknown.State);
        Assert.Equal(26000, reopened.Usage().Current.HeldTokens);
        Assert.Throws<WorkspaceException>(() => reopened.Apply(Run(Prepare(reopened, sources))));
        var c = C("memory.analysis.reconcile") with
        {
            Id = unknown.Id,
            ExpectedVersion = unknown.Version,
            Digest = unknown.Job.Digest,
            Decision = "confirmed-usage-and-termination",
            Body = CompanyJson.Encode(
                new CompanyMemoryAnalysisReconciliation(
                    "fictional-verified",
                    new(150, 50),
                    "Verified provider receipt and termination",
                    true
                )
            ),
        };
        Assert.Throws<WorkspaceException>(() => reopened.Apply(c with { Digest = "wrong" }));
        reopened.Apply(c);
        Assert.Equal("reconciled", reopened.MemoryAnalysis(prepared.Id).Analysis.State);
        Assert.Equal(0, reopened.Usage().Current.HeldTokens);
        Assert.Equal(200, reopened.Usage().Current.Tokens);
        reopened.FinishMemoryAnalysis(prepared.Id, Reply(prepared.Call));
        Assert.Null(reopened.MemoryAnalysis(prepared.Id).Analysis.Findings);
        Assert.Equal(200, reopened.Usage().Current.Tokens);
    }

    [Fact]
    public void RestartAfterDispatchNeverImportsALateReport()
    {
        var s = Store();
        var prepared = Prepare(s, Sources(s));
        s.Apply(Run(prepared));
        var reopened = Store();
        reopened.Recover();
        Assert.Equal("unknown", reopened.MemoryAnalysis(prepared.Id).Analysis.State);
        reopened.FinishMemoryAnalysis(prepared.Id, Reply(prepared.Call));
        Assert.Equal("withheld", reopened.MemoryAnalysis(prepared.Id).Analysis.State);
        Assert.Null(reopened.MemoryAnalysis(prepared.Id).Analysis.Findings);
        Assert.Equal(0, reopened.Usage().Current.HeldTokens);
    }

    [Fact]
    public async Task MonthlyUsageLimitAndRuntimeChangesPreventDispatch()
    {
        var s = Store();
        var sources = Sources(s);
        s.Apply(
            C("usage.configure") with
            {
                ExpectedVersion = s.Usage().Policy.Version,
                Body = CompanyJson.Encode(new CompanyUsageLimits(1, 26000)),
            }
        );
        var first = Prepare(s, sources);
        Assert.Throws<WorkspaceException>(() => Store(runtime: "new-runtime").Apply(Run(first)));
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(first) with { Decision = null }));
        await Execute(s, first);
        var next = Prepare(s, sources);
        Assert.Throws<WorkspaceException>(() => s.Apply(Run(next)));
        Assert.Single(s.Usage().Entries);
    }

    [Fact]
    public async Task NativeAdapterOnlyAllowsTheExplicitOwnerMemoryPolicyAndScope()
    {
        _now = DateTimeOffset.UtcNow;
        var model = MeasuredQueueChecks.Model(_root.Path, "company-memory-analysis");
        var s = Store(runtime: model.RuntimeIdentity);
        var prepared = Prepare(s, Sources(s));
        using var scheduler = new CompanyScheduler(s, model);
        await scheduler.RunMemoryAnalysisOnceAsync(Run(prepared), Owner, model, TestContext.Current.CancellationToken);
        var result = s.MemoryAnalysis(prepared.Id).Analysis;
        Assert.Equal("completed", result.State);
        Assert.Empty(result.Findings!);
        Assert.StartsWith("codex:", result.ProviderReceipt);
        var wrong = prepared.Call with { Context = prepared.Call.Context with { Scope = "employee" } };
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            model.CompleteMeasuredAsync(
                wrong,
                prepared.Job with
                {
                    State = "dispatched",
                    ContextDigest = CompanyJson.Digest(wrong.Context),
                },
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task CanceledDispatchAcceptsOnlyLateAccountingAndDoesNotResume()
    {
        var s = Store();
        var prepared = Prepare(s, Sources(s));
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new Model(
            (_, token) =>
            {
                cancellation.Cancel();
                token.Register(() => completion.TrySetResult(Reply(prepared.Call)));
                return completion.Task;
            }
        );
        using var scheduler = new CompanyScheduler(s, model);
        await scheduler.RunMemoryAnalysisOnceAsync(Run(prepared), Owner, model, cancellation.Token);
        Assert.Equal("withheld", s.MemoryAnalysis(prepared.Id).Analysis.State);
        Assert.Null(s.MemoryAnalysis(prepared.Id).Analysis.Findings);
        Assert.Equal(140, s.Usage().Current.Tokens);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task OverBudgetReceiptCannotBeReducedDuringReconciliation()
    {
        var s = Store();
        var prepared = Prepare(s, Sources(s));
        await Execute(
            s,
            prepared,
            new Model(
                (call, _) =>
                    Task.FromResult(
                        new ModelReply(Answer(call), 30000, "fictional-overrun", new(27000, 3000, BilledUsd: .03m))
                    )
            )
        );
        var unknown = s.MemoryAnalysis(prepared.Id).Analysis;
        Assert.Equal("unknown", unknown.State);
        var c = C("memory.analysis.reconcile") with
        {
            Id = unknown.Id,
            ExpectedVersion = unknown.Version,
            Digest = unknown.Job.Digest,
            Decision = "confirmed-usage-and-termination",
        };
        Assert.Throws<WorkspaceException>(() =>
            s.Apply(
                c with
                {
                    Body = CompanyJson.Encode(
                        new CompanyMemoryAnalysisReconciliation(
                            "fictional-verified",
                            new(100, 40),
                            "Verified receipt",
                            true
                        )
                    ),
                }
            )
        );
        Assert.Throws<WorkspaceException>(() =>
            s.Apply(
                c with
                {
                    Body = CompanyJson.Encode(
                        new CompanyMemoryAnalysisReconciliation(
                            "fictional-verified",
                            new(27000, 3000),
                            "Verified receipt",
                            false
                        )
                    ),
                }
            )
        );
        s.Apply(
            c with
            {
                Body = CompanyJson.Encode(
                    new CompanyMemoryAnalysisReconciliation(
                        "fictional-verified",
                        new(27000, 3000),
                        "Verified receipt",
                        true
                    )
                ),
            }
        );
        Assert.Equal(30000, s.Usage().Current.Tokens);
        Assert.Equal(.03m, Assert.Single(s.Usage().Entries).ReportedUsd);
    }

    [Fact]
    public async Task HttpReportsAndOperationReceiptsStayOwnerOnlyAndCompanyScopedIncludingMidflightDemotion()
    {
        CompanyStore? store = null;
        string? secondOwner = null;
        var model = new Model(
            (call, ignoredToken) =>
            {
                store!.Apply(
                    C("member.role") with
                    {
                        Id = Owner,
                        ExpectedVersion = 1,
                        Decision = "viewer",
                    },
                    secondOwner!,
                    out _
                );
                return Task.FromResult(Reply(call));
            }
        );
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
            fixture: model,
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            settings: CompanySettings.Load(_ => null)
        )!;
        var token = TestContext.Current.CancellationToken;
        await app.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new(
                app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()
            ),
        };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        store = app.Services.GetRequiredService<CompanyStore>();
        var sources = Sources(store);
        var prepare = C("memory.analysis.prepare") with
        {
            Evidence = sources.Select(s => s.Id).ToArray(),
            CreditAllowance = 1,
        };
        using var preparedResponse = await client.PostAsJsonAsync("/api/company/commands", prepare, token);
        Assert.Equal(HttpStatusCode.OK, preparedResponse.StatusCode);
        var report = (await preparedResponse.Content.ReadFromJsonAsync<CompanyMemoryAnalysis>(token))!;
        using (var response = await client.GetAsync("/api/company/memory/analyses/" + report.Id, token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        using (
            var response = await client.PostAsJsonAsync(
                "/api/companies",
                new { name = "Fictional neighboring studio" },
                token
            )
        )
        {
            var neighbor = (await response.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetString();
            client.DefaultRequestHeaders.Add("X-HireZero-Company", neighbor);
        }
        using (var response = await client.GetAsync("/api/company/memory/analyses/" + report.Id, token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await client.GetAsync("/api/company/operations/" + prepare.RequestId, token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/company/memory/analyses", token))!);
        client.DefaultRequestHeaders.Remove("X-HireZero-Company");
        store.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        secondOwner = store
            .SignIn("fictional-second-memory-owner", "second-owner@example.invalid", true, "Second owner")!
            .Subject;
        var run = Run(report);
        using (var response = await client.PostAsJsonAsync("/api/company/commands", run, token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("withheld", store.MemoryAnalysis(report.Id).Analysis.State);
        Assert.Equal(140, store.Usage().Current.Tokens);
        foreach (
            var route in new[]
            {
                "/api/company/memory/analyses",
                "/api/company/memory/analyses/" + report.Id,
                "/api/company/operations/" + prepare.RequestId,
                "/api/company/operations/" + run.RequestId,
            }
        )
        {
            using var response = await client.GetAsync(route, token);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(1, model.Calls);
        var audit = store.AuditEntries().Where(a => a.Kind == "memory-analysis").ToArray();
        Assert.NotEmpty(audit);
        Assert.All(audit, a => Assert.DoesNotContain("Workshops last", a.Body));
        Assert.True(store.VerifyAudit().Intact);
        await app.StopAsync(token);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
