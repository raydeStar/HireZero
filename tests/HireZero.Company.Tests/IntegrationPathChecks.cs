using System.Diagnostics;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Integrations.Git;
using HireZero.Integrations.OpenClaw;
using HireZero.Roles.Developer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

static class IntegrationPathChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var mode = "ok";
        var received = new List<JsonElement>();
        var redirectHits = 0;
        long lastReturnedUsage = 0;
        server.MapPost(
            "/v1/chat/completions",
            async (HttpContext c) =>
            {
                received.Add(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
                if (mode == "redirect")
                {
                    c.Response.Redirect("/redirected");
                    return;
                }
                if (mode == "error")
                {
                    c.Response.StatusCode = 503;
                    return;
                }
                if (mode == "lost")
                {
                    c.Abort();
                    return;
                }
                var messages = received.Last().GetProperty("messages");
                var input =
                    mode == "input-overhead"
                        ? 4355
                        : (
                            messages[0].GetProperty("content").GetString()!.Length
                            + messages[1].GetProperty("content").GetString()!.Length
                        ) / 4
                            + 32;
                lastReturnedUsage = input + 5;
                await c.Response.WriteAsJsonAsync(
                    new
                    {
                        id = "fixture-receipt",
                        model = mode == "route" ? "wrong-route" : "fixture-text",
                        usage = mode == "usage"
                            ? null
                            : new
                            {
                                prompt_tokens = input,
                                completion_tokens = 5,
                                total_tokens = input + 5,
                            },
                        choices = new[]
                        {
                            new
                            {
                                finish_reason = mode == "tool" ? "tool_calls" : "stop",
                                message = new
                                {
                                    role = "assistant",
                                    content = "Fictional scoped response",
                                    tool_calls = mode == "tool" ? new[] { new { id = "forbidden-tool" } } : null,
                                },
                            },
                        },
                    }
                );
            }
        );
        server.MapGet(
            "/redirected",
            () =>
            {
                redirectHits++;
                return Results.Ok();
            }
        );
        await server.StartAsync();
        var address = server
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var adapter = new ScopedCompanyText(
            new Uri(address + "/v1/chat/completions"),
            "fixture-text",
            new Counter(),
            syntheticFixture: true
        );
        using var closed = new ScopedCompanyText(
            new Uri(address + "/v1/chat/completions"),
            "fixture-text",
            new Counter()
        );
        var store = new CompanyStore(
            Path.Combine(root, "integration-paths.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions()
        );
        CompanyCommand C(string a) => new(Guid.NewGuid().ToString("D"), a);
        T A<T>(CompanyCommand c) => CompanyJson.Decode<T>(store.Apply(c));
        T[] List<T>(string name) =>
            CompanyJson.Decode<T[]>(
                JsonSerializer.SerializeToElement(store.Dashboard(), CompanyJson.Options).GetProperty(name).GetRawText()
            );
        var dep = A<CompanyDepartment>(C("department.create") with { Name = "Integration fixture" });
        CompanyEmployee Hire(string name) =>
            A<CompanyEmployee>(
                C("employee.hire") with
                {
                    DepartmentId = dep.Id,
                    RoleId = "developer",
                    Name = name,
                    Responsibility = "Isolated integration fixture",
                }
            );
        var first = Hire("First");
        var second = Hire("Second");
        var fields = CompanyWorkspace
            .Definitions()
            .Single(r => r.Id == "developer")
            .OnboardingFields.ToDictionary(k => k, k => "Approved " + k);
        var knowledge = A<CompanyKnowledge>(
            C("knowledge.save") with
            {
                DepartmentId = dep.Id,
                Body = CompanyJson.Encode(fields),
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
        var threads = List<CompanyThread>("threads");
        var t1 = threads.Single(t => t.EmployeeId == first.Id);
        var t2 = threads.Single(t => t.EmployeeId == second.Id);
        store.Apply(C("notes.save") with { EmployeeId = first.Id, Body = "FIRST_EMPLOYEE_ONLY_SENTINEL" });
        CompanyTask Submit(CompanyEmployee e, CompanyThread thread, string skill = "chat") =>
            A<CompanyTask>(
                C("task.create") with
                {
                    EmployeeId = e.Id,
                    ThreadId = thread.Id,
                    Skill = skill,
                    Body = "Fictional scoped integration request",
                }
            );
        var scheduler = new CompanyScheduler(store, adapter);
        var task = Submit(first, t1);
        await scheduler.TickAsync(first.Id);
        Submit(second, t2);
        await scheduler.TickAsync(second.Id);
        check(
            received.Count == 2 && received.All(r => r.GetProperty("messages").GetArrayLength() == 2),
            "Actual HTTP transport sends only system and the admitted scoped context."
        );
        check(
            received[0].GetRawText().Contains("FIRST_EMPLOYEE_ONLY_SENTINEL")
                && !received[1].GetRawText().Contains("FIRST_EMPLOYEE_ONLY_SENTINEL"),
            "Actual wire requests preserve employee memory isolation."
        );
        check(
            received.All(r =>
                r.GetProperty("store").GetBoolean() == false
                && r.GetProperty("tool_choice").GetString() == "none"
                && r.GetProperty("tools").GetArrayLength() == 0
            ),
            "Actual wire requests disable storage and tool selection."
        );
        check(
            received.Select(r => r.GetProperty("user").GetString()).Distinct().Count() == 2,
            "Separate dispatches never reuse a provider correlation key."
        );
        var detail = JsonSerializer.SerializeToElement(store.TaskDetail(task.Id), CompanyJson.Options);
        check(
            detail.GetProperty("attempts")[0].GetProperty("providerReceipt").GetString() == "fixture-receipt",
            "Provider receipt persists with attributed usage."
        );
        var count = received.Count;
        try
        {
            await closed.CompleteAsync(
                new(
                    Guid.NewGuid().ToString("N"),
                    new("x", first.Id, "developer", t1.Id, "r", "", "", "", [], [], "data"),
                    "s",
                    4000,
                    1000
                ),
                default
            );
            check(false, "Live adapter must remain closed.");
        }
        catch (ModelUnavailableException)
        {
            check(received.Count == count, "Unqualified live route performs no HTTP request.");
        }
        var caseIndex = 0;
        foreach (var state in new[] { "tool", "usage", "route", "redirect", "error", "lost" })
        {
            var person = caseIndex++ % 2 == 0 ? first : second;
            var thread = person.Id == first.Id ? t1 : t2;
            mode = state;
            var work = Submit(person, thread);
            await scheduler.TickAsync(person.Id);
            var current = store.Work(work.Id);
            check(
                current.State == (state == "tool" ? "failed" : "unknown"),
                "Transport " + state + " cannot produce an accepted artifact."
            );
            var budget = List<CompanyBudget>("budgets").Single(b => b.Id == work.RootId);
            check(
                state == "tool" ? budget.Used == lastReturnedUsage && budget.Reserved == 0 : budget.Reserved == 5000,
                "Transport " + state + " accounts for confirmed or unknown usage conservatively."
            );
        }
        check(
            redirectHits == 0 && received.Count == 8,
            "Redirects and failures never trigger fallback or automatic HTTP retries."
        );
        var repo = Path.Combine(root, "temporary-checkout");
        Directory.CreateDirectory(repo);
        var git = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Git",
            "cmd",
            "git.exe"
        );
        async Task Git(params string[] args)
        {
            var start = new ProcessStartInfo(git)
            {
                WorkingDirectory = repo,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (
                var arg in new[]
                {
                    "-c",
                    "user.name=Fictional Fixture",
                    "-c",
                    "user.email=fixture@example.invalid",
                    "-c",
                    "commit.gpgsign=false",
                    "-c",
                    "core.hooksPath=" + Path.Combine(root, "empty-hooks"),
                }.Concat(args)
            )
                start.ArgumentList.Add(arg);
            using var p = Process.Start(start)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            await output;
            var text = await error;
            if (p.ExitCode != 0)
                throw new Exception("Fixture Git failed: " + text);
        }
        await Git("init");
        await File.WriteAllTextAsync(Path.Combine(repo, "source.txt"), "Fictional source");
        await Git("add", "source.txt");
        await Git("commit", "-m", "Fictional fixture baseline");
        var inspector = new GitCheckoutInspector(git);
        var fake = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../HireZero.FakeCodex/bin/Debug/net10.0/HireZero.FakeCodex.exe"
            )
        );
        var identity = CompanyCodingFactory.Identity(fake);
        var factory = new CompanyCodingFactory(
            Path.Combine(root, "coding.sqlite"),
            fake,
            Path.Combine(root, "scoped-runtime"),
            identity
        );
        var bridge = new CompanyDeveloperBridge(store, ToolFixtures.Developer(store, inspector), factory);
        async Task<CompanyExecutionGrant> Prepare(CompanyEmployee e, CompanyThread thread)
        {
            // Each proposal starts from the clean commit; earlier runs' and scenarios' changes are discarded first.
            File.Delete(Path.Combine(repo, "fixture-change.txt"));
            await File.WriteAllTextAsync(Path.Combine(repo, "source.txt"), "Fictional source");
            var work = Submit(e, thread, "developer-work");
            store.Apply(C("workspace.grant") with { Id = work.Id, ExpectedVersion = work.Version, Body = repo });
            return await bridge.PrepareAsync(work.Id, repo, true);
        }
        var grant = await Prepare(first, t1);
        check(
            store.Work(grant.TaskId).ModelRoute == "openai/gpt-6.1-sol" && grant.Route == "openai/gpt-6.1-sol",
            "Explicit coding task and reviewed proposal record Sol 6.1."
        );
        check(
            store.ToolCalls().FirstOrDefault(c => c.ToolId == "git.checkout-inspect")
                is { Outcome: "completed", ModuleId: "developer" } call
                && call.InputDigest.Length == 64
                && store.ToolCalls().Any(c => c.ToolId == "git.checkout-review" && c.Outcome == "completed"),
            "Checkout inspection and the clean-baseline review run through the declared Git tools and are audited."
        );
        check(!store.AuthorizeDispatch(grant.AttemptId), "Text dispatch cannot bypass an execution grant.");
        try
        {
            await bridge.ExecuteFixtureAsync(grant.Id);
            check(false, "Execution without owner review must fail.");
        }
        catch (WorkspaceException)
        {
            check(
                !File.Exists(Path.Combine(repo, "fixture-change.txt")),
                "Unapproved execution starts no runtime write."
            );
        }
        grant = A<CompanyExecutionGrant>(
            C("execution.review") with
            {
                Id = grant.Id,
                ExpectedVersion = grant.Version,
                Digest = grant.Digest,
                Decision = "approve",
            }
        );
        var previous = Environment.GetEnvironmentVariable("HIREZERO_PARENT_SENTINEL");
        Environment.SetEnvironmentVariable("HIREZERO_PARENT_SENTINEL", "AMBIENT_PARENT_SENTINEL");
        try
        {
            await bridge.ExecuteFixtureAsync(grant.Id);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HIREZERO_PARENT_SENTINEL", previous);
        }
        check(
            store.Work(grant.TaskId).State == "awaiting_review"
                && File.Exists(Path.Combine(repo, "fixture-change.txt")),
            "Actual stdio runner executes approved fake coding work in the temporary repository."
        );
        check(
            store.Execution(grant.Id).RunId is not null && store.Execution(grant.Id).State == "completed",
            "Coding receipt and single-use outcome persist."
        );
        var savedReview = store.Execution(grant.Id).Review!;
        var changedFile = savedReview.Files.Single();
        var codingDetail = JsonSerializer.SerializeToElement(store.TaskDetail(grant.TaskId), CompanyJson.Options);
        var codingArtifact = List<CompanyArtifact>("artifacts").Single(a => a.TaskId == grant.TaskId);
        check(
            savedReview.BaseRevision == grant.Revision
                && savedReview.Digest == CompanyJson.Digest(savedReview with { Digest = "" })
                && changedFile.Path == "fixture-change.txt"
                && changedFile.Change == "added"
                && changedFile.Patch.Contains("+Fictional scoped change"),
            "Approved fake coding work captures an exact durable text diff against its reviewed revision."
        );
        check(
            codingDetail.GetProperty("executions")[0].GetProperty("review").GetProperty("digest").GetString()
                == savedReview.Digest
                && codingArtifact.Body.Contains(savedReview.Digest)
                && codingArtifact.State == "needs_review",
            "Owner task detail exposes the saved diff, bound to an artifact that still needs review."
        );
        var wire = await File.ReadAllTextAsync(
            Path.Combine(root, "scoped-runtime", grant.AttemptId, "fixture-wire.jsonl")
        );
        check(
            wire.Contains("workspaceWrite")
                && wire.Contains("gpt-6.1-sol")
                && !wire.Contains("AMBIENT_PARENT_SENTINEL"),
            "Actual runtime wire enforces scope and excludes inherited private environment."
        );
        try
        {
            await bridge.ExecuteFixtureAsync(grant.Id);
            check(false, "Execution cannot replay.");
        }
        catch (WorkspaceException)
        {
            check(true, "Completed grant cannot dispatch twice.");
        }
        var stale = await Prepare(second, t2);
        stale = A<CompanyExecutionGrant>(
            C("execution.review") with
            {
                Id = stale.Id,
                ExpectedVersion = stale.Version,
                Digest = stale.Digest,
                Decision = "approve",
            }
        );
        await File.AppendAllTextAsync(Path.Combine(repo, "source.txt"), "Changed after approval");
        try
        {
            await bridge.ExecuteFixtureAsync(stale.Id);
            check(false, "Stale checkout must fail.");
        }
        catch (WorkspaceException)
        {
            check(
                store.Execution(stale.Id).RunId is null,
                "Checkout fingerprint is checked before starting the runtime."
            );
        }
        store.Apply(C("task.cancel") with { Id = stale.TaskId, ExpectedVersion = store.Work(stale.TaskId).Version });
        var unknown = await Prepare(second, t2);
        unknown = A<CompanyExecutionGrant>(
            C("execution.review") with
            {
                Id = unknown.Id,
                ExpectedVersion = unknown.Version,
                Digest = unknown.Digest,
                Decision = "approve",
            }
        );
        var uncertainBridge = new CompanyDeveloperBridge(
            store,
            ToolFixtures.Developer(store, inspector),
            new CompanyCodingFactory(
                Path.Combine(root, "coding.sqlite"),
                fake,
                Path.Combine(root, "scoped-runtime"),
                identity,
                "scoped-missing-usage"
            )
        );
        try
        {
            await uncertainBridge.ExecuteFixtureAsync(unknown.Id);
            check(false, "Missing coding usage must not complete.");
        }
        catch (WorkspaceException)
        {
            check(
                store.Work(unknown.TaskId).State == "unknown" && store.Execution(unknown.Id).State == "unknown",
                "Missing real protocol usage holds task, execution receipt and reservation."
            );
        }
        var collision = await Prepare(first, t1);
        check(
            Throws(() => store.AcquireWorkspace(collision.AttemptId, repo)),
            "Unknown coding outcome retains the checkout claim against other employees."
        );
        store.Apply(
            C("task.cancel") with
            {
                Id = collision.TaskId,
                ExpectedVersion = store.Work(collision.TaskId).Version,
            }
        );
        repo = Path.Combine(root, "configuration-checkout");
        Directory.CreateDirectory(repo);
        await Git("init");
        await File.WriteAllTextAsync(Path.Combine(repo, "source.txt"), "Fictional source");
        await Git("add", "source.txt");
        await Git("commit", "-m", "Fictional clean baseline");
        Directory.CreateDirectory(Path.Combine(repo, ".codex"));
        await File.WriteAllTextAsync(Path.Combine(repo, ".codex", "config.toml"), "# Unreviewed fixture configuration");
        // Committed, so the baseline is clean and the runner itself must refuse the project configuration.
        await Git("add", ".codex/config.toml");
        await Git("commit", "-m", "Fictional project configuration");
        var configured = await Prepare(first, t1);
        configured = A<CompanyExecutionGrant>(
            C("execution.review") with
            {
                Id = configured.Id,
                ExpectedVersion = configured.Version,
                Digest = configured.Digest,
                Decision = "approve",
            }
        );
        try
        {
            await bridge.ExecuteFixtureAsync(configured.Id);
            check(false, "Project runtime configuration must be refused.");
        }
        catch (CodingStartRefusedException)
        {
            check(
                store.Execution(configured.Id).State == "not_sent"
                    && List<CompanyBudget>("budgets").Single(b => b.Id == store.Work(configured.TaskId).RootId).Reserved
                        == 0,
                "Provable pre-start refusal releases its reservation without launching the runtime."
            );
        }
        await Git("rm", "-r", "-q", ".codex");
        await Git("commit", "-m", "Remove the fictional project configuration");
        var waiting = await Prepare(first, t1);
        waiting = A<CompanyExecutionGrant>(
            C("execution.review") with
            {
                Id = waiting.Id,
                ExpectedVersion = waiting.Version,
                Digest = waiting.Digest,
                Decision = "approve",
            }
        );
        var waitingBridge = new CompanyDeveloperBridge(
            store,
            ToolFixtures.Developer(store, inspector),
            new CompanyCodingFactory(
                Path.Combine(root, "coding.sqlite"),
                fake,
                Path.Combine(root, "scoped-runtime"),
                identity,
                "scoped-wait"
            )
        );
        var active = waitingBridge.ExecuteFixtureAsync(waiting.Id);
        var ready = Path.Combine(root, "scoped-runtime", waiting.AttemptId, "fixture-ready");
        for (var i = 0; i < 200 && !File.Exists(ready); i++)
            await Task.Delay(25);
        check(File.Exists(ready), "Actual fake runtime reached its active scoped turn.");
        var currentGrant = store.Execution(waiting.Id);
        store.Apply(
            C("execution.review") with
            {
                Id = waiting.Id,
                ExpectedVersion = currentGrant.Version,
                Decision = "revoke",
            }
        );
        try
        {
            await active;
            check(false, "Revoked runtime must not complete the task.");
        }
        catch (WorkspaceException)
        {
            check(
                store.Work(waiting.TaskId).State == "canceled" && store.Execution(waiting.Id).State == "revoked",
                "Owner revocation survives late runtime outcomes."
            );
        }
        var interruptedWire = await File.ReadAllTextAsync(
            Path.Combine(root, "scoped-runtime", waiting.AttemptId, "fixture-wire.jsonl")
        );
        check(
            interruptedWire.Contains("turn/interrupt"),
            "Revocation interrupts only the owned fake runtime through its actual protocol."
        );
        repo = Path.Combine(root, "diff-only-checkout");
        Directory.CreateDirectory(repo);
        await Git("init");
        await File.WriteAllTextAsync(Path.Combine(repo, "source.txt"), "Original fictional line\n");
        await File.WriteAllTextAsync(Path.Combine(repo, "removed.txt"), "Removed fictional line\n");
        await File.WriteAllTextAsync(Path.Combine(repo, ".gitattributes"), "*.txt filter=fixture diff=fixture\n");
        await Git("add", "source.txt", "removed.txt", ".gitattributes");
        await Git("commit", "-m", "Fictional diff baseline");
        // Configure only after committing the baseline: inspection must never invoke project commands.
        var marker = Path.Combine(repo, "filter-ran");
        await Git("config", "filter.fixture.clean", "echo invoked > filter-ran; cat");
        await Git("config", "diff.fixture.command", "echo invoked > filter-ran");
        var baseline = await inspector.InspectAsync(repo, default);
        await File.WriteAllTextAsync(Path.Combine(repo, "source.txt"), "Changed fictional line\n");
        File.Delete(Path.Combine(repo, "removed.txt"));
        await File.WriteAllTextAsync(Path.Combine(repo, "new.txt"), "Added fictional line\n");
        var diff = await inspector.ReviewAsync(repo, baseline.Revision, default);
        check(
            diff.Files.Length == 3
                && diff.Files.Single(f => f.Path == "source.txt").Patch.Contains("+Changed fictional line")
                && diff.Files.Single(f => f.Path == "removed.txt").Change == "deleted"
                && diff.Files.Single(f => f.Path == "new.txt").Change == "added"
                && !File.Exists(marker),
            "Raw review captures modified, deleted and new files without executing project clean filters or diff drivers."
        );
        await File.WriteAllBytesAsync(Path.Combine(repo, "new.txt"), [0, 1, 2]);
        try
        {
            await inspector.ReviewAsync(repo, baseline.Revision, default);
            check(false, "Binary changes require another review path.");
        }
        catch (WorkspaceException)
        {
            check(!File.Exists(marker), "Unsupported binary review fails closed without invoking project commands.");
        }
        mode = "input-overhead";
        var excess = Submit(first, t1);
        await scheduler.TickAsync(first.Id);
        var excessDetail = JsonSerializer
            .SerializeToElement(store.TaskDetail(excess.Id), CompanyJson.Options)
            .GetProperty("attempts")[0];
        check(
            store.Work(excess.Id).State == "failed"
                && List<CompanyBudget>("budgets").Single(b => b.Id == excess.RootId) is { Used: 4360, Reserved: 0 }
                && excessDetail.GetProperty("usage").GetProperty("inputTokens").GetInt64() == 4355,
            "Actual HTTP input beyond the complete-input bound is withheld while its full known usage and receipt persist."
        );
        await server.StopAsync();
        using (
            var connection = new SqliteConnection(
                "Data Source=" + Path.Combine(root, "integration-paths.sqlite") + ";Pooling=False"
            )
        )
        {
            connection.Open();
            using var q = connection.CreateCommand();
            q.CommandText =
                "DROP TABLE IF EXISTS co_decisions; DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE IF EXISTS co_document_retention; DROP TABLE IF EXISTS co_sandbox_jobs; DROP TABLE IF EXISTS co_compute_policy; DROP TABLE IF EXISTS co_workspace_leases; DROP TABLE IF EXISTS co_usage_entries; DROP TABLE IF EXISTS co_usage_policy; DROP TABLE IF EXISTS co_memory_terms; DROP TABLE IF EXISTS co_memory; DROP TABLE IF EXISTS co_notice_reads; DROP TABLE IF EXISTS co_notice_deliveries; DROP TABLE IF EXISTS co_notice_preferences; DROP TABLE IF EXISTS co_verified_emails; DROP TABLE IF EXISTS co_notices; DROP TABLE IF EXISTS co_schedule_runs; DROP TABLE IF EXISTS co_schedules; DROP TABLE IF EXISTS co_business_fact_reviews; DROP TABLE IF EXISTS co_business_checks; DROP TABLE IF EXISTS co_business_refreshes; DROP TABLE IF EXISTS co_document_terms; DROP TABLE IF EXISTS co_document_chunks; DROP TABLE IF EXISTS co_documents; DROP TABLE IF EXISTS co_business_history; DROP TABLE IF EXISTS co_business_lookups; DROP TABLE IF EXISTS co_business_drafts; DROP TABLE IF EXISTS co_business_profiles; DROP TABLE IF EXISTS co_business_sources; DROP TABLE IF EXISTS co_business; DROP TABLE IF EXISTS co_connections; DROP TABLE IF EXISTS co_audit; ALTER TABLE co_company DROP COLUMN name; DROP TABLE IF EXISTS co_members; DROP TABLE IF EXISTS co_owner_marks; DROP TABLE IF EXISTS co_publications; DROP TABLE co_execution_grants; DROP TABLE IF EXISTS co_tool_calls; DROP TABLE IF EXISTS co_member_invites; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=2";
            q.ExecuteNonQuery();
        }
        var upgraded = new CompanyStore(
            Path.Combine(root, "integration-paths.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions()
        );
        check(
            JsonSerializer
                .SerializeToElement(upgraded.Dashboard(), CompanyJson.Options)
                .GetProperty("employees")
                .GetArrayLength() == 2,
            "Version-one database upgrades transactionally without losing employees."
        );
    }

    static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (WorkspaceException)
        {
            return true;
        }
    }

    sealed class Counter : ICompanyTokenCounter
    {
        public string Identity => "synthetic-counter-v1";
        public int? RuntimeOverheadTokens => 32;

        public int Count(string system, string user) => (system.Length + user.Length) / 4;
    }
}
