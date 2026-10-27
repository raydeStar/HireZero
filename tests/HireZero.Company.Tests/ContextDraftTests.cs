using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Tools.GitHub;
using HireZero.Tools.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// Department context drafted from sources (docs/adr/0041-context-drafted-from-sources.md): an onboarding employee may
// run exactly one owner-authorized context draft, under its own named policy, whose answer is only a suggestion.
// Sources and the model are fictional: the compiled fake answers.
public sealed class ContextDraftTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("context-draft-");

    public async ValueTask DisposeAsync() => await _root.DisposeAsync();

    private static CompanyCommand C(string action) => MeasuredQueueChecks.C(action);

    private sealed record Prepared(CompanyTask Task, CompanyAttempt Attempt);

    [Fact]
    public async Task AnOnboardingEmployeeDraftsItsContextOnlyThroughOneAuthorizedTurn()
    {
        await using var f = await MeasuredQueueChecks.Fixture.Create(
            _root.Path,
            "context-draft",
            "company-measured-success"
        );
        var department = CompanyJson.Decode<CompanyDepartment>(
            await f.Post(C("department.create") with { Name = "Fictional Studio" })
        );
        var hired = CompanyJson.Decode<CompanyEmployee>(
            await f.Post(
                C("employee.hire") with
                {
                    DepartmentId = department.Id,
                    RoleId = "marketing",
                    Name = "Fictional marketer",
                    Responsibility = "Fictional campaigns",
                }
            )
        );
        Assert.Equal("onboarding", hired.State);

        // Ordinary work stays refused while onboarding.
        await f.Refused(
            C("conversation.prepare") with
            {
                EmployeeId = hired.Id,
                Scope = "employee",
                Body = "Hello",
                CreditAllowance = 1,
            }
        );

        var sources =
            "Source 1: https://example.invalid/ (Fictional Studio)\nWe make fictional paper crane kits for adult hobbyists.";
        var prepared = CompanyJson.Decode<CompanyAttempt>(
            await f.Post(C("context.draft") with { EmployeeId = hired.Id, Body = sources, CreditAllowance = 1 })
        );
        var task = f.Store.Work(prepared.TaskId);
        Assert.Equal((CompanyStore.ContextDraftKind, "running"), (task.Kind, task.State));
        Assert.Equal(CompanyMeasuredPolicy.ContextDraft, prepared.Measured!.Policy);
        Assert.Equal(0, f.Starts);

        var call = f.Store.TextCall(prepared.Id);
        Assert.Contains("paper crane kits", call.Context.Input);
        Assert.Empty(call.Context.History);
        Assert.Contains("\"purpose\":", call.Instructions);
        Assert.Contains("untrusted third-party text", call.Instructions);

        await f.Post(
            C("measured.run") with
            {
                Id = prepared.Id,
                ExpectedVersion = prepared.Version,
                Digest = prepared.Measured.Digest,
                Decision = "accept-soft-limits",
            }
        );
        Assert.Equal("completed", f.Store.Work(task.Id).State);

        // The answer is a suggestion in the private conversation; no knowledge was saved or approved.
        var state = JsonSerializer.SerializeToElement(f.Store.Dashboard(), CompanyJson.Options);
        Assert.DoesNotContain(
            state.GetProperty("knowledge").EnumerateArray(),
            k => k.GetProperty("departmentId").GetString() == department.Id
        );
        Assert.Equal(
            "onboarding",
            state
                .GetProperty("employees")
                .EnumerateArray()
                .Single(e => e.GetProperty("id").GetString() == hired.Id)
                .GetProperty("state")
                .GetString()
        );
        Assert.Contains(f.Store.Feed(0, 200), e => e.Type == "context.drafted" && e.Id == task.Id);
        // The dashboard dates the task from its first and latest events (UX round 8).
        var times = state.GetProperty("taskTimes").GetProperty(task.Id);
        Assert.True(
            times.GetProperty("updated").GetDateTimeOffset() >= times.GetProperty("started").GetDateTimeOffset()
        );

        // The company-wide facts are drafted the same way, under the fixed company fields instead of the role's.
        var company = CompanyJson.Decode<CompanyAttempt>(
            await f.Post(
                C("context.draft") with
                {
                    EmployeeId = hired.Id,
                    Decision = CompanyStore.CompanyScope,
                    Body = sources,
                    CreditAllowance = 1,
                }
            )
        );
        Assert.Equal(CompanyStore.CompanyScope, f.Store.Work(company.TaskId).ContextScope);
        var instructions = f.Store.TextCall(company.Id).Instructions;
        Assert.Contains("company-wide facts", instructions);
        Assert.Contains("\"boundaries\":", instructions);
        Assert.DoesNotContain("\"purpose\":", instructions);
        await f.Refused(
            C("context.draft") with
            {
                EmployeeId = hired.Id,
                Decision = "everything",
                Body = sources,
                CreditAllowance = 1,
            }
        );
    }

    // The route reads each named source through the audited tools, reports the ones it could not read, and prepares
    // one draft without any model call. The page and the repository are fictional fakes.
    [Fact]
    public async Task SourcesAreReadThroughToolsAndBecomeOnePreparedDraft()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(_root.Path, "sources");
        Directory.CreateDirectory(root);
        var model = MeasuredQueueChecks.Model(root, "company-measured-success");
        await using var app = CompanyWorkspace.TryCreate(
            [CompanyWorkspace.Flag],
            "owner@example.invalid",
            true,
            Path.Combine(root, "data"),
            fixture: model,
            fixtureUrl: "http://127.0.0.1:0",
            fixtureTools: registry =>
            {
                registry.Register(
                    WebTools.Read,
                    (request, _) =>
                        request.Url.StartsWith("https://", StringComparison.Ordinal)
                            ? Task.FromResult(
                                new WebPage(
                                    request.Url,
                                    "Fictional Studio",
                                    "We make fictional paper crane kits.",
                                    false
                                )
                            )
                            : throw new ToolFailureException("refused", "Use a public https address.", 422)
                );
                registry.Register(
                    GitHubCommands.Read,
                    (command, _) =>
                        Task.FromResult(
                            command.Args[1].EndsWith("/readme", StringComparison.Ordinal)
                                ? "{\"content\":\""
                                    + Convert.ToBase64String(Encoding.UTF8.GetBytes("# Fictional kit\nOpen source."))
                                    + "\"}"
                                : "{\"description\":\"Fictional crane kits\",\"topics\":[\"paper\"]}"
                        )
                );
            }
        )!;
        await app.StartAsync(token);
        var store = app.Services.GetRequiredService<CompanyStore>();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new(
                app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()
            ),
        };
        client.DefaultRequestHeaders.Host = "127.0.0.1:5065";
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5065");
        client.DefaultRequestHeaders.Add("X-Dev-Request", "1");
        async Task<T> Command<T>(CompanyCommand command)
        {
            using var r = await client.PostAsJsonAsync("/api/company/commands", command, token);
            return CompanyJson.Decode<T>(await r.Content.ReadAsStringAsync(token));
        }
        var department = await Command<CompanyDepartment>(C("department.create") with { Name = "Fictional Studio" });
        var hired = await Command<CompanyEmployee>(
            C("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "marketing",
                Name = "Fictional marketer",
                Responsibility = "Fictional campaigns",
            }
        );

        using var response = await client.PostAsJsonAsync(
            "/api/company/context-sources",
            new
            {
                employeeId = hired.Id,
                sources = new[] { "fictional/crane-kit", "example.invalid", "ftp://example.invalid/x" },
                creditAllowance = 1,
            },
            token
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)).RootElement;
        var sources = result.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal([true, true], sources.Take(2).Select(s => s.GetProperty("read").GetBoolean()));
        Assert.False(sources[2].GetProperty("read").GetBoolean());
        var attempt = CompanyJson.Decode<CompanyAttempt>(result.GetProperty("attempt").GetRawText());
        Assert.Equal(CompanyMeasuredPolicy.ContextDraft, attempt.Measured!.Policy);
        var input = store.TextCall(attempt.Id).Context.Input;
        Assert.Contains("GitHub repository fictional/crane-kit", input);
        Assert.Contains("Open source.", input);
        Assert.Contains("We make fictional paper crane kits.", input);
        Assert.Contains(store.ToolCalls(50), c => c.ToolId == WebTools.Read.Id);
        await app.StopAsync(token);
    }

    [Fact]
    public void RepositoriesAreRecognisedAndPagesAreNot()
    {
        Assert.Equal("fictional-owner/crane-kit", CompanyContextSources.Repository("fictional-owner/crane-kit"));
        Assert.Equal("owner/repo", CompanyContextSources.Repository("https://github.com/owner/repo/tree/main/docs"));
        Assert.Equal("owner/repo", CompanyContextSources.Repository("https://github.com/owner/repo.git"));
        Assert.Null(CompanyContextSources.Repository("https://example.com/blog/post"));
        Assert.Null(CompanyContextSources.Repository("example.com"));
    }

    [Fact]
    public void OnlyNamedBoundedPoliciesAreKnown()
    {
        Assert.True(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Current));
        Assert.True(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.ContextDraft));
        Assert.True(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Conversation));
        Assert.True(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Deliverable));
        Assert.False(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Deliverable with { OutputTarget = 100000 }));
        Assert.Equal(
            CompanyMeasuredPolicy.Deliverable,
            CompanyMeasuredPolicy.For("work", CompanyMeasuredPolicy.DeliverableProfile)
        );
        Assert.Equal(
            CompanyMeasuredPolicy.Conversation,
            CompanyMeasuredPolicy.For("conversation", CompanyMeasuredPolicy.DeliverableProfile)
        );
        Assert.False(
            CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.ContextDraft with { ApplicationBytes = 1 << 20 })
        );
        Assert.False(
            CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Conversation with { ApplicationBytes = 1 << 20 })
        );
        // A reply has room for approved facts and history (docs/adr/0047); other measured work keeps 4 KiB.
        Assert.Equal(CompanyMeasuredPolicy.Conversation, CompanyMeasuredPolicy.For("conversation"));
        Assert.Equal(16 * 1024, CompanyMeasuredPolicy.Conversation.ApplicationBytes);
        Assert.Equal(CompanyMeasuredPolicy.Current, CompanyMeasuredPolicy.For("work"));
        Assert.Equal(CompanyMeasuredPolicy.ContextDraft, CompanyMeasuredPolicy.For(CompanyStore.ContextDraftKind));
    }
}
