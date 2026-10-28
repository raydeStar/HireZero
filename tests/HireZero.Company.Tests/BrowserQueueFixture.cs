using System.Net;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Web;

// Test executable only. The provider is unconditionally the compiled fake stdio runtime.
static class BrowserQueueFixture
{
    // Explicit synthetic research mode cannot touch the network, even if the UI submits another address.
    private sealed class BusinessPages : HttpMessageHandler
    {
        private int _pageReads;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.Host != "paper.example.invalid")
                throw new ToolFailureException("refused", "The fictional fixture has no such website.", 422);
            var robots = request.RequestUri.AbsolutePath == "/robots.txt";
            var content = robots
                ? "User-agent: *\nDisallow: /blocked"
                : "<title>Fictional Paper Studio</title><nav><a href='/services'>Services</a><a href='/about'>About</a><a href='https://other.example.invalid/about'>Other business</a></nav><main><h1>Paper craft kits for adult hobbyists</h1><p>Each kit includes paper and a printed folding guide.</p><p>We offer simple craft workshops in Fictional Valley.</p></main>";
            if (
                !robots
                && Interlocked.Increment(ref _pageReads) > 1
                && Environment.GetEnvironmentVariable("HIREZERO_BROWSER_SOURCE_FRESHNESS") == "1"
            )
                content =
                    "<title>Fictional Paper Studio</title><main><p>We now offer studio workshops only.</p><p>We offer simple craft workshops in Fictional Valley.</p></main>";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8, robots ? "text/plain" : "text/html"),
                }
            );
        }
    }

    private sealed class GoogleConnection : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var json = request.RequestUri!.Host switch
            {
                "oauth2.googleapis.com" =>
                    "{\"access_token\":\"fictional-access\",\"refresh_token\":\"fictional-refresh\",\"expires_in\":3600}",
                "analyticsadmin.googleapis.com" =>
                    "{\"accountSummaries\":[{\"propertySummaries\":[{\"property\":\"properties/123456\",\"displayName\":\"Fictional Paper Studio\"}]}]}",
                "searchconsole.googleapis.com" =>
                    "{\"siteEntry\":[{\"siteUrl\":\"sc-domain:paper.example.invalid\",\"permissionLevel\":\"siteOwner\"}]}",
                _ => throw new InvalidOperationException("The fictional connection fixture has no such endpoint."),
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    public static async Task RunAsync(string[] args)
    {
        if (args.Length is < 2 or > 4)
            throw new ArgumentException("Select one disposable browser test root.");
        var root = Path.GetFullPath(args[1]);
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (
            Path.GetDirectoryName(root) != parent
            || !Path.GetFileName(root).StartsWith("hirezero-manual-ui-", StringComparison.Ordinal)
        )
            throw new ArgumentException("Browser fixture requires its disposable temporary root.");
        Environment.SetEnvironmentVariable(ExecutionHold.Variable, "0");
        var mode = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_SYNTHETIC_MODE") ?? "company-measured-success";
        if (mode is not ("company-measured-success" or "company-conversation-browser"))
            throw new ArgumentException("Choose a compiled browser fixture mode.");
        var businessMode = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_SYNTHETIC_BUSINESS_DRAFT");
        var businessDraft = businessMode is "1" or "success-then-unknown";
        var scheduleMode = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_SCHEDULE_CLOCK") == "1";
        var memoryMode = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_MEMORY") == "1";
        var memoryAnalysis =
            Environment.GetEnvironmentVariable("HIREZERO_BROWSER_MEMORY_ANALYSIS") == "1"
                ? new MemoryAnalysisModel()
                : null;
        var wordImportMode = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_WORD_IMPORT") == "1";
        var drive =
            Environment.GetEnvironmentVariable("HIREZERO_BROWSER_DRIVE_IMPORT") == "1"
                ? new DriveDocumentFixture()
                : null;
        var companyKeys =
            Environment.GetEnvironmentVariable("HIREZERO_BROWSER_COMPANY_KEY") == "1"
                ? new CompanyModelFixture()
                : null;
        var decisions =
            Environment.GetEnvironmentVariable("HIREZERO_BROWSER_DECISIONS") == "1"
                ? new CompanyDecisionFixture()
                : null;
        var usageMode = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_USAGE") == "1";
        var premiumMode = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_PREMIUM") == "1";
        var billing =
            premiumMode ? new BillingFixtureProvider { SelectedPlan = BillingFixtureProvider.ExtendedPlan }
            : Environment.GetEnvironmentVariable("HIREZERO_BROWSER_BILLING") == "1" ? new BillingFixtureProvider()
            : null;
        var sandbox =
            Environment.GetEnvironmentVariable("HIREZERO_BROWSER_SANDBOX") == "1"
                ? new SandboxFixtureExecutor { ReleaseFailures = 1, ExtendedEnabled = premiumMode }
                : null;
        var notices =
            Environment.GetEnvironmentVariable("HIREZERO_BROWSER_NOTIFICATIONS") == "1" ? new NoticeSender() : null;
        var publicBrowser =
            Environment.GetEnvironmentVariable("HIREZERO_BROWSER_PUBLIC_REVIEW") == "1"
                ? new BrowserFixtureExecutor { ReleaseFailures = 1 }
                : null;
        var clockTicks = DateTimeOffset.UtcNow.UtcTicks;
        IModelProvider? model =
            decisions is not null ? new UsageModel()
            : memoryAnalysis is not null ? memoryAnalysis
            : Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEAM") == "1" ? new TeamModel()
            : Environment.GetEnvironmentVariable("HIREZERO_BROWSER_PLANS") == "1" ? new PlanModel()
            : usageMode ? new UsageModel()
            : scheduleMode ? new ScheduleModel()
            : businessDraft ? new BusinessModel(businessMode == "success-then-unknown")
            : args[0] == "--company-browser-fixture" ? null
            : MeasuredQueueChecks.Model(root, mode);
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            "owner@example.invalid",
            true,
            Path.Combine(root, "company"),
            fixture: model,
            fixtureUrl: args.Length >= 3 ? args[2] : "http://127.0.0.1:5065",
            fixtureWebRoot: args.Length == 4 ? args[3] : null,
            fixtureNotificationSender: notices,
            fixtureSandbox: sandbox,
            fixtureBrowser: publicBrowser,
            fixtureBilling: billing?.Runtime(),
            fixtureCompanyModels: decisions is not null ? CompanyDecisionFixture.Models
                : companyKeys is null ? null
                : CompanyModelFixture.Models,
            fixtureCompanyModelHandler: companyKeys is null ? null : _ => companyKeys,
            fixtureCompanyDecisionHandler: decisions is null ? null : _ => decisions,
            fixtureGoogle: drive is not null ? drive
                : Environment.GetEnvironmentVariable("HIREZERO_BROWSER_CONNECTIONS") == "1" ? new GoogleConnection()
                : null,
            fixtureClock: scheduleMode || memoryMode || usageMode
                ? () => new DateTimeOffset(Interlocked.Read(ref clockTicks), TimeSpan.Zero)
                : null,
            fixtureTools: Environment.GetEnvironmentVariable("HIREZERO_BROWSER_SYNTHETIC_BUSINESS") == "1"
                ? tools =>
                {
                    new WebReader(new BusinessPages()).Register(tools);
                    new BusinessSearch(() => "fictional-search-key", new SearchPages()).Register(tools);
                }
                : null
        )!;
        if (decisions is not null)
        {
            ((CompanyModelConnection)app.Services.GetRequiredService<IModelProvider>()).Save(
                new(
                    0,
                    "openrouter",
                    CompanyDecisionFixture.Model,
                    "fictional-browser-decision-key",
                    "use-host-and-company-key"
                )
            );
            app.MapGet("/__fixture/decisions", () => Results.Ok(new { calls = decisions.Requests.Count }));
            app.MapPost(
                "/__fixture/decision-mode",
                (System.Text.Json.JsonElement body) =>
                {
                    decisions.Unknown = body.GetProperty("unknown").GetBoolean();
                    return Results.Ok();
                }
            );
        }
        if (Environment.GetEnvironmentVariable("HIREZERO_BROWSER_CONNECTION_MEMBERS") == "1")
            ConnectionMemberFixture.Map(app);
        if (memoryAnalysis is not null)
        {
            app.MapGet("/__fixture/memory-analysis", () => Results.Ok(new { memoryAnalysis.Calls }));
        }
        if (drive is not null)
        {
            var driveTask = MeasuredQueueChecks.Seed(app.Services.GetRequiredService<CompanyStore>());
            app.MapGet(
                "/__fixture/drive-document",
                () =>
                    Results.Ok(
                        new
                        {
                            fileId = DriveDocumentFixture.FileId,
                            key = drive.Key,
                            requests = drive.Requests.Count,
                            driveTask.EmployeeId,
                        }
                    )
            );
            app.MapPost(
                "/__fixture/drive-mode",
                (System.Text.Json.JsonElement body) =>
                {
                    if (body.TryGetProperty("changed", out var changed))
                        drive.ChangeVersion = changed.GetBoolean();
                    if (body.TryGetProperty("version", out var sourceVersion))
                        drive.Version = sourceVersion.GetString()!;
                    if (body.TryGetProperty("text", out var sourceText))
                        drive.Content = System.Text.Encoding.UTF8.GetBytes(sourceText.GetString()!);
                    return Results.Ok(new { changed = drive.ChangeVersion });
                }
            );
        }
        if (companyKeys is not null)
            app.MapGet(
                "/api/fixture/company-model",
                () =>
                    Results.Ok(
                        new
                        {
                            calls = companyKeys.Requests.Count,
                            providers = companyKeys.Requests.Select(r => r.Provider).ToArray(),
                            models = companyKeys.RequestedModels.ToArray(),
                        }
                    )
            );
        if (billing is not null)
        {
            app.MapPost(
                "/api/fixture/billing",
                (System.Text.Json.JsonElement body) =>
                {
                    var action = body.GetProperty("action").GetString();
                    if (action == "lose-next-checkout")
                        billing.LoseNextCheckoutReply = true;
                    else if (action == "lose-next-portal")
                        billing.LoseNextPortalReply = true;
                    else if (action == "reads-fail")
                        billing.RefuseReads = true;
                    else if (action == "reads-resume")
                        billing.RefuseReads = false;
                    else if (action is "paid" or "past_due" or "canceled")
                    {
                        var operation = body.GetProperty("operation").GetString()!;
                        billing.Change(operation, action);
                        var envelope = billing.Event(
                            operation,
                            action == "paid" ? "checkout.session.completed" : "customer.subscription.updated"
                        );
                        return Results.Ok(new { body = envelope.Body, signature = envelope.Signature });
                    }
                    return Results.Ok(
                        new
                        {
                            billing.Creates,
                            billing.Portals,
                            billing.Reads,
                        }
                    );
                }
            );
        }
        if (sandbox is not null)
        {
            var sandboxStore = app.Services.GetRequiredService<CompanyStore>();
            var task = MeasuredQueueChecks.Seed(sandboxStore);
            T SandboxSeed<T>(CompanyCommand command) => CompanyJson.Decode<T>(sandboxStore.Apply(command));
            var sandboxEngineering = SandboxSeed<CompanyDepartment>(
                MeasuredQueueChecks.C("department.create") with
                {
                    Name = "Fictional report engineering",
                }
            );
            var recipient = SandboxSeed<CompanyEmployee>(
                MeasuredQueueChecks.C("employee.hire") with
                {
                    DepartmentId = sandboxEngineering.Id,
                    RoleId = "developer",
                    Name = "Script report reviewer",
                    Responsibility = "Prepare unpublished technical proposals from reviewed reports",
                }
            );
            var recipientKnowledge = SandboxSeed<CompanyKnowledge>(
                MeasuredQueueChecks.C("knowledge.save") with
                {
                    DepartmentId = sandboxEngineering.Id,
                    Body = CompanyJson.Encode(
                        CompanyWorkspace
                            .Definitions()
                            .Single(r => r.Id == "developer")
                            .OnboardingFields.ToDictionary(k => k, k => "Fictional approved " + k)
                    ),
                }
            );
            sandboxStore.Apply(
                MeasuredQueueChecks.C("knowledge.review") with
                {
                    Id = recipientKnowledge.Id,
                    ExpectedVersion = recipientKnowledge.Version,
                    Digest = recipientKnowledge.Digest,
                    Decision = "approve",
                }
            );
            var document = CompanyJson.Decode<CompanyDocument>(
                sandboxStore.Apply(
                    MeasuredQueueChecks.C("document.save") with
                    {
                        ExpectedVersion = 0,
                        Body = CompanyJson.Encode(
                            new CompanyDocumentInput(
                                "Fictional campaign rows",
                                "campaign,leads\nPaper,3",
                                SourceName: "campaign.txt"
                            )
                        ),
                    }
                )
            );
            sandboxStore.Apply(
                MeasuredQueueChecks.C("document.review") with
                {
                    Id = document.Id,
                    ExpectedVersion = document.Version,
                    Digest = document.Digest,
                    Decision = "approve",
                }
            );
            app.MapGet(
                "/__fixture/sandbox",
                () =>
                    Results.Ok(
                        new
                        {
                            taskId = task.Id,
                            documentId = document.Id,
                            recipientId = recipient.Id,
                            executions = sandbox.Executions,
                            inputs = sandbox.LastRequest?.Inputs,
                        }
                    )
            );
            app.MapPost(
                "/__fixture/sandbox/{mode}",
                (string mode) =>
                {
                    if (mode is "disable" or "enable")
                        sandbox.ExecutionEnabled = mode == "enable";
                    else if (mode == "unknown-pending")
                    {
                        sandbox.ReleaseFailures = 1;
                        sandbox.Mode = "unknown";
                    }
                    else
                        sandbox.Mode = mode;
                    return Results.Ok();
                }
            );
        }
        if (publicBrowser is not null)
        {
            var browserStore = app.Services.GetRequiredService<CompanyStore>();
            var browserTask = MeasuredQueueChecks.Seed(browserStore);
            app.MapGet(
                "/__fixture/browser",
                () =>
                    Results.Ok(
                        new
                        {
                            taskId = browserTask.Id,
                            executions = publicBrowser.Executions,
                            request = publicBrowser.LastRequest,
                        }
                    )
            );
            app.MapPost(
                "/__fixture/browser/{mode}",
                (string mode) =>
                {
                    if (mode is "disable" or "enable")
                        publicBrowser.ExecutionEnabled = mode == "enable";
                    else
                        publicBrowser.Mode = mode;
                    return Results.Ok();
                }
            );
        }
        if (companyKeys is not null)
            MeasuredQueueChecks.Seed(app.Services.GetRequiredService<CompanyStore>());
        if (usageMode)
        {
            MeasuredQueueChecks.Seed(app.Services.GetRequiredService<CompanyStore>());
            app.MapPost(
                "/__fixture/usage/month",
                () =>
                {
                    var now = new DateTimeOffset(Interlocked.Read(ref clockTicks), TimeSpan.Zero);
                    Interlocked.Exchange(
                        ref clockTicks,
                        new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).UtcTicks
                    );
                    return Microsoft.AspNetCore.Http.Results.Ok(new { advanced = true });
                }
            );
        }
        if (wordImportMode)
        {
            var wordStore = app.Services.GetRequiredService<CompanyStore>();
            var wordTask = MeasuredQueueChecks.Seed(wordStore);
            app.MapGet(
                "/__fixture/word-document",
                () =>
                    Results.Ok(
                        new
                        {
                            contentBase64 = Convert.ToBase64String(WordDocumentFixture.BrandGuide()),
                            pdfContentBase64 = Convert.ToBase64String(PdfDocumentFixture.Read("brand-guide")),
                            pdfScannedBase64 = Convert.ToBase64String(PdfDocumentFixture.Read("scanned")),
                            pdfMixedBase64 = Convert.ToBase64String(PdfDocumentFixture.Read("mixed-pages")),
                            invalidBase64 = Convert.ToBase64String(
                                WordDocumentFixture.Create(
                                    "<w:ins><w:p><w:r><w:t>Unreviewed text</w:t></w:r></w:p></w:ins>"
                                )
                            ),
                            wordTask.EmployeeId,
                        }
                    )
            );
        }
        if (memoryMode)
        {
            var memoryStore = app.Services.GetRequiredService<CompanyStore>();
            var task = MeasuredQueueChecks.Seed(memoryStore);
            var artifact = CompanyJson.Decode<CompanyArtifact>(
                memoryStore.Apply(
                    MeasuredQueueChecks.C("artifact.create") with
                    {
                        Id = task.Id,
                        ExpectedVersion = task.Version,
                        Body = "Cobalt campaign used patient paper demonstrations.",
                    }
                )
            );
            memoryStore.Apply(
                MeasuredQueueChecks.C("artifact.review") with
                {
                    Id = artifact.Id,
                    ExpectedVersion = artifact.Version,
                    Digest = artifact.Digest,
                    Decision = "approve",
                }
            );
            app.MapPost(
                "/__fixture/memory/expire",
                () =>
                {
                    Interlocked.Add(ref clockTicks, TimeSpan.FromDays(2).Ticks);
                    return Microsoft.AspNetCore.Http.Results.Ok(new { reviewed = memoryStore.MaintainMemory() });
                }
            );
        }
        if (notices is not null)
        {
            var noticeStore = app.Services.GetRequiredService<CompanyStore>();
            noticeStore.SignIn("owner@example.invalid", "owner@example.invalid", true, "Fictional Owner");
            var task = MeasuredQueueChecks.Seed(noticeStore);
            app.MapPost(
                "/__fixture/notifications/event",
                () =>
                {
                    var current = noticeStore.Work(task.Id);
                    noticeStore.Apply(
                        MeasuredQueueChecks.C("artifact.create") with
                        {
                            Id = current.Id,
                            ExpectedVersion = current.Version,
                            Body = "Fictional private result for the notification journey.",
                        }
                    );
                    return Microsoft.AspNetCore.Http.Results.Ok(new { created = true });
                }
            );
            app.MapGet(
                "/__fixture/notifications/sent",
                () => Microsoft.AspNetCore.Http.Results.Ok(notices.Sent.ToArray())
            );
            app.MapPost(
                "/__fixture/notifications/orchestration",
                () =>
                {
                    var seed = OrchestrationNoticeFixture.Seed(noticeStore);
                    var completed = OrchestrationNoticeFixture.CompletePlan(noticeStore, seed.Plan);
                    var proposed = OrchestrationNoticeFixture.A<CompanyPlan>(
                        noticeStore,
                        OrchestrationNoticeFixture.C("plan.create") with
                        {
                            EmployeeId = seed.Plan.CeoId,
                            Body = "Fictional next campaign proposal",
                            Tasks = seed.Plan.Tasks,
                            Budget = seed.Plan.Budget,
                        }
                    );
                    var meeting = OrchestrationNoticeFixture.FinishDiscussion(noticeStore, seed.Meeting);
                    return Microsoft.AspNetCore.Http.Results.Ok(
                        new
                        {
                            planId = completed.Id,
                            proposalId = proposed.Id,
                            meetingId = meeting.Id,
                        }
                    );
                }
            );
        }
        if (scheduleMode)
            app.MapPost(
                "/__fixture/schedules/{id}/due",
                (string id) =>
                {
                    var saved = app.Services.GetRequiredService<CompanyStore>().ScheduleDetail(id).Schedule;
                    Interlocked.Exchange(
                        ref clockTicks,
                        saved.NextRunAt?.UtcTicks ?? throw new WorkspaceException("No fictional slot remains.")
                    );
                    return Microsoft.AspNetCore.Http.Results.Ok(new { advanced = true });
                }
            );
        if (
            model is null
            || model is TeamModel
            || businessDraft
            || scheduleMode
            || notices is not null
            || memoryMode
            || memoryAnalysis is not null
            || usageMode
        )
        {
            Console.WriteLine(
                businessDraft
                    ? "BROWSER FIXTURE: fictional business model only; the butler has unplugged the outside world."
                    : "BROWSER FIXTURE: unavailable provider; zero live inference or credentials."
            );
            await app.RunAsync();
            return;
        }
        var store = app.Services.GetRequiredService<CompanyStore>();
        T Apply<T>(CompanyCommand c) => CompanyJson.Decode<T>(store.Apply(c));
        var department = Apply<CompanyDepartment>(
            MeasuredQueueChecks.C("department.create") with
            {
                Name = "Fictional browser studio",
            }
        );
        Apply<CompanyEmployee>(
            MeasuredQueueChecks.C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "marketing",
                Name = "Browser draft fixture",
                Responsibility = "Prepare internal copy from approved fictional facts",
            }
        );
        var fields = new
        {
            purpose = "Fictional paper crane kit",
            audience = "Adult hobbyists",
            positioning = "Paper craft kit",
            voice = "Clear and warm",
            claims = "Contains paper and a printed folding guide",
            constraints = "No prices, proof, performance claims, publishing or outreach",
        };
        var knowledge = Apply<CompanyKnowledge>(
            MeasuredQueueChecks.C("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(fields),
            }
        );
        store.Apply(
            MeasuredQueueChecks.C("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var engineering = Apply<CompanyDepartment>(
            MeasuredQueueChecks.C("department.create") with
            {
                Name = "Fictional browser engineering",
            }
        );
        Apply<CompanyEmployee>(
            MeasuredQueueChecks.C("employee.hire") with
            {
                DepartmentId = engineering.Id,
                RoleId = "developer",
                Name = "Browser Developer fixture",
                Responsibility = "Prepare isolated implementation proposals from owner-approved shared facts",
            }
        );
        var engineeringFields = CompanyWorkspace
            .Definitions()
            .Single(r => r.Id == "developer")
            .OnboardingFields.ToDictionary(k => k, k => "Fictional approved " + k);
        var engineeringKnowledge = Apply<CompanyKnowledge>(
            MeasuredQueueChecks.C("knowledge.save") with
            {
                DepartmentId = engineering.Id,
                Body = CompanyJson.Encode(engineeringFields),
            }
        );
        store.Apply(
            MeasuredQueueChecks.C("knowledge.review") with
            {
                Id = engineeringKnowledge.Id,
                ExpectedVersion = engineeringKnowledge.Version,
                Digest = engineeringKnowledge.Digest,
                Decision = "approve",
            }
        );
        Console.WriteLine("BROWSER FIXTURE: fake provider only; zero live inference or credentials.");
        await app.RunAsync();
    }

    private sealed class NoticeSender : INotificationSender
    {
        public string Identity => "fictional-notification-browser-v1";
        public System.Collections.Concurrent.ConcurrentQueue<NotificationEnvelope> Sent { get; } = new();

        public Task SendAsync(NotificationEnvelope envelope, CancellationToken token)
        {
            Sent.Enqueue(envelope);
            return Sent.Count == 2
                ? Task.FromException(new IOException("Fictional uncertain mail receipt"))
                : Task.CompletedTask;
        }
    }

    private sealed class SearchPages : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.Host != "api.search.brave.com")
                throw new InvalidOperationException("The fictional search valet refuses a detour.");
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"web":{"results":[{"url":"https://paper.example.invalid/","title":"Fictional Paper Studio","description":"Paper kits in Fictional Valley"},{"url":"https://another.example.invalid/","title":"Another Fictional Paper Studio","description":"A different business in Fictional Harbor"}]}}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    private sealed class TeamModel : IModelProvider, IMeasuredModelProvider
    {
        public bool Qualified => false;
        public string Status => "Fictional first-team provider";
        public string RuntimeIdentity => "fictional-team-browser-v1";

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            if (
                job.Policy != CompanyMeasuredPolicy.Deliverable
                || job.State != "dispatched"
                || !call.Context.CompanyKnowledge.Contains("Fictional team studio")
                || call.Context.Evidence.Length != 0
            )
                throw new ModelTransportException(
                    "The fictional team requires its approved brief; the butler has withheld the wrong papers.",
                    0,
                    usage: new(0, 0)
                );
            var result = call.Context.RoleId switch
            {
                "developer"
                    when call.Context.Input.Contains("technical starter plan")
                        && call.Instructions.Contains("not browsed") =>
                    "Fictional technical plan: the approved goal concerns website accessibility. Hypothesis: clearer form labels may help. Unknown: actual website markup. First task: obtain approved page evidence and define a keyboard check. No website or repository was accessed.",
                "ceo"
                    when call.Context.Input.Contains("priority plan")
                        && call.Instructions.Contains("not a delegation JSON") =>
                    "Fictional priority plan: first confirm the most valuable team workflow, then propose a bounded task. Success measure: an owner-reviewed deliverable. No employee has been assigned or work started.",
                _ => throw new ModelTransportException(
                    "The fictional team has no such assignment.",
                    0,
                    usage: new(0, 0)
                ),
            };
            return Task.FromResult(
                new ModelReply(result, 100, "fictional-team-" + call.DispatchId, new(70, 30, BilledUsd: 0.001m))
            );
        }
    }

    private sealed class PlanModel : IModelProvider, IMeasuredModelProvider
    {
        public bool Qualified => false;
        public string Status => "Fictional delegation provider";
        public string RuntimeIdentity => "fictional-plan-browser-v1";

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            try
            {
                return PlanReply(call, job);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(
                    "Fictional delegation fixture failed; the butler requests the missing page: " + error
                );
                throw;
            }
        }

        private static Task<ModelReply> PlanReply(ModelCall call, CompanyMeasuredJob job)
        {
            if (
                job.Policy != CompanyMeasuredPolicy.Deliverable
                || !call.Context.CompanyKnowledge.Contains("Fictional shared launch")
            )
                throw new WorkspaceException(
                    "The fictional plan requires its exact shared company context and deliverable policy."
                );
            string body;
            if (call.Context.RoleId == "ceo")
            {
                if (
                    CompanyJson.Encode(call.Context).Contains("Private marketing note")
                    || CompanyJson.Encode(call.Context).Contains("Private developer note")
                )
                    throw new WorkspaceException("The coordinator must not receive other employees' private notes.");
                var marketer = call.Context.Colleagues!.Single(c =>
                    c.RoleId == "marketing" && c.State == "active" && c.Name == "Planning contributor"
                );
                var developer = call.Context.Colleagues!.Single(c =>
                    c.RoleId == "developer" && c.State == "active" && c.Name == "Technical reviewer"
                );
                body = CompanyJson.Encode(
                    new CompanyPlanProposal(
                        "Fictional launch coordination",
                        [
                            new(
                                marketer.Id,
                                marketer.ThreadId!,
                                "chat",
                                "Draft a campaign premise. Acceptance: a modest hypothesis and success measure.",
                                [],
                                []
                            ),
                            new(
                                developer.Id,
                                developer.ThreadId!,
                                "chat",
                                "Review the proposed campaign for website implications. Acceptance: cite the approved premise and identify unknowns.",
                                [],
                                [0]
                            ),
                        ],
                        44000
                    )
                );
            }
            else if (call.Context.RoleId == "marketing")
                body =
                    "Fictional campaign premise: demonstrate the paper kit; record questions as a success measure. This is a hypothesis, not a promised outcome.";
            else
            {
                if (
                    !call.Context.Evidence.Any(e => e.Body.Contains("Fictional campaign premise"))
                    || CompanyJson.Encode(call.Context).Contains("Private marketing note")
                )
                    throw new WorkspaceException(
                        "The dependent task needs only the approved result, not the other employee's private notes."
                    );
                body =
                    "Fictional website review: the approved campaign premise suggests a demonstration page. Unknown: current website content; no website was accessed.";
            }
            return Task.FromResult(
                new ModelReply(body, 100, "fictional-plan-" + call.DispatchId, new(70, 30, BilledUsd: 0.001m))
            );
        }
    }

    private sealed class UsageModel : IModelProvider, IMeasuredModelProvider
    {
        public bool Qualified => false;
        public string Status => "Fictional usage provider";
        public string RuntimeIdentity => "fictional-usage-browser-v1";

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(
            ModelCall call,
            CompanyMeasuredJob job,
            CancellationToken token
        ) =>
            Task.FromResult(
                new ModelReply(
                    "Fictional internal paper campaign suggestion for owner review.",
                    30,
                    "fictional-usage-" + call.DispatchId,
                    new(20, 10, BilledUsd: 0.0009m)
                )
            );
    }

    private sealed class ScheduleModel : IModelProvider, IMeasuredModelProvider
    {
        public bool Qualified => false;
        public string Status => "Fictional recurring-work provider";
        public string RuntimeIdentity => "fictional-schedule-browser-v1";

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            if (
                job.State != "dispatched"
                || (job.Policy != CompanyMeasuredPolicy.Conversation && job.Policy != CompanyMeasuredPolicy.Deliverable)
                || call.Context.History.Length != 0
            )
                throw new ModelTransportException(
                    "The fictional recurring request has unexpected place settings.",
                    0,
                    usage: new(0, 0)
                );
            return Task.FromResult(
                new ModelReply(
                    "Fictional weekly outline: demonstrate paper folding, introduce the studio process, and collect consenting workshop feedback. These are hypotheses for owner review.",
                    30,
                    "fictional-schedule-browser-receipt",
                    new(20, 10)
                )
            );
        }
    }

    private sealed class MemoryAnalysisModel : IModelProvider, IMeasuredModelProvider
    {
        public int Calls;
        public bool Qualified => false;
        public string Status => "Fictional memory analysis fixture";
        public string RuntimeIdentity => "fictional-memory-analysis-browser-v1";

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            if (job.Policy != CompanyMeasuredPolicy.MemoryAnalysis || call.Context.Scope != "memory-analysis")
                throw new ModelTransportException(
                    "A fictional memory check requested the wrong stationery.",
                    0,
                    usage: new(0, 0)
                );
            if (Interlocked.Increment(ref Calls) == 2)
                throw new HttpRequestException("Fictional unconfirmed provider outcome");
            using var input = System.Text.Json.JsonDocument.Parse(call.Context.Input);
            var sources = CompanyJson.Decode<CompanyMemoryItem[]>(
                input.RootElement.GetProperty("records").GetRawText()
            );
            var findings = new[]
            {
                new CompanyMemoryFinding(
                    "possible-conflict",
                    "Verify which workshop duration is current; these two records differ.",
                    sources.Select(s => new CompanyMemoryCitation(s.Id, s.Definition.Statement)).ToArray(),
                    new CompanyMemoryRevision(
                        sources[0].Id,
                        "Confirm whether each workshop lasts sixty or ninety minutes before quoting its duration.",
                        "The reviewed records disagree; verify the applicable format."
                    )
                ),
            };
            return Task.FromResult(
                new ModelReply(
                    CompanyJson.Encode(new { findings }),
                    140,
                    "fictional-memory-receipt",
                    new(100, 40, BilledUsd: .001m)
                )
            );
        }
    }

    private sealed class BusinessModel(bool failAfterFirst) : IModelProvider, IMeasuredModelProvider
    {
        private int _calls;
        public bool Qualified => false;
        public string Status => "Fictional business drafting fixture";
        public string RuntimeIdentity => "fictional-business-browser-v1";

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            if (
                job.State == "dispatched"
                && call.Context.Scope == "employee"
                && job.Policy == CompanyMeasuredPolicy.Deliverable
            )
            {
                if (
                    !call.Context.CompanyKnowledge.Contains(
                        "Paper craft kits for adult hobbyists",
                        StringComparison.Ordinal
                    )
                    || !call.Context.DepartmentKnowledge.Contains("approved company brief", StringComparison.Ordinal)
                    || !call.Context.Input.Contains("one-week marketing starter plan", StringComparison.Ordinal)
                )
                    throw new ModelTransportException(
                        "The first plan is missing its approved place settings.",
                        0,
                        usage: new(0, 0)
                    );
                return Task.FromResult(
                    new ModelReply(
                        "DRAFT FOR INTERNAL REVIEW\nObservations: approved services describe paper craft kits for adult hobbyists.\nHypothesis: a short folding demonstration may help.\nExperiment: show a draft demonstration to three consenting testers and record their questions.\nSample copy: Make time for a paper craft project.\nUnknown: price. Owner question: which kit should lead the launch?",
                        240,
                        "fictional-first-plan",
                        new(160, 80, BilledUsd: 0.002m)
                    )
                );
            }
            if (++_calls > 1 && failAfterFirst)
                throw new HttpRequestException("Fictional unconfirmed provider outcome");
            if (job.State != "dispatched" || call.Context.Scope != "business-profile" || call.Context.EmployeeId != "")
                throw new ModelTransportException("Unexpected fixture request", 0, usage: new(0, 0));
            var fields = CompanyStore
                .BusinessProfileFields.Select(key =>
                    key is "business" or "services"
                        ? new CompanyProfileField(
                            key,
                            "Paper craft kits for adult hobbyists",
                            "sourced",
                            [new(call.Context.Evidence[0].Id, "Paper craft kits for adult hobbyists")]
                        )
                        : new CompanyProfileField(key, "Unknown", "unknown", [])
                )
                .ToArray();
            return Task.FromResult(
                new ModelReply(
                    CompanyJson.Encode(new { fields }),
                    160,
                    "fictional-browser-receipt",
                    new(120, 40, BilledUsd: 0.001m)
                )
            );
        }
    }
}
