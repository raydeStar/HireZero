using System.Net;
using System.Net.Http.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public sealed partial class CompanyTaskCostTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("task-costs-");

    private CompanyStore Store(string company = CompanyStore.DefaultCompanyId) =>
        new(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: "fictional-costs",
            companyId: company
        )
        {
            UsagePrice = _ => null,
        };

    private static CompanyCommand C(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T A<T>(CompanyStore s, CompanyCommand c) => CompanyJson.Decode<T>(s.Apply(c));

    private static CompanyOperatingCost[] Costs(decimal? amount = 0) =>
        CompanyStore.OperatingCostCategories.Select(c => new CompanyOperatingCost(c, amount)).ToArray();

    private static CompanyCommand Save(
        CompanyTask t,
        CompanyOperatingCost[]? costs = null,
        CompanyTaskCostReview? prior = null
    ) =>
        C("cost.save") with
        {
            Id = prior?.Id,
            ExpectedVersion = prior?.Version ?? 0,
            Body = CompanyJson.Encode(
                new CompanyTaskCostInput(
                    t.Id,
                    costs ?? Costs(),
                    "Fictional invoice allocation; amounts verified by owner."
                )
            ),
        };

    private static CompanyCommand Record(CompanyTaskCostReview r, string decision = "record-costs") =>
        C("cost.review") with
        {
            Id = r.Id,
            ExpectedVersion = r.Version,
            Digest = r.Digest,
            Decision = decision,
        };

    private static void Sql(CompanyStore s, string query, params object[] values)
    {
        using var db = new StoreConnection(s.Database.Open(), s.Database);
        db.Execute(query, values);
    }

    private static CompanyTask Completed(CompanyStore s)
    {
        var t = MeasuredQueueChecks.Seed(s);
        var a = A<CompanyAttempt>(
            s,
            C("measured.prepare") with
            {
                Id = t.Id,
                ExpectedVersion = t.Version,
                CreditAllowance = 1,
            }
        );
        s.Apply(
            C("measured.run") with
            {
                Id = a.Id,
                ExpectedVersion = a.Version,
                Digest = a.Measured!.Digest,
                Decision = "accept-soft-limits",
            }
        );
        s.Finish(a.Id, new("Fictional internal work", 30, "fictional-receipt", new(20, 10, BilledUsd: .02m)));
        var artifact = MeasuredQueueChecks.Items<CompanyArtifact>(s, "artifacts").Single(a => a.TaskId == t.Id);
        s.Apply(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        t = s.Work(t.Id);
        s.Apply(C("task.complete") with { Id = t.Id, ExpectedVersion = t.Version });
        return s.Work(t.Id);
    }

    private static CompanyUsageEntry ExtraUsage(
        CompanyStore s,
        CompanyTask t,
        decimal? reported = null,
        decimal? estimated = null,
        string state = "settled",
        bool companyKey = false
    )
    {
        var e = new CompanyUsageEntry(
            Guid.NewGuid().ToString("D"),
            "2026-10",
            "employee-job",
            t.Id,
            t.EmployeeId,
            "fictional-route",
            DateTimeOffset.UtcNow,
            100,
            null,
            1,
            State: state,
            Outcome: "failed",
            ReportedUsd: reported,
            EstimatedUsd: estimated,
            Funding: companyKey ? new("company-key", "fictional", "fixture-revision") : null
        );
        Sql(
            s,
            "INSERT INTO co_usage_entries(id,company_id,state,revision,payload) VALUES($0,$1,$2,1,$3)",
            e.Id,
            s.CompanyId,
            e.State,
            CompanyJson.Encode(e)
        );
        return e;
    }

    [Fact]
    public void MissingOperatingCostsStayUnknownAndExplicitZerosSurviveRestart()
    {
        var s = Store();
        var t = Completed(s);
        Assert.Null(s.TaskCosts(t.Id).OperatingUsd);
        Assert.Null(s.TaskCosts(t.Id).ReconciledTotalUsd);
        var r = A<CompanyTaskCostReview>(s, Save(t, Costs(null)));
        Assert.Equal(422, Assert.Throws<WorkspaceException>(() => s.Apply(Record(r))).Status);
        r = A<CompanyTaskCostReview>(s, Save(t, Costs(), r));
        var command = Record(r);
        var receipt = s.Apply(command);
        Assert.Equal(receipt, s.Apply(command));
        var view = Store().TaskCosts(t.Id);
        Assert.Equal(0m, view.OperatingUsd);
        Assert.Equal(.02m, view.ReconciledTotalUsd);
        Assert.True(view.ReconciliationCurrent);
        Assert.False(view.IncludesModelEstimates);
        Assert.Equal(Owner, Assert.Single(view.Reviews).RecordedBy);
        Assert.True(s.VerifyAudit().Intact);
        Assert.DoesNotContain("invoice allocation", string.Join(" ", s.AuditEntries().Select(a => a.Body)));
    }

    [Fact]
    public void FailedRunsAndCompanyKeyEstimatesAreIncludedOnceWithoutInventingPrices()
    {
        var s = Store();
        var t = Completed(s);
        ExtraUsage(s, t, .03m, .99m);
        ExtraUsage(s, t, estimated: .04m, companyKey: true);
        s.Apply(Record(A<CompanyTaskCostReview>(s, Save(t, Costs(.1m)))));
        var view = s.TaskCosts(t.Id);
        Assert.Equal(3, view.Models.Length);
        Assert.Equal(.05m, view.ReportedModelUsd);
        Assert.Equal(.04m, view.EstimatedModelUsd);
        Assert.Equal(.04m, view.CompanyKeyModelUsd);
        Assert.Equal(.59m, view.ReconciledTotalUsd);
        Assert.True(view.IncludesModelEstimates);
        ExtraUsage(s, t);
        Assert.Null(s.TaskCosts(t.Id).ReconciledTotalUsd);
        s.Apply(Record(A<CompanyTaskCostReview>(s, Save(t, Costs(.1m)))));
        view = s.TaskCosts(t.Id);
        Assert.True(view.ReconciliationCurrent);
        Assert.Equal(1, view.UnpricedModels);
        Assert.Equal(.5m, view.OperatingUsd);
        Assert.Null(view.ReconciledTotalUsd);
    }

    [Fact]
    public void CorrectionsCreateImmutableHistoryAndChangedUsageInvalidatesDraftsAndTotals()
    {
        var s = Store();
        var t = Completed(s);
        var r = A<CompanyTaskCostReview>(s, Save(t, Costs(.1m)));
        Assert.Throws<WorkspaceException>(() => s.Apply(Record(r) with { Digest = "different" }));
        ExtraUsage(s, t, .01m);
        Assert.Contains("changed", Assert.Throws<WorkspaceException>(() => s.Apply(Record(r))).Message);
        r = A<CompanyTaskCostReview>(s, Save(t, Costs(.1m), r));
        r = A<CompanyTaskCostReview>(s, Record(r));
        Assert.Throws<WorkspaceException>(() => s.Apply(Save(t, Costs(.2m), r)));
        var correction = A<CompanyTaskCostReview>(s, Save(t, Costs(.2m)));
        s.Apply(Record(correction));
        var view = s.TaskCosts(t.Id);
        Assert.Equal(2, view.Reviews.Length);
        Assert.Equal(.1m, view.Reviews[1].Costs[0].Usd);
        Assert.Equal(1.03m, view.ReconciledTotalUsd);
        ExtraUsage(s, t, .02m);
        Assert.False(s.TaskCosts(t.Id).ReconciliationCurrent);
        Assert.Null(s.TaskCosts(t.Id).OperatingUsd);
        Assert.Null(s.TaskCosts(t.Id).ReconciledTotalUsd);
    }

    [Fact]
    public void IncompleteTasksAndOutstandingUsageCannotBeRecorded()
    {
        var s = Store();
        var t = MeasuredQueueChecks.Seed(s);
        var r = A<CompanyTaskCostReview>(s, Save(t));
        Assert.Contains("Complete", Assert.Throws<WorkspaceException>(() => s.Apply(Record(r))).Message);
        s.Apply(Record(r, "discard"));
        t = Completed(s);
        ExtraUsage(s, t, .1m, state: "reserved");
        r = A<CompanyTaskCostReview>(s, Save(t));
        Assert.Equal(1, s.TaskCosts(t.Id).HeldModels);
        Assert.Equal(.02m, s.TaskCosts(t.Id).ReportedModelUsd);
        Assert.Contains("outstanding", Assert.Throws<WorkspaceException>(() => s.Apply(Record(r))).Message);
    }

    [Fact]
    public void UnknownNativeAttemptsDoNotBecomeFreeAndUnsentAttemptsDoNotBlockKnownCosts()
    {
        var s = Store();
        var t = Completed(s);
        var original = s.Attempt(s.TaskCosts(t.Id).Models.Single().Id);
        var legacy = original with { Id = "fictional-legacy-native", Measured = null, Phase = "responded" };
        Sql(
            s,
            "INSERT INTO co_attempts(id,company_id,parent_id,employee_id,state,revision,payload) VALUES($0,$1,$2,$3,$4,1,$5)",
            legacy.Id,
            s.CompanyId,
            t.Id,
            t.EmployeeId,
            legacy.Phase,
            CompanyJson.Encode(legacy)
        );
        s.Apply(Record(A<CompanyTaskCostReview>(s, Save(t))));
        Assert.Equal(1, s.TaskCosts(t.Id).UnmeteredAttempts);
        Assert.Null(s.TaskCosts(t.Id).ReconciledTotalUsd);
        legacy = legacy with { Phase = "not_sent" };
        Sql(
            s,
            "UPDATE co_attempts SET state=$0,payload=$1 WHERE company_id=$2 AND id=$3",
            legacy.Phase,
            CompanyJson.Encode(legacy),
            s.CompanyId,
            legacy.Id
        );
        s.Apply(Record(A<CompanyTaskCostReview>(s, Save(t))));
        Assert.Equal(0, s.TaskCosts(t.Id).UnmeteredAttempts);
        Assert.Equal(.02m, s.TaskCosts(t.Id).ReconciledTotalUsd);
    }

    [Fact]
    public void ComputeAllocationIsEvidenceNotAnInvoiceAndUnreleasedResourcesBlockRecording()
    {
        var s = Store();
        var t = Completed(s);
        var now = DateTimeOffset.UtcNow;
        var script = new CompanySandboxJob(
            "fictional-script",
            t.Id,
            t.EmployeeId,
            t.Generation,
            1,
            "Fictional script",
            "fixture-runtime",
            new("fictional-script", s.CompanyId, "console.log(1)", [], [], new(30)),
            [],
            "fixture-digest",
            "completed",
            1,
            now,
            now.AddHours(1),
            StartedAt: now,
            Release: new(true, "removed", "fixture-resource"),
            AllocatedSeconds: 30,
            SettledAt: now,
            Result: new("completed", 0, "private output", "", [], 1500, new(true, "removed", "fixture-resource"))
        );
        Sql(
            s,
            "INSERT INTO co_sandbox_jobs(id,company_id,parent_id,employee_id,state,revision,payload) VALUES($0,$1,$2,$3,$4,1,$5)",
            script.Id,
            s.CompanyId,
            t.Id,
            t.EmployeeId,
            script.State,
            CompanyJson.Encode(script)
        );
        var browser = new CompanyBrowserJob(
            "fictional-browser",
            t.Id,
            t.EmployeeId,
            t.Generation,
            1,
            "Fictional review",
            "fixture-browser",
            new("fictional-browser", s.CompanyId, ["https://paper.example.invalid"], [], new()),
            "fixture-browser-digest",
            "unknown",
            1,
            now,
            now.AddHours(1),
            60,
            StartedAt: now
        );
        Sql(
            s,
            "INSERT INTO co_browser_jobs(id,company_id,parent_id,employee_id,state,revision,payload) VALUES($0,$1,$2,$3,$4,1,$5)",
            browser.Id,
            s.CompanyId,
            t.Id,
            t.EmployeeId,
            browser.State,
            CompanyJson.Encode(browser)
        );
        var view = s.TaskCosts(t.Id);
        Assert.Equal(2, view.Compute.Length);
        Assert.Equal(1500, view.Compute.Single(c => c.Kind == "script").WallMilliseconds);
        Assert.Equal(30, view.Compute.Single(c => c.Kind == "script").AllocatedSeconds);
        Assert.Equal(60, view.Compute.Single(c => c.Kind == "browser").HeldSeconds);
        Assert.Null(view.OperatingUsd);
        Assert.DoesNotContain("private output", CompanyJson.Encode(view));
        var review = A<CompanyTaskCostReview>(s, Save(t, Costs(.1m)));
        Assert.Contains("outstanding", Assert.Throws<WorkspaceException>(() => s.Apply(Record(review))).Message);
        browser = browser with
        {
            State = "failed",
            Release = new(true, "removed", "fixture-browser-resource"),
            SettledAt = now,
            AllocatedSeconds = 60,
        };
        Sql(
            s,
            "UPDATE co_browser_jobs SET state=$0,payload=$1 WHERE company_id=$2 AND id=$3",
            browser.State,
            CompanyJson.Encode(browser),
            s.CompanyId,
            browser.Id
        );
        Assert.Contains("changed", Assert.Throws<WorkspaceException>(() => s.Apply(Record(review))).Message);
        review = A<CompanyTaskCostReview>(s, Save(t, Costs(.1m), review));
        s.Apply(Record(review));
        Assert.Equal(.52m, s.TaskCosts(t.Id).ReconciledTotalUsd);
        Assert.All(s.TaskCosts(t.Id).Compute, c => Assert.Equal(0, c.HeldSeconds));
    }

    [Fact]
    public void InvalidAmountsDuplicateCategoriesAndStaleOrCompetingDraftsAreRejected()
    {
        var s = Store();
        var t = Completed(s);
        foreach (
            var costs in new[]
            {
                Costs(-1),
                Costs(100001),
                Costs(.0000001m),
                Costs()[..4],
                Enumerable.Repeat(new CompanyOperatingCost("compute", 0), 5).ToArray(),
            }
        )
            Assert.Equal(422, Assert.Throws<WorkspaceException>(() => s.Apply(Save(t, costs))).Status);
        var r = A<CompanyTaskCostReview>(s, Save(t));
        Assert.Throws<WorkspaceException>(() => s.Apply(Save(t)));
        Assert.Throws<WorkspaceException>(() => s.Apply(Save(t, Costs(), r) with { ExpectedVersion = 0 }));
        s.Apply(Record(r, "discard"));
        var next = A<CompanyTaskCostReview>(s, Save(t));
        Assert.Equal(2, next.Sequence);
        Assert.NotEqual(r.Id, next.Id);
        var shortBasis = Save(t, prior: next) with
        {
            Body = CompanyJson.Encode(new CompanyTaskCostInput(t.Id, Costs(), "x")),
        };
        next = A<CompanyTaskCostReview>(s, shortBasis);
        Assert.Equal(422, Assert.Throws<WorkspaceException>(() => s.Apply(Record(next))).Status);
    }

    [Fact]
    public void MigrationPreservesChecksumsAndCompanyIsolation()
    {
        var s = Store();
        var t = Completed(s);
        string[] hashes;
        using (var db = new StoreConnection(s.Database.Open(), s.Database))
        {
            hashes = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE co_task_cost_reviews; DELETE FROM module_schema_migrations WHERE owner='company' AND version=28;"
            );
        }
        s = Store();
        using (var db = new StoreConnection(s.Database.Open(), s.Database))
            Assert.Equal(
                hashes,
                db.Rows(
                    "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                    r => r.GetString(0)
                )
            );
        var save = Save(t);
        s.Apply(save);
        Assert.True(s.IsTaskCostOperation(save.RequestId));
        Assert.False(Store("co-neighbor").IsTaskCostOperation(save.RequestId));
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => Store("co-neighbor").TaskCosts(t.Id)).Status);
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => Store("co-neighbor").Apply(Save(t))).Status);
    }

    [Fact]
    public async Task HttpReportsAndRecoveryReceiptsAreOwnerOnlyAndCompanyScoped()
    {
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            Owner,
            true,
            Path.Combine(_root.Path, "host"),
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
        var s = app.Services.GetRequiredService<CompanyStore>();
        var t = MeasuredQueueChecks.Seed(s);
        var save = Save(t);
        using (var response = await client.PostAsJsonAsync("/api/company/commands", save, token))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await client.GetAsync("/api/company/tasks/" + t.Id + "/costs", token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        using (var response = await client.GetAsync("/api/company/progress", token))
        {
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.NotNull((await response.Content.ReadFromJsonAsync<CompanyProgressView>(token))!.TaskCosts);
        }
        using (
            var response = await client.PostAsJsonAsync("/api/companies", new { name = "Fictional neighbor" }, token)
        )
        {
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(token);
            client.DefaultRequestHeaders.Add("X-HireZero-Company", json.GetProperty("id").GetString());
        }
        foreach (
            var route in new[] { "/api/company/tasks/" + t.Id + "/costs", "/api/company/operations/" + save.RequestId }
        )
        {
            using var response = await client.GetAsync(route, token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        client.DefaultRequestHeaders.Remove("X-HireZero-Company");
        s.Apply(C("member.invite") with { Body = "second-owner@example.invalid", Decision = "owner" });
        var second = s.SignIn("fictional-cost-owner", "second-owner@example.invalid", true, "Second owner")!.Subject;
        s.Apply(C("member.role") with { Id = Owner, ExpectedVersion = 1, Decision = "viewer" }, second, out _);
        using (var response = await client.GetAsync("/api/company/progress", token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null((await response.Content.ReadFromJsonAsync<CompanyProgressView>(token))!.TaskCosts);
        }
        foreach (
            var route in new[] { "/api/company/tasks/" + t.Id + "/costs", "/api/company/operations/" + save.RequestId }
        )
        {
            using var response = await client.GetAsync(route, token);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var response = await client.PostAsJsonAsync("/api/company/commands", Save(t), token))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await app.StopAsync(token);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
