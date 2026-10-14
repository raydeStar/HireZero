using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using HireZero.Integrations.Codex;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

public static class DiscoveryChecks
{
    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var root = Path.Combine(Path.GetTempPath(), "discovery-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "workspace.sqlite");
        var requests = new RequestStore(path); var agents = new AgentStore(path); var reviews = new GitHubReviewStore(path); var coding = new CodingRunStore(path);
        var request = requests.CreateRequest("owner@example.invalid", "owner/example", "Fictional queued work", "Keep this history", "fixture");
        requests.AllowAgent(request.Id); requests.AddMessage(request.Id, "owner@example.invalid", "Fictional history", "history");
        agents.EnqueueReply(request.Id, request.Title, "fixture");
        var now = DateTimeOffset.UtcNow.ToString("O");
        coding.Save(new("run", "queued", root, "fictional", "high", null, null, null, [], now, now));
        reviews.Add(new ReviewRepository("repo", 1, 10, "owner/example", "", "fictional"));
        using (var database = new SqliteConnection("Data Source=" + path))
        {
            database.Open(); using var command = database.CreateCommand();
            command.CommandText = "INSERT INTO github_workflow_approvals VALUES('fixture','repair','fictional','2099-01-01T00:00:00Z',NULL)"; command.ExecuteNonQuery();
        }
        var github = new SyntheticGitHub(); var discovery = new GitHubDiscovery(path, github, reviews);
        try { await discovery.ScanAsync(CancellationToken.None); throw new Exception("Nonempty queue accepted"); } catch (ReviewAccessException) { checks++; }
        var reset = ResetQueuedWork.Run(root);
        Check(reset.ArchivedRequests == 1 && reset.CanceledCodingRuns == 1 && reset.ExpiredApprovals == 1, "Reset archives and cancels supported local states.");
        Check(agents.OpenTasks().Length == 0 && coding.Unfinished().Length == 0 && requests.FindRequest(request.Id)!.Status == "archived", "No queued work can be replayed.");
        Check(requests.Messages(request.Id).Length == 1 && !requests.CanUseAgent(requests.FindRequest(request.Id)!, new WorkspaceOwner("owner@example.invalid")), "History remains; owner consent is invalidated.");
        using (var backup = new SqliteConnection("Data Source=" + reset.Backup + ";Mode=ReadOnly"))
        {
            backup.Open(); using var command = backup.CreateCommand(); command.CommandText = "SELECT status FROM requests";
            Check((string?)command.ExecuteScalar() == "queued", "SQLite backup recovers original queue state.");
        }
        var result = await discovery.ScanAsync(CancellationToken.None);
        Check(result.Findings.Length == 1 && result.Findings[0].Repository == "owner/example" && github.Writes == 0 && github.LogReads == 0, "Configured scope produces observations without writes, logs or models.");
        Check(agents.OpenTasks().Length == 0 && requests.Requests(null).Length == 1, "Observation never dispatches or creates executable work.");
        Check(new GitHubDiscovery(path, github, reviews).Read().Findings.Length == 1, "Observation survives restart.");
        coding.Save(new("running", "running", root, "fictional", "high", null, null, null, [], now, now));
        try { ResetQueuedWork.Run(root); throw new Exception("Live run reset accepted"); } catch (InvalidOperationException) { checks++; }
        coding.Save(new("running", "canceled", root, "fictional", "high", null, null, null, [], now, now));
        var previous = Environment.GetEnvironmentVariable(DiscoveryWorkspace.Variable);
        try
        {
            Environment.SetEnvironmentVariable(DiscoveryWorkspace.Variable, "1");
            await using var app = DiscoveryWorkspace.TryCreate(["--urls=http://0.0.0.0:0"], "owner@example.invalid", true, root, github, "http://127.0.0.1:0")!;
            Check(app.Services.GetService<IModelGateway>() is null && app.Services.GetService<ICodingRunner>() is null && app.Services.GetService<IHttpClientFactory>() is null, "Discovery has no execution or other connector providers.");
            Check(!app.Services.GetServices<IHostedService>().Any(service => service is BackgroundService), "Discovery has no recovery or execution workers.");
            await app.StartAsync(); var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) }; client.DefaultRequestHeaders.Host = "127.0.0.1:5064";
            Check((await client.GetStringAsync("/")).Contains("A fresh look"), "Discovery page is usable.");
            var runtime = await client.GetFromJsonAsync<JsonElement>("/api/runtime"); Check(runtime.GetProperty("executionHeld").GetBoolean() && runtime.GetProperty("discoveryOnly").GetBoolean(), "Execution hold remains explicit.");
            using (var scan = await client.PostAsync("/api/discovery/scan", null)) Check(scan.IsSuccessStatusCode, "Only explicit discovery dispatch is accepted.");
            foreach (var target in new[] { "/api/intake", "/api/mcp", "/api/requests/fixture/allow-agent", "/api/github/workflows/fixture/confirm", "/api/github/workflows/fixture/publish", "/api/github/loops/fixture/resume" })
                using (var reply = await client.PostAsync(target, null)) Check(reply.StatusCode == HttpStatusCode.ServiceUnavailable, "Held mutation cannot dispatch.");
            using (var forwarded = new HttpRequestMessage(HttpMethod.Get, "/api/runtime"))
            { forwarded.Headers.Add("X-Forwarded-Host", "fictional.invalid"); using var reply = await client.SendAsync(forwarded); Check(reply.StatusCode == HttpStatusCode.Forbidden, "Forwarded identities cannot enter discovery."); }
            using (var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/discovery/scan"))
            { foreign.Headers.Add("Origin", "https://fictional.invalid"); using var reply = await client.SendAsync(foreign); Check(reply.StatusCode == HttpStatusCode.Forbidden, "Cross-origin observation is refused."); }
            await app.StopAsync();
        }
        finally { Environment.SetEnvironmentVariable(DiscoveryWorkspace.Variable, previous); }
        Console.WriteLine("PASS " + checks + " discovery isolation, fresh queue, backup, consent and observation checks");
    }
}
