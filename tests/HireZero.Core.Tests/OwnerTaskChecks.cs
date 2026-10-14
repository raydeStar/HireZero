using HireZero.Core;

public static class OwnerTaskChecks
{
    sealed class Contributor(OwnerTask task) : IOwnerTaskContributor { public OwnerTask[] Tasks() => [task]; }
    public static async Task RunAsync(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "owner-task-check-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var requests = new RequestStore(path); var agents = new AgentStore(path); var triage = new TriageStore(path);
            var owner = new WorkspaceOwner("owner@example.invalid"); var views = new OwnerTaskStore(path);
            var request = requests.CreateRequest("coworker@example.invalid", "example", "Please fix", "Check the failure", "test");
            var external = new OwnerTask("workflow:1", "Example PR", "GitHub workflow", "https://github.com/owner/example/pull/1", request.Id,
                "publication-proposed", "2026-01-01T00:00:00Z", owner.Login, "normal", ["Owner approval required."], "Review exact diff.", "needs-owner");
            var board = new OwnerTaskBoard(requests, agents, triage, owner, views, [new Contributor(external)]);
            var item = board.Tasks().Single();
            check(item.Id == "request:" + request.Id && item.Provenance!.Length == 2 && item.Status == "publication-proposed", "Linked source records deduplicate while preserving their provenance.");
            views.Save(item.Id, new("resolved", "high"));
            check(board.Tasks().Single() is { LocalState: "resolved", Priority: "high", Status: "publication-proposed" }, "Local resolution changes no source workflow status.");
            check(requests.FindRequest(request.Id)!.Status == "queued" && agents.OpenTasks().Length == 0, "Presentation actions neither complete a request nor launch agent work.");
            check(new OwnerTaskStore(path).Apply(item).LocalState == "resolved", "Local presentation state survives restart.");
            views.Save(item.Id, new("open", "low"));
            check(board.Tasks().Single().LocalState == "open", "Local dismissal/resolution can be restored.");
            try { views.Save(item.Id, new("deploy", "urgent")); check(false, "Invalid local actions must fail."); }
            catch (WorkspaceException) { check(true, "Invalid local actions must fail."); }
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(board); builder.Services.AddSingleton(views);
            await using var app = builder.Build();
            app.Use((context, next) => { context.Items["role"] = context.Request.Headers["Test-Role"].ToString(); return next(context); });
            app.MapOwnerTaskEndpoints(); await app.StartAsync();
            var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features
                .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            foreach (var role in new[] { "visitor", "trusted", "boss", "revoked", "" })
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, "/api/owner-tasks"); get.Headers.Add("Test-Role", role);
                using var read = await client.SendAsync(get);
                check((int)read.StatusCode == 403, "A non-owner cannot read aggregate private task metadata.");
                using var put = new HttpRequestMessage(HttpMethod.Put, "/api/owner-tasks/" + Uri.EscapeDataString(item.Id)); put.Headers.Add("Test-Role", role);
                put.Content = new StringContent("{\"state\":\"resolved\",\"priority\":\"high\"}", System.Text.Encoding.UTF8, "application/json");
                using var write = await client.SendAsync(put);
                check((int)write.StatusCode == 403, "A non-owner cannot change aggregate presentation metadata.");
            }
            await app.StopAsync();
        }
        finally { File.Delete(path); }
    }
}
