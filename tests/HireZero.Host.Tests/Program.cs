using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
var root = Path.Combine(Path.GetTempPath(), "held-workspace-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
File.WriteAllText(Path.Combine(root, "wwwroot", "index.html"), "<html><body>Fictional held fixture</body></html>");
var dataRoot = Path.Combine(root, "private-fixture"); Directory.CreateDirectory(dataRoot);
var database = Path.Combine(dataRoot, "workspace.sqlite");
using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
{
    connection.Open(); using var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE original_work(id TEXT PRIMARY KEY,state TEXT,payload TEXT); INSERT INTO original_work VALUES('queued-fixture','queued','fictional pending work'),('running-fixture','running','fictional interrupted work'),('approval-fixture','expired','fictional unused consent');";
    command.ExecuteNonQuery();
}
File.WriteAllText(Path.Combine(dataRoot, "roles.json"), "{\"hired\":[\"developer\"]}");
Directory.CreateDirectory(Path.Combine(dataRoot, "OpenClaw", "fictional-cell"));
File.WriteAllText(Path.Combine(dataRoot, "OpenClaw", "fictional-cell", "openclaw.json"), "{\"fictionalSentinel\":true}");
Dictionary<string, string> Fingerprint() => Directory.GetFiles(dataRoot, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(dataRoot, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
var before = Fingerprint(); var previous = Environment.GetEnvironmentVariable(HeldWorkspace.Variable);
try
{
    Check(!HeldWorkspace.Requested(null) && !HeldWorkspace.Requested("0") && HeldWorkspace.Requested("1"), "Hold is explicit, with normal startup unchanged by default.");
    try { HeldWorkspace.Requested("yes"); throw new Exception("Invalid hold flag was accepted"); } catch (InvalidOperationException) { checks++; }
    Environment.SetEnvironmentVariable(HeldWorkspace.Variable, "0");
    Check(HeldWorkspace.TryCreate([], "owner@example.invalid", true, dataRoot) is null, "Normal startup does not construct the held root.");
    Environment.SetEnvironmentVariable(HeldWorkspace.Variable, "1");
    try { HeldWorkspace.TryCreate([], "owner@example.invalid", true, dataRoot, "http://0.0.0.0:0", root); throw new Exception("Public fixture binding accepted"); } catch (InvalidOperationException) { checks++; }
    await using var app = HeldWorkspace.TryCreate(["--Kestrel:Endpoints:Unwanted:Url=http://0.0.0.0:0", "--urls=http://0.0.0.0:0"], "owner@example.invalid", true, dataRoot, "http://127.0.0.1:0", root)!;
    // Check the frozen empty loader BEFORE starting, so the hostile fixture config can never expose a port.
    Check(!app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.ConfigurationLoader!.Configuration.GetSection("Endpoints").GetChildren().Any(), "Held endpoint loader discards hostile configured listeners before startup.");
    Check(app.Services.GetService<IModelGateway>() is null && app.Services.GetService<ICodingRunner>() is null && app.Services.GetService<IHttpClientFactory>() is null, "Held root registers no model, coding runner or connector client factory for direct dispatch.");
    Check(app.Services.GetService<RequestStore>() is null && app.Services.GetService<AgentStore>() is null && app.Services.GetService<SessionWatcher>() is null, "Held root cannot instantiate storage recovery or session scanning.");
    Check(!app.Services.GetServices<IHostedService>().Any(service => service is BackgroundService), "No application background worker is registered under the hold.");
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    Check(IPAddress.IsLoopback(IPAddress.Parse(new Uri(address).Host)), "Synthetic runtime listens only on loopback.");
    app.Configuration["Kestrel:Endpoints:Unwanted:Url"]="http://127.0.0.2:0";
    ((IConfigurationRoot)app.Configuration).Reload();
    await Task.Delay(50);
    Check(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.SequenceEqual([address]), "Configuration reload cannot add or change held listeners.");
    using var client = new HttpClient { BaseAddress = new Uri(address) }; client.DefaultRequestHeaders.Host = "127.0.0.1:5064";
    using (var health = await client.GetAsync("/health")) Check(health.StatusCode == HttpStatusCode.OK, "Held process is healthy.");
    var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
    Check(session.GetProperty("executionHeld").GetBoolean() && session.GetProperty("role").GetString() == "owner" && session.GetProperty("roles").GetArrayLength() == 0, "Session explicitly reports held execution without provider preparation.");
    var runtime = await client.GetFromJsonAsync<JsonElement>("/api/runtime");
    Check(runtime.GetProperty("executionHeld").GetBoolean() && runtime.GetProperty("dataRoot").GetString() == dataRoot && runtime.GetProperty("tailnetAddress").ValueKind == JsonValueKind.Null, "Runtime preserves owner/data identity and enables no tailnet listener.");
    foreach (var path in new[] { "/api/requests", "/api/github/evidence?repository=owner/example&number=42", "/api/github/reconnect", "/api/sessions", "/api/github/workflows/chat/fixture", "/api/future-provider-dispatch" })
        using (var reply = await client.GetAsync(path)) Check(reply.StatusCode == HttpStatusCode.ServiceUnavailable, "Provider/status GET cannot reach normal dispatch while held.");
    foreach (var path in new[] { "/api/requests/fixture/agent-reply", "/api/requests/fixture/allow-agent", "/api/github/workflows/fixture/loop", "/api/github/loops/fixture/resume", "/api/github/workflows/fixture/refresh", "/api/github/workflows/fixture/publish", "/api/mcp", "/api/agent/shift", "/api/connections/microsoft/send" })
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { fictional = true }) };
        var id = Guid.NewGuid().ToString("D"); message.Headers.Add(WorkspaceCorrelation.Header, id);
        using var reply = await client.SendAsync(message);
        var problem = await reply.Content.ReadFromJsonAsync<JsonElement>();
        Check(reply.StatusCode == HttpStatusCode.ServiceUnavailable && problem.GetProperty("code").GetString() == "execution-held", "Every mutation is refused before any authority or provider side effect.");
        Check(reply.Headers.GetValues(WorkspaceCorrelation.Header).Single() == id && problem.GetProperty("correlationId").GetString() == id, "Held refusals carry the opaque diagnostic request ID.");
    }
    foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        using (var reply = await client.SendAsync(new HttpRequestMessage(method, "/api/runtime"))) Check(reply.StatusCode == HttpStatusCode.ServiceUnavailable, "Safe status paths cannot bypass the hold with mutations.");
    using (var forwarded = new HttpRequestMessage(HttpMethod.Get, "/api/session"))
    { forwarded.Headers.Add("X-Forwarded-Host", "fictional.ts.net"); using var reply = await client.SendAsync(forwarded); Check(reply.StatusCode == HttpStatusCode.Forbidden, "Forwarded access cannot create an access request or reach held status."); }
    using (var badId = new HttpRequestMessage(HttpMethod.Post, "/api/mcp"))
    { badId.Headers.Add(WorkspaceCorrelation.Header, "fictional-sensitive-marker"); using var reply = await client.SendAsync(badId); Check(Guid.TryParseExact(reply.Headers.GetValues(WorkspaceCorrelation.Header).Single(), "D", out _), "Untrusted diagnostic headers are replaced by bounded opaque IDs."); }
    Check((await client.GetStringAsync("/")).Contains("Fictional held fixture"), "The local held UI remains available.");
    await app.StopAsync();
    var after = Fingerprint();
    Check(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var hash) && hash == pair.Value), "Held startup, reads, refused dispatch and shutdown preserve every original database/config byte.");
    Console.WriteLine("PASS " + checks + " held startup, local status, provider absence, dispatch refusal, correlation and work preservation checks");
}
finally { Environment.SetEnvironmentVariable(HeldWorkspace.Variable, previous); }
await DiscoveryChecks.RunAsync();
await RecoveryChecks.RunAsync();
