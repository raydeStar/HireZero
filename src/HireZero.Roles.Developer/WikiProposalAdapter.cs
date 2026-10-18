using System.Text.Json;
using HireZero.Tools.Wiki;

namespace HireZero.Roles.Developer;

public sealed class WikiAdapterException(int status, bool uncertain = false) : Exception
{
    public int Status { get; } = status;
    public bool Uncertain { get; } = uncertain;
}

// Wiki proposals and publication through the wiki.* tools (docs/adr/0007-tool-registry.md). Contribution runs as the
// agent, publication as the owner role; both only for synthetic pilot projects.
public sealed class WikiProposalAdapter(ToolRegistry? registry = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IToolbox? _tools = registry?.For(DeveloperModule.Shared);
    public static bool Enabled => Environment.GetEnvironmentVariable("HIREZERO_SYNTHETIC_WIKI") == "1";

    // One signed-in Wiki session: every request runs as the matching wiki.* tool.
    private sealed class Session(IToolbox tools, string name, CancellationToken token)
    {
        public async Task<ServiceReply> SendAsync(string method, string path, object? body = null, string? csrf = null)
        {
            var call = new WikiCall(
                name,
                method,
                path,
                body is null ? null : JsonSerializer.Serialize(body, Json),
                csrf
            );
            var contract = WikiTools.Classify(call) ?? throw new WikiAdapterException(409);
            return await tools.InvokeAsync(contract, call, token, DeveloperAuthority.ForService(contract));
        }

        public Task<ServiceReply> GetAsync(string path) => SendAsync("GET", path);
    }

    public async Task<WikiTarget[]> TargetsAsync(WikiProject project, CancellationToken cancellationToken) =>
        await SessionAsync(project, async session => await ReadTargetsAsync(session, project), cancellationToken);

    public async Task<WikiReceipt> SubmitAsync(WikiDraft draft, bool retry, CancellationToken cancellationToken) =>
        await SessionAsync(
            draft.Destination,
            async session =>
            {
                var targets = await ReadTargetsAsync(session, draft.Destination);
                if (!retry && draft.Before is { } before)
                {
                    var current = targets.FirstOrDefault(item => item.EntryId == before.EntryId);
                    if (current is null || current != before || current.Status != "current" || !current.Supported)
                        throw new WikiAdapterException(409);
                }
                else if (
                    !retry
                    && draft.Before is null
                    && targets.Any(item => item.KnowledgeKey == draft.Content.KnowledgeKey)
                )
                    throw new WikiAdapterException(409);
                using var sessionJson = Read(await session.GetAsync("api/session"));
                var csrf = sessionJson.RootElement.GetProperty("csrf").GetString();
                if (string.IsNullOrWhiteSpace(csrf))
                    throw new WikiAdapterException(503);
                var response = await session.SendAsync(
                    "POST",
                    "api/proposals",
                    new
                    {
                        area = "engineering",
                        project = draft.Destination.WikiProjectId,
                        draft.Content.KnowledgeKey,
                        draft.Content.Kind,
                        draft.Content.Title,
                        draft.Content.Body,
                        draft.Content.Reason,
                        source = draft.Content.SourceNote,
                        idempotencyKey = IdempotencyKey(draft),
                        operation = draft.Before is null ? "Create" : "Replace",
                        evidence = "Reported",
                        expectedVersion = draft.Before?.Version ?? 0,
                        environment = draft.Before?.Environment ?? "all",
                        region = draft.Before?.Region ?? "all",
                    },
                    csrf
                );
                using var json = Read(response, posting: true);
                var value = json.RootElement;
                if (
                    value.ValueKind != JsonValueKind.Object
                    || !value.TryGetProperty("id", out var id)
                    || !Guid.TryParse(id.GetString(), out _)
                    || !value.TryGetProperty("entryId", out var entryId)
                    || !Guid.TryParse(entryId.GetString(), out _)
                    || !value.TryGetProperty("state", out var state)
                    || state.ValueKind != JsonValueKind.String
                )
                    throw new WikiAdapterException(503, true);
                return new WikiReceipt(id.GetString()!, entryId.GetString()!, state.GetString()!);
            },
            cancellationToken
        );

    // The key the Wiki deduplicates a proposal by, and the publication authority binds to.
    public static string IdempotencyKey(WikiDraft draft) => $"developer-workspace:{draft.Id}:{draft.Version}";

    public async Task ApproveAutomaticAsync(WikiDraft draft, WikiReceipt receipt, CancellationToken cancellationToken)
    {
        if (receipt.State == "Approved")
            return;
        if (receipt.State != "Proposed")
            throw new WikiAdapterException(409);
        await SessionAsync(
            draft.Destination,
            async session =>
            {
                using var candidates = Read(await session.GetAsync("api/review"));
                var found = candidates
                    .RootElement.EnumerateArray()
                    .FirstOrDefault(item =>
                        item.GetProperty("revision").GetProperty("id").GetString() == receipt.RevisionId
                    );
                if (found.ValueKind != JsonValueKind.Object)
                    throw new WikiAdapterException(503, true);
                var entry = found.GetProperty("entry");
                var revision = found.GetProperty("revision");
                if (
                    entry.GetProperty("id").GetString() != receipt.EntryId
                    || entry.GetProperty("area").GetString() != "engineering"
                    || entry.GetProperty("project").GetString() != draft.Destination.WikiProjectId
                    || entry.GetProperty("knowledgeKey").GetString() != draft.Content.KnowledgeKey
                    || entry.GetProperty("kind").GetString() != draft.Content.Kind
                    || revision.GetProperty("title").GetString() != draft.Content.Title.Trim()
                    || revision.GetProperty("body").GetString() != draft.Content.Body.Trim()
                    || revision.GetProperty("reason").GetString() != draft.Content.Reason.Trim()
                    || revision.GetProperty("source").GetString() != draft.Content.SourceNote.Trim()
                    || revision.GetProperty("expectedVersion").GetInt32() != (draft.Before?.Version ?? 0)
                )
                    throw new WikiAdapterException(409);
                using var identity = Read(await session.GetAsync("api/session"));
                var approved = await session.SendAsync(
                    "POST",
                    "api/review/" + receipt.RevisionId,
                    new
                    {
                        contentHash = revision.GetProperty("contentHash").GetString(),
                        expectedVersion = draft.Before?.Version ?? 0,
                        expectedReviewVersion = 0,
                        approve = true,
                        conflictsReviewed = false,
                        note = "Automatic publication under the developer workspace owner's policy; no human review step.",
                    },
                    identity.RootElement.GetProperty("csrf").GetString()
                );
                using var result = Read(approved, posting: true);
                if (
                    result.RootElement.GetProperty("id").GetString() != receipt.RevisionId
                    || result.RootElement.GetProperty("state").GetString() != "Approved"
                )
                    throw new WikiAdapterException(503, true);
                return true;
            },
            cancellationToken,
            publication: true
        );
    }

    private async Task<T> SessionAsync<T>(
        WikiProject project,
        Func<Session, Task<T>> action,
        CancellationToken cancellationToken,
        bool publication = false
    )
    {
        if (!Enabled || !project.Synthetic || !project.WikiProjectId.StartsWith("synthetic-", StringComparison.Ordinal))
            throw new WikiAdapterException(409);
        var tools = _tools ?? throw new WikiAdapterException(409);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = new Session(tools, publication ? "publication" : "contribution", cancellationToken);
            using var mode = Read(await session.GetAsync("api/identity-mode"));
            if (!mode.RootElement.TryGetProperty("localPilot", out var pilot) || pilot.ValueKind != JsonValueKind.True)
                throw new WikiAdapterException(409);
            var signed = await session.SendAsync("POST", "api/pilot/session?role=" + (publication ? "owner" : "agent"));
            if (!signed.Success)
                throw new WikiAdapterException(503);
            return await action(session);
        }
        // A refused call never reached the Wiki, so its outcome is certain.
        catch (ToolRefusedException)
        {
            throw new WikiAdapterException(409);
        }
        catch (ToolFailureException failure) when (failure.Kind == "refused")
        {
            throw new WikiAdapterException(409);
        }
        catch (Exception error)
            when (error is ToolFailureException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new WikiAdapterException(503, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<WikiTarget[]> ReadTargetsAsync(Session session, WikiProject project)
    {
        using var json = Read(
            await session.GetAsync("api/knowledge?project=" + Uri.EscapeDataString(project.WikiProjectId))
        );
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new WikiAdapterException(503);
        return ParseTargets(json.RootElement, project.WikiProjectId);
    }

    // The supported, current knowledge of one project, from a GET api/knowledge reply.
    public static WikiTarget[] ParseTargets(JsonElement knowledge, string projectId)
    {
        var result = new List<WikiTarget>();
        foreach (var value in knowledge.EnumerateArray().Take(40))
        {
            var entry = value.GetProperty("entry");
            var revision = value.GetProperty("revision");
            if (
                entry.GetProperty("project").GetString() != projectId
                || entry.GetProperty("area").GetString() != "engineering"
            )
                continue;
            bool extended = new[]
            {
                "product",
                "customerCategory",
                "versionFrom",
                "versionBefore",
                "parentEntryId",
            }.Any(key => entry.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null);
            var kind = entry.GetProperty("kind").GetString()!;
            bool linkedSources =
                revision.TryGetProperty("sourceDependencies", out var dependencies)
                && dependencies.ValueKind == JsonValueKind.Array
                && dependencies.GetArrayLength() > 0;
            bool supported =
                !extended
                && !linkedSources
                && entry.GetProperty("version").GetInt32() == revision.GetProperty("version").GetInt32()
                && kind is "Rule" or "Project" or "Gotcha" or "Artifact";
            var status = value.GetProperty("status").GetString()!;
            if (value.TryGetProperty("reportedConcerns", out var concerns) && concerns.GetInt32() > 0)
                status = "reported-concerns";
            result.Add(
                new(
                    entry.GetProperty("id").GetString()!,
                    revision.GetProperty("id").GetString()!,
                    entry.GetProperty("knowledgeKey").GetString()!,
                    kind,
                    revision.GetProperty("title").GetString()!,
                    revision.GetProperty("body").GetString()!,
                    entry.GetProperty("version").GetInt32(),
                    status,
                    entry.GetProperty("environment").GetString()!,
                    entry.GetProperty("region").GetString()!,
                    supported
                )
            );
        }
        return [.. result];
    }

    // The reply as JSON; failures map to blocked (4xx validation) or uncertain (a failed post).
    private static JsonDocument Read(ServiceReply reply, bool posting = false)
    {
        if (!reply.Success)
            throw new WikiAdapterException(
                reply.Status is 400 or 409 or 422 ? 409 : 503,
                posting && reply.Status >= 500
            );
        return JsonDocument.Parse(reply.Body);
    }
}
