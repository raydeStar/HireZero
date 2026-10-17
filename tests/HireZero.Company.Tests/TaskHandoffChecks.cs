using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;
using HireZero.Roles.Developer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

static class TaskHandoffChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var data = Path.Combine(root, "task-handoff-http");
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            "owner@example.invalid",
            true,
            data,
            fixtureUrl: "http://127.0.0.1:0"
        )!;
        await app.StartAsync();
        var store = app.Services.GetRequiredService<CompanyStore>();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(
                app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()
            ),
        };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        CompanyCommand C(string action) => MeasuredQueueChecks.C(action);
        async Task<string> Post(CompanyCommand command)
        {
            using var response = await client.PostAsJsonAsync("/api/company/commands", command);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception("Handoff fixture command refused: " + body);
            return body;
        }
        async Task<T> Apply<T>(CompanyCommand command) => CompanyJson.Decode<T>(await Post(command));
        async Task Denied(CompanyCommand command)
        {
            using var response = await client.PostAsJsonAsync("/api/company/commands", command);
            check(!response.IsSuccessStatusCode, "Invalid handoff command is refused: " + command.Action);
        }
        T[] Items<T>(string name) => MeasuredQueueChecks.Items<T>(store, name);
        async Task<(CompanyEmployee Employee, CompanyThread Thread)> Hire(string name, string role)
        {
            var department = await Apply<CompanyDepartment>(
                C("department.create") with
                {
                    Name = name + " department",
                }
            );
            var employee = await Apply<CompanyEmployee>(
                C("employee.hire") with
                {
                    DepartmentId = department.Id,
                    RoleId = role,
                    Name = name,
                    Responsibility = "Fictional internal work only",
                }
            );
            var fields = store
                .Roles.Single(r => r.Id == role)
                .OnboardingFields.ToDictionary(k => k, k => "Fictional approved " + k);
            var knowledge = await Apply<CompanyKnowledge>(
                C("knowledge.save") with
                {
                    DepartmentId = department.Id,
                    Body = CompanyJson.Encode(fields),
                }
            );
            await Post(
                C("knowledge.review") with
                {
                    Id = knowledge.Id,
                    ExpectedVersion = knowledge.Version,
                    Digest = knowledge.Digest,
                    Decision = "approve",
                }
            );
            return (
                Items<CompanyEmployee>("employees").Single(e => e.Id == employee.Id),
                Items<CompanyThread>("threads").Single(t => t.EmployeeId == employee.Id)
            );
        }
        var marketing = await Hire("Marketing fixture", "marketing");
        var developer = await Hire("Developer fixture", "developer");
        var sibling = await Hire("Sibling fixture", "developer");
        await Post(C("notes.save") with { EmployeeId = marketing.Employee.Id, Body = "SOURCE_PRIVATE_NOTES_SENTINEL" });
        await Post(
            C("message.send") with
            {
                EmployeeId = marketing.Employee.Id,
                ThreadId = marketing.Thread.Id,
                Body = "SOURCE_PRIVATE_CHAT_SENTINEL",
            }
        );
        await Post(C("notes.save") with { EmployeeId = developer.Employee.Id, Body = "RECIPIENT_OWN_NOTES" });
        await Post(C("notes.save") with { EmployeeId = sibling.Employee.Id, Body = "SIBLING_PRIVATE_NOTES_SENTINEL" });
        var sourceTask = await Apply<CompanyTask>(
            C("task.create") with
            {
                EmployeeId = marketing.Employee.Id,
                ThreadId = marketing.Thread.Id,
                Skill = "chat",
                Body = "Review fictional paper-crane copy",
            }
        );
        var artifact = await Apply<CompanyArtifact>(
            C("artifact.create") with
            {
                Id = sourceTask.Id,
                ExpectedVersion = store.Work(sourceTask.Id).Version,
                Body = "APPROVED_PAPER_CRANE_COPY: paper and a printed folding guide",
            }
        );
        CompanyCommand Assign(CompanyArtifact a) =>
            C("handoff.assign") with
            {
                Id = a.Id,
                ExpectedVersion = a.Version,
                Digest = a.Digest,
                EmployeeId = developer.Employee.Id,
                ThreadId = developer.Thread.Id,
                Skill = "developer-work",
                Body =
                    "Prepare an unpublished page using this approved copy. Acceptance: retain only the two approved kit contents. No publication.",
            };
        await Denied(Assign(artifact));
        artifact = await Apply<CompanyArtifact>(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        await Denied(Assign(artifact) with { Digest = "wrong" });
        await Denied(Assign(artifact) with { ExpectedVersion = artifact.Version - 1 });
        await Denied(Assign(artifact) with { ThreadId = sibling.Thread.Id });
        await Denied(Assign(artifact) with { Skill = "nonexistent" });
        await Denied(Assign(artifact) with { EmployeeId = marketing.Employee.Id, ThreadId = marketing.Thread.Id });
        await Denied(Assign(artifact) with { Evidence = [artifact.Id] });
        check(
            Items<CompanyHandoff>("handoffs").Length == 0
                && Items<CompanyTask>("tasks").All(t => t.EmployeeId == marketing.Employee.Id),
            "Refused handoffs roll back task, budget and sharing records."
        );
        var command = Assign(artifact);
        var receipt = await Post(command);
        check(receipt == await Post(command), "Lost-response retries return the same atomic handoff/task receipt.");
        await Denied(command with { Body = "Changed task under an old operation ID" });
        var json = JsonSerializer.Deserialize<JsonElement>(receipt, CompanyJson.Options);
        var handoff = CompanyJson.Decode<CompanyHandoff>(json.GetProperty("handoff").GetRawText());
        var task = CompanyJson.Decode<CompanyTask>(json.GetProperty("task").GetRawText());
        check(
            task.State == "blocked"
                && task.Reason == "developer-execution-unavailable"
                && task.Resource is null
                && Items<CompanyExecutionGrant>("executions").Length == 0
                && handoff.TargetTaskId == task.Id
                && handoff.ArtifactVersion == artifact.Version,
            "The real owner entry assigns cross-department Developer work without checkout, model, write or execution authority."
        );
        check(
            Items<CompanyHandoff>("handoffs").Length == 1
                && Items<CompanyTask>("tasks").Count(t => t.EmployeeId == developer.Employee.Id) == 1,
            "One approved handoff creates exactly one assigned task."
        );
        check(
            await client.GetStringAsync("/api/company/operations/" + command.RequestId) == receipt,
            "The complete operation is readable for reconciliation without reposting."
        );
        var unrelated = await Apply<CompanyTask>(
            C("task.create") with
            {
                EmployeeId = developer.Employee.Id,
                ThreadId = developer.Thread.Id,
                Skill = "developer-work",
                Body = "Unrelated fictional work",
            }
        );
        var other = store.Claim(developer.Employee.Id, true, unrelated.Id, execution: true)!;
        var otherContext = store.Capture(other.Id);
        check(
            otherContext.Evidence.Length == 0,
            "A task-scoped handoff does not enter unrelated work in the same recipient conversation."
        );
        store.FailAttempt(other.Id, "fictional-check-complete", false);

        // Continue this same HTTP-created task through the existing isolated fake coding bridge.
        var checkout = Path.Combine(root, "handoff-isolated-checkout");
        Directory.CreateDirectory(checkout);
        var git = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Git",
            "cmd",
            "git.exe"
        );
        async Task Git(params string[] arguments)
        {
            var start = new ProcessStartInfo(git)
            {
                WorkingDirectory = checkout,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (
                var argument in new[]
                {
                    "-c",
                    "user.name=Fictional Fixture",
                    "-c",
                    "user.email=fixture@example.invalid",
                    "-c",
                    "commit.gpgsign=false",
                    "-c",
                    "core.hooksPath=" + Path.Combine(root, "empty-hooks"),
                }.Concat(arguments)
            )
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await output;
            if (process.ExitCode != 0)
                throw new Exception("Disposable fixture Git failed: " + await error);
            await error;
        }
        await Git("init");
        await File.WriteAllTextAsync(Path.Combine(checkout, "source.txt"), "Fictional starting content");
        await Git("add", "source.txt");
        await Git("commit", "-m", "Fictional isolated baseline");
        await Post(
            C("workspace.grant") with
            {
                Id = task.Id,
                ExpectedVersion = store.Work(task.Id).Version,
                Body = checkout,
            }
        );
        var fake = MeasuredQueueChecks.Fake;
        var factory = new CompanyCodingFactory(
            Path.Combine(root, "handoff-coding.sqlite"),
            fake,
            Path.Combine(root, "handoff-runtime"),
            CompanyCodingFactory.Identity(fake)
        );
        var bridge = new CompanyDeveloperBridge(store, new CompanyWorkspaceInspector(git), factory);
        var grant = await bridge.PrepareAsync(task.Id, checkout, true);
        var context = store.ExecutionContext(grant.Id);
        var packed = CompanyJson.Encode(context);
        check(
            context.Evidence.Single().Body == artifact.Body
                && context.Notes == "RECIPIENT_OWN_NOTES"
                && !packed.Contains("SOURCE_PRIVATE_")
                && !packed.Contains("SIBLING_PRIVATE_"),
            "The exact Developer execution context receives the approved artifact and its own notes, never the sender's or sibling's private context."
        );
        grant = await Apply<CompanyExecutionGrant>(
            C("execution.review") with
            {
                Id = grant.Id,
                ExpectedVersion = grant.Version,
                Digest = grant.Digest,
                Decision = "approve",
            }
        );
        await bridge.ExecuteFixtureAsync(grant.Id);
        check(
            store.Work(task.Id).State == "awaiting_review"
                && store.Execution(grant.Id).Review?.Files.Single().Path == "fixture-change.txt",
            "The assigned handoff reaches a durable diff and owner review using only the compiled fake runner in the disposable checkout."
        );
        var outputArtifact = Items<CompanyArtifact>("artifacts").Single(a => a.TaskId == task.Id);
        outputArtifact = await Apply<CompanyArtifact>(
            C("artifact.review") with
            {
                Id = outputArtifact.Id,
                ExpectedVersion = outputArtifact.Version,
                Digest = outputArtifact.Digest,
                Decision = "approve",
            }
        );
        await Post(C("task.complete") with { Id = task.Id, ExpectedVersion = store.Work(task.Id).Version });
        check(
            store.Work(task.Id).State == "completed" && outputArtifact.State == "approved",
            "The cross-department handoff completes only after exact owner review of the generated internal result."
        );
        handoff = await Apply<CompanyHandoff>(
            C("handoff.revoke") with
            {
                Id = handoff.Id,
                ExpectedVersion = handoff.Version,
            }
        );
        check(
            handoff.State == "revoked" && store.Work(task.Id).State == "completed",
            "Revoking sharing preserves completed historical work and its receipt."
        );
        await Denied(
            Assign(outputArtifact) with
            {
                EmployeeId = marketing.Employee.Id,
                ThreadId = marketing.Thread.Id,
                Skill = "chat",
            }
        );

        var secondJson = JsonSerializer.Deserialize<JsonElement>(await Post(Assign(artifact)), CompanyJson.Options);
        var second = CompanyJson.Decode<CompanyHandoff>(secondJson.GetProperty("handoff").GetRawText());
        await Denied(
            C("task.retry") with
            {
                Id = second.TargetTaskId,
                ExpectedVersion = store.Work(second.TargetTaskId!).Version,
            }
        );
        var pending = store.Claim(developer.Employee.Id, true, second.TargetTaskId, execution: true)!;
        store.Capture(pending.Id);
        await Post(C("handoff.revoke") with { Id = second.Id, ExpectedVersion = second.Version });
        check(
            store.Work(second.TargetTaskId!).State == "canceled"
                && store.Attempt(pending.Id).Phase == "not_sent"
                && !store.IsCurrent(pending.Id)
                && store.Work(unrelated.Id).State == "blocked",
            "Revocation cancels/fences only the exact recipient task and does not alter unrelated work."
        );
        var staleJson = JsonSerializer.Deserialize<JsonElement>(await Post(Assign(artifact)), CompanyJson.Options);
        var stale = CompanyJson.Decode<CompanyHandoff>(staleJson.GetProperty("handoff").GetRawText());
        artifact = await Apply<CompanyArtifact>(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "changes",
            }
        );
        artifact = await Apply<CompanyArtifact>(
            C("artifact.review") with
            {
                Id = artifact.Id,
                ExpectedVersion = artifact.Version,
                Digest = artifact.Digest,
                Decision = "approve",
            }
        );
        var staleAttempt = store.Claim(developer.Employee.Id, true, stale.TargetTaskId, execution: true)!;
        try
        {
            store.Capture(staleAttempt.Id);
            check(false, "Changed source approval must invalidate the handoff.");
        }
        catch (WorkspaceException)
        {
            check(
                true,
                "A changed approval version cannot silently authorize an older recipient task, even when the artifact body is unchanged."
            );
        }
        store.FailAttempt(staleAttempt.Id, "stale-handoff", false);
        var heldJson = JsonSerializer.Deserialize<JsonElement>(
            await Post(
                Assign(artifact) with
                {
                    EmployeeId = sibling.Employee.Id,
                    ThreadId = sibling.Thread.Id,
                    Skill = "chat",
                }
            ),
            CompanyJson.Options
        );
        var heldTask = CompanyJson.Decode<CompanyTask>(heldJson.GetProperty("task").GetRawText());
        var strictFake = new FakeModel();
        check(
            heldTask.State == "blocked"
                && heldTask.Reason == "handoff-awaiting-approval"
                && !await new CompanyScheduler(store, strictFake).TickAsync(sibling.Employee.Id)
                && strictFake.Calls.Count == 0,
            "Even a qualified strict provider cannot automatically dispatch newly assigned handoff work without separate admission."
        );
        var reopened = new CompanyStore(
            Path.Combine(data, "workspace.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions()
        );
        reopened.Recover();
        check(
            MeasuredQueueChecks.Items<CompanyHandoff>(reopened, "handoffs").Single(h => h.Id == second.Id).State
                == "revoked"
                && reopened.Work(second.TargetTaskId!).State == "canceled",
            "Task-scoped sharing and revocation persist across restart without resumption."
        );
        check(
            Items<CompanyBudget>("budgets").All(b => b.Reserved == 0),
            "Rejected, revoked and stale handoffs leave no token reservations; only the separately approved fake coding run consumes fixture usage."
        );
        await app.StopAsync();
    }
}
