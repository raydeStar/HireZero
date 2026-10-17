using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using HireZero.Integrations.OpenClaw;
using HireZero.Integrations.Codex;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

public static class RecoveryChecks
{
    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var root = Path.Combine(Path.GetTempPath(), "recovery-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
        File.WriteAllText(Path.Combine(root, "wwwroot", "index.html"), "Original conversation fixture");
        var path = Path.Combine(root, "workspace.sqlite");
        const string owner = "local-owner@workspace.invalid";
        var requests = new RequestStore(path); var agents = new AgentStore(path); var reviews = new GitHubReviewStore(path);
        var old = requests.CreateRequest(owner, "owner/example", "Old", "Old context stays private", Guid.NewGuid().ToString());
        var historical = requests.AddMessage(old.Id, owner, "Old context stays private", "old-message"); requests.ArchiveRequest(old.Id);
        reviews.Add(new ReviewRepository("repo", 1, 10, "owner/example", "", "fictional"));
        var github = new SyntheticGitHub(); var model = new FixtureModel();
        var previous = Environment.GetEnvironmentVariable(RecoveryWorkspace.Variable);
        try
        {
            Environment.SetEnvironmentVariable(RecoveryWorkspace.Variable, "1");
            await using var app = RecoveryWorkspace.TryCreate(["--contentRoot=" + root], owner, true, root, github, model, "http://127.0.0.1:0")!;
            Check(app.Services.GetService<ICodingRunner>() is null && app.Services.GetService<OpenClawCells>() is null, "Recovery has no coding or legacy model runtime.");
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) }; client.DefaultRequestHeaders.Host = "127.0.0.1:5064";
            Check((await client.GetStringAsync("/")).Contains("Original conversation fixture"), "Original UI assets are served.");
            var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
            Check(session.GetProperty("chatOnly").GetBoolean() && session.GetProperty("mutationsHeld").GetBoolean() && !session.GetProperty("executionHeld").GetBoolean(), "Chat UI is available with mutations held.");
            using (var rejected = await client.PostAsJsonAsync("/api/requests/" + old.Id + "/agent-reply", new { retry = true })) Check(rejected.StatusCode == HttpStatusCode.Conflict && model.Calls == 0, "Old consent does not replay history.");
            foreach (var route in new[] { "/api/github/workflows/old/publish", "/api/requests/old/allow-agent", "/api/intake", "/api/agent/shift/start" })
                using (var refused = await client.PostAsync(route, null)) Check(refused.StatusCode == HttpStatusCode.ServiceUnavailable, "Mutation routes remain held.");
            var home = requests.Home(owner);
            using (var posted = await client.PostAsJsonAsync("/api/requests/" + home.Id + "/messages", new { body = "Reply exactly: ready", clientRequestId = Guid.NewGuid().ToString() }))
                Check(posted.StatusCode == HttpStatusCode.Created, "Fresh owner message is admitted.");
            await model.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(model.Calls == 1 && model.Prompt == "Reply exactly: ready", "Only fresh text reaches model.");
            async Task WaitState(string state)
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (agents.Latest(home.Id)?.State != state) await Task.Delay(10, limit.Token);
            }
            await WaitState("done"); model.FailNext = true;
            using (var posted = await client.PostAsJsonAsync("/api/requests/" + home.Id + "/messages", new { body = "Fresh failure fixture", clientRequestId = Guid.NewGuid().ToString() }))
                Check(posted.StatusCode == HttpStatusCode.Created, "Fresh failure fixture is admitted.");
            await WaitState("failed");
            using (var acknowledge = await client.PostAsJsonAsync("/api/requests/" + home.Id + "/agent-reply", new { retry = false }))
                Check(acknowledge.StatusCode == HttpStatusCode.Accepted && agents.Latest(home.Id)?.State == "failed" && model.Calls == 2, "Following an already-failed reply does not retry it.");
            using (var retry = await client.PostAsJsonAsync("/api/requests/" + home.Id + "/agent-reply", new { retry = true }))
                Check(retry.StatusCode == HttpStatusCode.Accepted, "Only an explicit retry re-admits a failed fresh reply.");
            await WaitState("done"); Check(model.Calls == 3 && model.Prompt == "Fresh failure fixture", "Explicit retry remains fresh-message-only.");
            Check(TextOnlyOpenClaw.FailureMessage(HttpStatusCode.Forbidden).Contains("HTTP 403") && TextOnlyOpenClaw.FailureMessage(HttpStatusCode.Forbidden).Contains("has not been verified"), "Gateway refusal does not falsely diagnose expired OAuth.");
            var replies = app.Services.GetRequiredService<FreshOwnerReplies>();
            try { replies.Admit(old, historical); throw new Exception("Archived history admitted"); } catch (WorkspaceException) { checks++; }
            var now = DateTimeOffset.UtcNow.AddMinutes(20);
            var recurring = new GitHubDiscovery(path, github, reviews) { Clock = () => now };
            await recurring.ScanAsync(CancellationToken.None); var first = recurring.Read();
            Check(!await recurring.TickAsync(now.AddMinutes(1), CancellationToken.None), "Scan waits for 15-minute interval.");
            now = now.AddMinutes(16); Check(await recurring.TickAsync(now, CancellationToken.None), "Due recurring scan runs.");
            Check(recurring.Read().Findings.Length == 1 && recurring.Read().ChangedAt == first.ChangedAt && github.Writes == 0 && github.LogReads == 0, "Repeated discovery deduplicates without writes or log reads.");
            Check(requests.FindRequest(old.Id)!.Status == "archived", "Historical work stays archived.");
            using var config = JsonDocument.Parse(JsonSerializer.Serialize(TextOnlyOpenClaw.Configuration("fictional", "existing-auth-in-place")));
            Check(config.RootElement.GetProperty("tools").GetProperty("deny")[0].GetString() == "*" && !config.RootElement.GetProperty("cron").GetProperty("enabled").GetBoolean() && !config.RootElement.GetProperty("hooks").GetProperty("enabled").GetBoolean(), "Text gateway disables tools, cron and hooks.");
            await app.StopAsync();
        }
        finally { Environment.SetEnvironmentVariable(RecoveryWorkspace.Variable, previous); }
        Console.WriteLine("PASS " + checks + " original UI, fresh chat, historical refusal, held mutations and recurring discovery checks");
    }

    private sealed class FixtureModel : IModelGateway
    {
        public string DefaultModel => "fictional-model";
        public string Reasoning => "high";
        public int Calls; public string? Prompt; public bool FailNext;
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Prepare(string cell) { }
        public void Stop(string cell) { }
        public Task<string> CompleteAsync(string cell, string instructions, string prompt, CancellationToken token, string? model = null, int maxTokens = 900)
        { Calls++; Prompt = prompt; Called.TrySetResult(); if (FailNext) { FailNext = false; return Task.FromException<string>(new ModelUnavailableException("Synthetic gateway refusal")); } return Task.FromResult("ready"); }
    }
}
