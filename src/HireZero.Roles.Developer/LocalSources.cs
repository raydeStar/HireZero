using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HireZero.Roles.Developer;

public sealed record SourceView(string Status, JsonElement? Packet);
public sealed record ContextView(SourceView SemaForge, SourceView Wiki);

public sealed class LocalSources(IHttpClientFactory clients, RequestStore store, WikiDraftStore wikiDrafts)
{
    private static readonly char[] SplitCharacters = [' ', '\t', '\n', '\r', '.', ',', ':', ';', '-', '/'];

    public async Task<object> AvailabilityAsync(CancellationToken cancellationToken)
    {
        var sema = ProbeAsync("semaforge", "/health", cancellationToken);
        var wiki = Environment.GetEnvironmentVariable("HIREZERO_SYNTHETIC_WIKI") == "1"
            ? ProbeAsync("wiki", "/healthz", cancellationToken) : Task.FromResult("disabled");
        await Task.WhenAll(sema, wiki);
        return new { semaForge = await sema, jev = JevConfiguration.Key() is null ? "missing-key" : "configured", wiki = await wiki };
    }

    private async Task<string> ProbeAsync(string name, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = clients.CreateClient(name);
            if (name == "semaforge") AddSemaForgeKey(client);
            using var response = await client.GetAsync(path, timeout.Token);
            return response.IsSuccessStatusCode ? "online" : "unavailable";
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return "unavailable";
        }
    }

    public async Task<TriageView> SuggestAsync(WorkRequest item, CancellationToken cancellationToken)
    {
        var key = JevConfiguration.Key();
        if (key is null) return TriageDecision.Unavailable("Jev needs its server-side API key. Reconnect and restart the local workspace.");
        var candidates = store.Requests(null)
            .Where(other => other.Id != item.Id && other.SemaForgeId is not null)
            .Select(other => new { Request = other, Score = Overlap(item.Title, other.Title) })
            .OrderByDescending(other => other.Score).Take(5).ToArray();
        var questions = new Dictionary<string, object>
        {
            ["route"] = new
            {
                type = "choice",
                instructions = "What should happen to this development request?",
                criteria = new Dictionary<string, string>
                {
                    ["existing"] = "Update one already tracked development task",
                    ["new"] = "Propose a new development task",
                    ["context"] = "Keep a useful project fact without creating work",
                    ["noise"] = "No useful development work or context",
                    ["uncertain"] = "Intent or ownership is unclear"
                }
            }
        };
        if (candidates.Length > 0)
            questions["match"] = new { type = "choice", instructions = "Which already tracked request matches best?",
                criteria = candidates.ToDictionary(other => other.Request.Id, other => other.Request.Title) };

        try
        {
            using var client = clients.CreateClient("jev");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/systemone");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = JsonContent.Create(new { model = "jev-latest",
                state = new { project = item.Project, title = item.Title, body = item.Description[..Math.Min(700, item.Description.Length)] }, questions });
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return TriageDecision.Unavailable(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? "Jev rejected the configured key. Reconnect its server-side credential."
                : response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? "Jev is rate-limited. Review manually or try later." : "Jev did not confirm a classification.");
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            return document is null ? TriageDecision.Unavailable("Jev returned no classification.") :
                TriageDecision.Parse(document.RootElement, candidates.Select(candidate => candidate.Request.Id).ToArray());
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            return TriageDecision.Unavailable("Jev is unavailable. No work was changed.");
        }
    }

    public async Task<ContextView> ContextAsync(WorkRequest item, CancellationToken cancellationToken)
    {
        var semaForge = item.SemaForgeId is null
            ? new SourceView("not-linked", null)
            : await SemaForgeContextAsync(item.SemaForgeId, cancellationToken);
        var wiki = Environment.GetEnvironmentVariable("HIREZERO_SYNTHETIC_WIKI") == "1"
            ? await WikiContextAsync(item.Project, cancellationToken)
            : new SourceView("disabled-synthetic-pilot", null);
        return new(semaForge, wiki);
    }

    private async Task<SourceView> SemaForgeContextAsync(string workItemId, CancellationToken cancellationToken)
    {
        try
        {
            using var client = clients.CreateClient("semaforge");
            AddSemaForgeKey(client);
            using var response = await client.PostAsJsonAsync("/api/work-intelligence/context-packet",
                new { workItemIds = new[] { workItemId } }, cancellationToken);
            if (!response.IsSuccessStatusCode) return new("unavailable", null);
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            return new(document is null ? "unavailable" : "ready", document?.RootElement.Clone());
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new("unavailable", null);
        }
    }

    private async Task<SourceView> WikiContextAsync(string project, CancellationToken cancellationToken)
    {
        var destination = wikiDrafts.Project(project);
        if (destination is { Synthetic: false }) return new("authenticated-wiki-required", null);
        if (destination is not null) project = destination.WikiProjectId;
        try
        {
            using var client = clients.CreateClient("wiki");
            using var signin = new HttpRequestMessage(HttpMethod.Post, "/api/pilot/session?role=agent");
            signin.Headers.Add("X-Wiki-Pilot", "local-demo");
            using var session = await client.SendAsync(signin, cancellationToken);
            if (!session.IsSuccessStatusCode) return new("unavailable", null);
            using var response = await client.GetAsync("/api/agent/brief?project=" + Uri.EscapeDataString(project) +
                "&limit=8&bodyBudget=3000", cancellationToken);
            if (!response.IsSuccessStatusCode) return new("unavailable", null);
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            if (document is null) return new("unavailable", null);
            var fresh = document.RootElement.TryGetProperty("recheckBy", out var recheckBy) &&
                recheckBy.TryGetDateTimeOffset(out var nextCheck) && nextCheck > DateTimeOffset.UtcNow;
            var status = !fresh ? "stale" :
                document.RootElement.TryGetProperty("complete", out var complete) && complete.GetBoolean()
                    ? "ready" : "incomplete";
            return new(status, document.RootElement.Clone());
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new("unavailable", null);
        }
    }

    public static void AddSemaForgeKey(HttpClient client)
    {
        var apiKey = Environment.GetEnvironmentVariable("HIREZERO_SEMAFORGE_API_KEY");
        if (!string.IsNullOrEmpty(apiKey)) client.DefaultRequestHeaders.Add("X-SemaForge-Api-Key", apiKey);
    }

    private static int Overlap(string left, string right)
    {
        var leftWords = left.ToLowerInvariant().Split(SplitCharacters, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return right.ToLowerInvariant().Split(SplitCharacters, StringSplitOptions.RemoveEmptyEntries)
            .Count(leftWords.Contains);
    }
}
