using System.Net;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Integrations.Jev;
using HireZero.Integrations.ModelApis;
using HireZero.Integrations.OpenRouter;
using Microsoft.AspNetCore.DataProtection;

public sealed class VultrOnlyTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("vultr-only-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly ModelVendorPolicy Policy = new("vultr");

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private CompanyModelConnection Connection(ApiModelProvider provider)
    {
        var model = new CompanyModelConnection(
            CompanyStore.DefaultCompanyId,
            provider,
            Policy.Routing(new()),
            vendorPolicy: Policy
        );
        model.Initialize(_root.Path, new EphemeralDataProtectionProvider());
        return model;
    }

    private CompanyStore Store(CompanyModelConnection model) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(Policy),
            model.Routing,
            model.RuntimeIdentity
        )
        {
            CurrentModelRouting = () => model.Routing,
            CurrentMeasuredRuntimeIdentity = () => model.MeasuredIdentity,
            MeasuredTerms = model.TermsFor,
            UsagePrice = model.Price,
            UsageFunding = model.FundingFor,
            UsageRouteFunding = (kind, route) => model.FundingFor(kind, route),
            PrepareDecisionPacket = model.PrepareDecision,
            CurrentDecisionPacket = model.DecisionCurrent,
        };

    private static CompanyAttempt Prepare(CompanyStore store)
    {
        var task = MeasuredQueueChecks.Seed(store);
        return CompanyJson.Decode<CompanyAttempt>(
            store.Apply(
                C("measured.prepare") with
                {
                    Id = task.Id,
                    ExpectedVersion = task.Version,
                    CreditAllowance = 5,
                }
            )
        );
    }

    private static CompanyCommand Run(CompanyAttempt attempt) =>
        C("measured.run") with
        {
            Id = attempt.Id,
            ExpectedVersion = attempt.Version,
            Digest = attempt.Measured!.Digest,
            Decision = "accept-soft-limits",
        };

    [Fact]
    public void SettingsAreOptInAndAllOtherProviderConstructorsRefuseTheNamedBoundary()
    {
        Assert.False(CompanySettings.Load(_ => null).ModelVendor.VultrOnly);
        Assert.Throws<InvalidOperationException>(() =>
            CompanySettings.Load(name => name == ModelVendorPolicy.Variable ? "other" : null)
        );
        var settings = Path.Combine(_root.Path, "settings.json");
        File.WriteAllText(
            settings,
            """{"HIREZERO_MODEL_VENDOR":"vultr","HIREZERO_VULTR_BASE_URL":"http://127.0.0.1:12345/v1"}"""
        );
        var policy = CompanySettings.Load(name => name == CompanySettings.FileVariable ? settings : null).ModelVendor;
        var options = new CompanyCodexOptions("fictional-executable", "fictional-identity", _root.Path);
        var constructors = new Action[]
        {
            () => new CompanyCodexConnection(options, vendorPolicy: policy),
            () => new CompanyCodexCodingFactory(options, new(), [], vendorPolicy: policy),
            () => new OpenRouterToolProvider(_root.Path, vendorPolicy: policy),
            () => new OpenRouterDecisions("fictional-company", () => "fictional-key", vendorPolicy: policy),
            () => JevToolProvider.Company(policy),
            () => new JevClient(null!, policy),
            () => new ApiModelProvider(provider: "openai", vendorPolicy: policy),
            () => new ApiModelProvider(provider: "anthropic", vendorPolicy: policy),
            () => new ApiModelProvider(provider: "openrouter", vendorPolicy: policy),
        };
        foreach (var construct in constructors)
            Assert.Contains(ModelVendorPolicy.Variable, Assert.Throws<InvalidOperationException>(construct).Message);
        using var fake = new VultrProviderTests.Fake();
        using var provider = VultrProviderTests.Provider(fake);
        var model = Connection(provider);
        foreach (var vendor in new[] { "openai", "anthropic", "openrouter", "codex", "jev" })
        {
            Assert.Throws<InvalidOperationException>(() =>
                model.Save(new(0, vendor, "openai/fictional", "fictional-key", "use-company-key"))
            );
            Assert.Throws<InvalidOperationException>(() => provider.Endpoint(vendor + "/fictional"));
        }
        Assert.Equal([ModelVendorPolicy.VultrRoute], model.Options.Select(o => o.Route));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task SuccessSettlesReasoningUsageAndEveryTextPurposeUsesVultr()
    {
        using var fake = new VultrProviderTests.Fake();
        using var provider = VultrProviderTests.Provider(fake);
        var model = Connection(provider);
        var store = Store(model);
        var attempt = Prepare(store);
        Assert.Equal(ModelVendorPolicy.VultrRoute, attempt.Measured!.Route);
        Assert.Equal(24000, attempt.Measured.Policy.OutputTarget);
        Assert.Equal(60000, attempt.Measured.Policy.PlanningTokens);
        Assert.Equal(ModelVendorPolicy.VultrRoute, model.Routing.For(false, true));
        Assert.Equal(ModelVendorPolicy.VultrRoute, store.GoalTerms("marketing", "marketing-starter-plan", null).Route);
        await new CompanyScheduler(store, model).RunMeasuredOnceAsync(Run(attempt), model, Token);
        Assert.Equal("completed", store.Attempt(attempt.Id).Measured!.State);
        Assert.Equal(new ModelUsage(130, 12000, 11000), store.Attempt(attempt.Id).Usage);
        Assert.Equal(0, store.Usage().Current.HeldJobs);
        var entry = Assert.Single(store.Usage().Entries);
        Assert.Equal("vultr", entry.Funding!.Provider);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task DisconnectAndMissingUsageHoldReservationsAndNeverReplay()
    {
        using var fake = new VultrProviderTests.Fake { Disconnect = true };
        using var provider = VultrProviderTests.Provider(fake);
        var model = Connection(provider);
        var store = Store(model);
        var attempt = Prepare(store);
        var run = Run(attempt);
        var scheduler = new CompanyScheduler(store, model);
        await scheduler.RunMeasuredOnceAsync(run, model, Token);
        await scheduler.RunMeasuredOnceAsync(run, model, Token);
        Assert.Equal("unknown", store.Attempt(attempt.Id).Phase);
        Assert.Equal(1, store.Usage().Current.HeldJobs);
        Assert.Empty(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        Assert.Single(fake.Requests);
        // A different company proves that an HTTP 200 without usage also retains its own allowance.
        await using var secondRoot = new TemporaryRoot("vultr-missing-usage-");
        using var missing = new VultrProviderTests.Fake
        {
            Reply =
                """{"id":"fictional-missing","choices":[{"message":{"content":"unaccounted"},"finish_reason":"stop"}]}""",
        };
        using var missingProvider = VultrProviderTests.Provider(missing);
        var second = new CompanyModelConnection(
            "missing-company",
            missingProvider,
            Policy.Routing(new()),
            vendorPolicy: Policy
        );
        var secondStore = new CompanyStore(
            Path.Combine(secondRoot.Path, "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(Policy),
            second.Routing,
            second.RuntimeIdentity
        )
        {
            MeasuredTerms = second.TermsFor,
            UsagePrice = second.Price,
        };
        var missingAttempt = Prepare(secondStore);
        await new CompanyScheduler(secondStore, second).RunMeasuredOnceAsync(Run(missingAttempt), second, Token);
        Assert.Equal("unknown", secondStore.Attempt(missingAttempt.Id).Phase);
        Assert.Equal(1, secondStore.Usage().Current.HeldJobs);
        Assert.Single(missing.Requests);
    }

    [Fact]
    public async Task DecisionsUseTheSameVultrCapAndSettleOneDurableDispatch()
    {
        using var fake = new VultrProviderTests.Fake();
        var json = System.Text.Json.Nodes.JsonNode.Parse(fake.Reply)!;
        json["choices"]![0]!["message"]!["content"] =
            """{"choice":"draft","confidence":0.8,"probabilities":{"draft":0.8,"clarify":0.2}}""";
        fake.Reply = json.ToJsonString();
        using var provider = VultrProviderTests.Provider(fake);
        var model = Connection(provider);
        var store = Store(model);
        var prepared = CompanyJson.Decode<CompanyDecision>(
            store.Apply(
                C("decision.prepare") with
                {
                    CreditAllowance = 5,
                    Body = CompanyJson.Encode(
                        new CompanyDecisionInput(
                            ModelVendorPolicy.VultrRoute,
                            "{\"context\":\"Fictional bakery\"}",
                            "Choose the next step.",
                            new() { ["draft"] = "Draft an FAQ", ["clarify"] = "Ask a question" }
                        )
                    ),
                }
            )
        );
        Assert.Equal("vultr", prepared.Packet.Provider);
        Assert.Equal(60000, prepared.PlanningTokens);
        var command = C("decision.run") with
        {
            Id = prepared.Id,
            ExpectedVersion = prepared.Version,
            Digest = prepared.Digest,
            Decision = "accept-soft-limits",
        };
        var scheduler = new CompanyScheduler(store, model);
        await scheduler.RunDecisionOnceAsync(command, "owner@example.invalid", model.SendDecisionAsync, Token);
        await scheduler.RunDecisionOnceAsync(command, "owner@example.invalid", model.SendDecisionAsync, Token);
        var result = store.Decision(prepared.Id);
        Assert.Equal("completed", result.State);
        Assert.Equal("draft", result.Result!.Choice);
        Assert.Null(result.ReportedCostUsd);
        Assert.Null(result.Usage!.BilledUsd);
        Assert.Equal(
            ModelRoutes.Cost(ModelVendorPolicy.VultrRoute, result.Usage),
            Assert.Single(store.Usage().Entries).EstimatedUsd
        );
        Assert.Equal(0, store.Usage().Current.HeldJobs);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task HostExposesOnlyVultrAndReportsCodingRolesUnavailableBeforeHiring()
    {
        var settings = CompanySettings.Load(name =>
            name switch
            {
                ModelVendorPolicy.Variable => "vultr",
                "HIREZERO_DATA_ROOT" => _root.Path,
                "HIREZERO_EXECUTION_HOLD" => "0",
                "HIREZERO_COMPANY_TEXT_MODEL" => "openai/fictional-old-route",
                "HIREZERO_COMPANY_ANALYSIS_MODEL" => "anthropic/fictional-old-route",
                _ => null,
            }
        );
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            "owner@example.invalid",
            true,
            _root.Path,
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: _ => { },
            settings: settings
        )!;
        await app.StartAsync(Token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(app.Urls.First()),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/company");
        request.Headers.Host = "127.0.0.1:5065";
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var caps = json.RootElement.GetProperty("capabilities");
        Assert.False(caps.GetProperty("liveCoding").GetBoolean());
        Assert.Equal(
            ModelVendorPolicy.VultrRoute,
            caps.GetProperty("modelRouting").GetProperty("textModel").GetString()
        );
        Assert.Equal(
            ModelVendorPolicy.VultrRoute,
            caps.GetProperty("modelRouting").GetProperty("analysisModel").GetString()
        );
        foreach (
            var integration in caps.GetProperty("integrations")
                .EnumerateArray()
                .Where(x => x.GetProperty("id").GetString() is "openrouter" or "jev")
        )
            Assert.False(integration.GetProperty("active").GetBoolean());
        var developer = CompanyWorkspace.Definitions(Policy).Single(r => r.Id == "developer");
        Assert.Contains(ModelVendorPolicy.Variable, developer.UnavailableReason);
        await app.StopAsync(Token);
        Assert.Throws<InvalidOperationException>(() =>
            CompanyWorkspace.TryCreate(
                [CompanyWorkspace.Flag, CompanyWorkspace.CodexFlag],
                "owner@example.invalid",
                true,
                _root.Path,
                settings: settings
            )
        );
    }

    [Fact]
    public void BusinessMemorySchedulesAndDeliverablesSealVultrLimitsBeforeAnySend()
    {
        using var fake = new VultrProviderTests.Fake();
        using var provider = VultrProviderTests.Provider(fake);
        var model = Connection(provider);
        var store = Store(model);
        T Apply<T>(CompanyCommand command) => CompanyJson.Decode<T>(store.Apply(command));
        var identity = Apply<CompanyBusinessIdentity>(
            C("business.identify") with
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
            identity.Name,
            "Fictional paper kits for adult hobbyists.",
            false
        );
        var business = Apply<CompanyBusinessDraft>(
            C("business.draft.prepare") with
            {
                ExpectedVersion = identity.Version,
                Digest = identity.Digest,
                Evidence = [source.Id],
                CreditAllowance = 5,
            }
        );
        var memories = new List<CompanyMemoryItem>();
        foreach (var duration in new[] { "sixty", "ninety" })
        {
            var item = Apply<CompanyMemoryItem>(
                C("memory.save") with
                {
                    Body = CompanyJson.Encode(
                        new CompanyMemoryInput(
                            "decision",
                            "Fictional workshop " + duration,
                            "Workshops last " + duration + " minutes.",
                            "Fictional owner guidance."
                        )
                    ),
                }
            );
            memories.Add(
                Apply<CompanyMemoryItem>(
                    C("memory.review") with
                    {
                        Id = item.Id,
                        ExpectedVersion = item.Version,
                        Digest = item.Digest,
                        Decision = "approve",
                    }
                )
            );
        }
        var memory = Apply<CompanyMemoryAnalysis>(
            C("memory.analysis.prepare") with
            {
                Evidence = memories.Select(m => m.Id).ToArray(),
                CreditAllowance = 5,
            }
        );
        var task = MeasuredQueueChecks.Seed(store);
        var knowledge = Apply<CompanyKnowledge>(
            C("knowledge.save") with
            {
                Body = "Fictional paper studio; internal drafts only.",
            }
        );
        store.Apply(
            C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var schedule = Apply<CompanySchedule>(
            C("schedule.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyScheduleInput(
                        "Fictional recurring draft",
                        "employee-task",
                        "daily",
                        "UTC",
                        DateTimeOffset.UtcNow.AddDays(1).ToString("yyyy-MM-dd"),
                        "09:00",
                        2,
                        task.EmployeeId,
                        "Draft three ideas from approved facts.",
                        5
                    )
                    {
                        Skill = "marketing-starter-plan",
                    }
                ),
            }
        );
        Assert.Equal(ModelVendorPolicy.VultrRoute, business.Job.Route);
        Assert.Equal(ModelVendorPolicy.VultrRoute, memory.Job.Route);
        Assert.Equal(ModelVendorPolicy.VultrRoute, schedule.Authority.Route);
        var goal = store.GoalTerms("marketing", "marketing-starter-plan", null);
        foreach (
            var policy in new[] { business.Job.Policy, memory.Job.Policy, schedule.Authority.Policy!, goal.Policy }
        )
        {
            Assert.True(CompanyMeasuredPolicy.Known(policy));
            Assert.Equal(24000, policy.OutputTarget);
            Assert.Equal(360, policy.LocalDeadlineSeconds);
            Assert.Equal(60000, policy.PlanningTokens);
        }
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("employee.hire") with
                {
                    DepartmentId = MeasuredQueueChecks.Items<CompanyDepartment>(store, "departments").First().Id,
                    RoleId = "developer",
                    Name = "Fictional developer",
                    Responsibility = "Review fictional code",
                }
            )
        );
        Assert.Empty(fake.Requests);
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return _root.DisposeAsync();
    }
}
