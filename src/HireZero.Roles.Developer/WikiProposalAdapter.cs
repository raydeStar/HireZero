using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HireZero.Roles.Developer;

public sealed class WikiAdapterException(int status, bool uncertain = false) : Exception
{
    public int Status { get; } = status;
    public bool Uncertain { get; } = uncertain;
}

public sealed class WikiProposalAdapter(IHttpClientFactory clients)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static bool Enabled => Environment.GetEnvironmentVariable("HIREZERO_SYNTHETIC_WIKI") == "1";
    public static Uri Origin()
    {
        var url = Environment.GetEnvironmentVariable("HIREZERO_WIKI_URL") ?? "http://127.0.0.1:5094";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var origin) || origin.Scheme != "http" || !origin.IsLoopback ||
            !string.IsNullOrEmpty(origin.UserInfo) || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            throw new InvalidOperationException("The Wiki pilot must use an HTTP loopback origin.");
        return origin;
    }
    public async Task<WikiTarget[]> TargetsAsync(WikiProject project, CancellationToken cancellationToken) =>
        await SessionAsync(project, async client => await ReadTargetsAsync(client, project, cancellationToken), cancellationToken);

    public async Task<WikiReceipt> SubmitAsync(WikiDraft draft, bool retry, CancellationToken cancellationToken) =>
        await SessionAsync(draft.Destination, async client =>
        {
            var targets = await ReadTargetsAsync(client, draft.Destination, cancellationToken);
            if (!retry && draft.Before is { } before)
            {
                var current = targets.FirstOrDefault(item => item.EntryId == before.EntryId);
                if (current is null || current != before || current.Status != "current" || !current.Supported)
                    throw new WikiAdapterException(409);
            }
            else if (!retry && draft.Before is null && targets.Any(item => item.KnowledgeKey == draft.Content.KnowledgeKey)) throw new WikiAdapterException(409);
            using var session = await client.GetAsync("api/session", cancellationToken);
            using var sessionJson = await ReadAsync(session, cancellationToken);
            var csrf = sessionJson.RootElement.GetProperty("csrf").GetString();
            if (string.IsNullOrWhiteSpace(csrf)) throw new WikiAdapterException(503);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/proposals");
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            request.Headers.Add("Origin", Origin().GetLeftPart(UriPartial.Authority));
            request.Content = JsonContent.Create(new
            {
                area = "engineering", project = draft.Destination.WikiProjectId,
                draft.Content.KnowledgeKey, draft.Content.Kind, draft.Content.Title, draft.Content.Body, draft.Content.Reason,
                source = draft.Content.SourceNote, idempotencyKey = $"developer-workspace:{draft.Id}:{draft.Version}",
                operation = draft.Before is null ? "Create" : "Replace", evidence = "Reported",
                expectedVersion = draft.Before?.Version ?? 0,
                environment = draft.Before?.Environment ?? "all", region = draft.Before?.Region ?? "all"
            });
            using var response = await client.SendAsync(request, cancellationToken);
            using var json = await ReadAsync(response, cancellationToken, posting: true);
            var value = json.RootElement;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out _) ||
                !value.TryGetProperty("entryId", out var entryId) || !Guid.TryParse(entryId.GetString(), out _) ||
                !value.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String)
                throw new WikiAdapterException(503, true);
            return new WikiReceipt(id.GetString()!, entryId.GetString()!, state.GetString()!);
        }, cancellationToken);

    public async Task ApproveAutomaticAsync(WikiDraft draft, WikiReceipt receipt, CancellationToken cancellationToken)
    {
        if (receipt.State == "Approved") return;
        if (receipt.State != "Proposed") throw new WikiAdapterException(409);
        await SessionAsync(draft.Destination, async client =>
        {
            using var response = await client.GetAsync("api/review", cancellationToken);
            using var candidates = await ReadAsync(response, cancellationToken);
            var found = candidates.RootElement.EnumerateArray().FirstOrDefault(item =>
                item.GetProperty("revision").GetProperty("id").GetString() == receipt.RevisionId);
            if (found.ValueKind != JsonValueKind.Object) throw new WikiAdapterException(503, true);
            var entry = found.GetProperty("entry"); var revision = found.GetProperty("revision");
            if (entry.GetProperty("id").GetString() != receipt.EntryId || entry.GetProperty("area").GetString() != "engineering" ||
                entry.GetProperty("project").GetString() != draft.Destination.WikiProjectId ||
                entry.GetProperty("knowledgeKey").GetString() != draft.Content.KnowledgeKey ||
                entry.GetProperty("kind").GetString() != draft.Content.Kind ||
                revision.GetProperty("title").GetString() != draft.Content.Title.Trim() ||
                revision.GetProperty("body").GetString() != draft.Content.Body.Trim() ||
                revision.GetProperty("reason").GetString() != draft.Content.Reason.Trim() ||
                revision.GetProperty("source").GetString() != draft.Content.SourceNote.Trim() ||
                revision.GetProperty("expectedVersion").GetInt32() != (draft.Before?.Version ?? 0))
                throw new WikiAdapterException(409);
            using var session = await client.GetAsync("api/session", cancellationToken);
            using var identity = await ReadAsync(session, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/review/" + receipt.RevisionId);
            request.Headers.Add("X-CSRF-TOKEN", identity.RootElement.GetProperty("csrf").GetString());
            request.Headers.Add("Origin", Origin().GetLeftPart(UriPartial.Authority));
            request.Content = JsonContent.Create(new
            {
                contentHash = revision.GetProperty("contentHash").GetString(), expectedVersion = draft.Before?.Version ?? 0,
                expectedReviewVersion = 0, approve = true, conflictsReviewed = false,
                note = "Automatic publication under the developer workspace owner's policy; no human review step."
            });
            using var approved = await client.SendAsync(request, cancellationToken);
            using var result = await ReadAsync(approved, cancellationToken, posting: true);
            if (result.RootElement.GetProperty("id").GetString() != receipt.RevisionId ||
                result.RootElement.GetProperty("state").GetString() != "Approved") throw new WikiAdapterException(503, true);
            return true;
        }, cancellationToken, publication: true);
    }

    private async Task<T> SessionAsync<T>(WikiProject project, Func<HttpClient, Task<T>> action, CancellationToken cancellationToken,
        bool publication = false)
    {
        if (!Enabled || !project.Synthetic || !project.WikiProjectId.StartsWith("synthetic-", StringComparison.Ordinal))
            throw new WikiAdapterException(409);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var client = clients.CreateClient(publication ? "wiki-publication" : "wiki-contribution");
            using var status = await client.GetAsync("api/identity-mode", cancellationToken);
            using var mode = await ReadAsync(status, cancellationToken);
            if (!mode.RootElement.TryGetProperty("localPilot", out var pilot) || pilot.ValueKind != JsonValueKind.True)
                throw new WikiAdapterException(409);
            using var signin = new HttpRequestMessage(HttpMethod.Post, "api/pilot/session?role=" + (publication ? "owner" : "agent"));
            signin.Headers.Add("X-Wiki-Pilot", "local-demo");
            using var signed = await client.SendAsync(signin, cancellationToken);
            if (!signed.IsSuccessStatusCode) throw new WikiAdapterException(503);
            return await action(client);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new WikiAdapterException(503, true);
        }
        finally { _gate.Release(); }
    }
    private static async Task<WikiTarget[]> ReadTargetsAsync(HttpClient client, WikiProject project, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("api/knowledge?project=" + Uri.EscapeDataString(project.WikiProjectId), cancellationToken);
        using var json = await ReadAsync(response, cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array) throw new WikiAdapterException(503);
        var result = new List<WikiTarget>();
        foreach (var value in json.RootElement.EnumerateArray().Take(40))
        {
            var entry = value.GetProperty("entry"); var revision = value.GetProperty("revision");
            if (entry.GetProperty("project").GetString() != project.WikiProjectId || entry.GetProperty("area").GetString() != "engineering") continue;
            bool extended = new[] { "product", "customerCategory", "versionFrom", "versionBefore", "parentEntryId" }
                .Any(key => entry.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null);
            var kind = entry.GetProperty("kind").GetString()!;
            bool linkedSources = revision.TryGetProperty("sourceDependencies", out var dependencies) &&
                dependencies.ValueKind == JsonValueKind.Array && dependencies.GetArrayLength() > 0;
            bool supported = !extended && !linkedSources && entry.GetProperty("version").GetInt32() == revision.GetProperty("version").GetInt32() &&
                kind is "Rule" or "Project" or "Gotcha" or "Artifact";
            var status = value.GetProperty("status").GetString()!;
            if (value.TryGetProperty("reportedConcerns", out var concerns) && concerns.GetInt32() > 0) status = "reported-concerns";
            result.Add(new(entry.GetProperty("id").GetString()!, revision.GetProperty("id").GetString()!,
                entry.GetProperty("knowledgeKey").GetString()!, kind, revision.GetProperty("title").GetString()!,
                revision.GetProperty("body").GetString()!, entry.GetProperty("version").GetInt32(),
                status, entry.GetProperty("environment").GetString()!,
                entry.GetProperty("region").GetString()!, supported));
        }
        return [.. result];
    }
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken, bool posting = false)
    {
        if (!response.IsSuccessStatusCode) throw new WikiAdapterException(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity ? 409 : 503,
            posting && (int)response.StatusCode >= 500);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, cancellationToken)) != 0)
        {
            if (buffer.Length + count > 2 * 1024 * 1024) throw new WikiAdapterException(503, posting);
            buffer.Write(bytes, 0, count);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
    }
}
