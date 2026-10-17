using System.Text.Json;
using HireZero.Tools.Jev;
using HireZero.Tools.SemaForge;
using HireZero.Tools.Wiki;

namespace HireZero.Roles.Developer;

public sealed record SourceView(string Status, JsonElement? Packet);

public sealed record ContextView(SourceView SemaForge, SourceView Wiki);

public sealed class LocalSources(RequestStore store, WikiDraftStore wikiDrafts, ToolRegistry? registry = null)
{
    private readonly IToolbox? _tools = registry?.For(DeveloperModule.Shared);
    private static readonly char[] SplitCharacters = [' ', '\t', '\n', '\r', '.', ',', ':', ';', '-', '/'];

    public async Task<object> AvailabilityAsync(CancellationToken cancellationToken)
    {
        var sema = ProbeAsync(() => Service(SemaForgeTools.Read, new ServiceCall("GET", "/health"), cancellationToken));
        var wiki = WikiProposalAdapter.Enabled
            ? ProbeAsync(() => Service(WikiTools.Read, new WikiCall("context", "GET", "/healthz"), cancellationToken))
            : Task.FromResult("disabled");
        await Task.WhenAll(sema, wiki);
        return new
        {
            semaForge = await sema,
            jev = await JevConfiguredAsync(cancellationToken) ? "configured" : "missing-key",
            wiki = await wiki,
        };
    }

    private async Task<bool> JevConfiguredAsync(CancellationToken token) =>
        _tools is not null && (await _tools.InvokeAsync(JevTools.Status, new(false), token)).Configured;

    private async Task<ServiceReply> Service<T>(ToolContract<T, ServiceReply> contract, T call, CancellationToken token)
    {
        var tools = _tools ?? throw new ToolFailureException("unavailable", "Local sources are not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        return await tools.InvokeAsync(contract, call, timeout.Token);
    }

    private static async Task<string> ProbeAsync(Func<Task<ServiceReply>> probe)
    {
        try
        {
            return (await probe()).Success ? "online" : "unavailable";
        }
        catch (Exception error) when (error is ToolFailureException or OperationCanceledException)
        {
            return "unavailable";
        }
    }

    public async Task<TriageView> SuggestAsync(WorkRequest item, CancellationToken cancellationToken)
    {
        if (!await JevConfiguredAsync(cancellationToken))
            return TriageDecision.Unavailable(
                "Jev needs its server-side API key. Reconnect and restart the local workspace."
            );
        var candidates = store
            .Requests(null)
            .Where(other => other.Id != item.Id && other.SemaForgeId is not null)
            .Select(other => new { Request = other, Score = Overlap(item.Title, other.Title) })
            .OrderByDescending(other => other.Score)
            .Take(5)
            .ToArray();
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
                    ["uncertain"] = "Intent or ownership is unclear",
                },
            },
        };
        if (candidates.Length > 0)
            questions["match"] = new
            {
                type = "choice",
                instructions = "Which already tracked request matches best?",
                criteria = candidates.ToDictionary(other => other.Request.Id, other => other.Request.Title),
            };

        try
        {
            var reply = await _tools!.InvokeAsync(
                JevTools.Classify,
                new(
                    JsonSerializer.Serialize(
                        new
                        {
                            model = "jev-latest",
                            state = new
                            {
                                project = item.Project,
                                title = item.Title,
                                body = item.Description[..Math.Min(700, item.Description.Length)],
                            },
                            questions,
                        }
                    )
                ),
                cancellationToken
            );
            if (!reply.Success)
                return TriageDecision.Unavailable(
                    reply.Status is 401 or 403
                        ? "Jev rejected the configured key. Reconnect its server-side credential."
                    : reply.Status == 429 ? "Jev is rate-limited. Review manually or try later."
                    : "Jev did not confirm a classification."
                );
            using var document = JsonDocument.Parse(reply.Body);
            return TriageDecision.Parse(
                document.RootElement,
                candidates.Select(candidate => candidate.Request.Id).ToArray()
            );
        }
        catch (Exception error) when (error is ToolFailureException or JsonException)
        {
            return TriageDecision.Unavailable("Jev is unavailable. No work was changed.");
        }
    }

    public async Task<ContextView> ContextAsync(WorkRequest item, CancellationToken cancellationToken)
    {
        var semaForge = item.SemaForgeId is null
            ? new SourceView("not-linked", null)
            : await SemaForgeContextAsync(item.SemaForgeId, cancellationToken);
        var wiki = WikiProposalAdapter.Enabled
            ? await WikiContextAsync(item.Project, cancellationToken)
            : new SourceView("disabled-synthetic-pilot", null);
        return new(semaForge, wiki);
    }

    private async Task<SourceView> SemaForgeContextAsync(string workItemId, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _tools!.InvokeAsync(
                SemaForgeTools.Read,
                new ServiceCall(
                    "POST",
                    "/api/work-intelligence/context-packet",
                    JsonSerializer.Serialize(new { workItemIds = new[] { workItemId } })
                ),
                cancellationToken
            );
            if (!reply.Success)
                return new("unavailable", null);
            using var document = JsonDocument.Parse(reply.Body);
            return new("ready", document.RootElement.Clone());
        }
        catch (Exception error) when (error is ToolFailureException or JsonException or NullReferenceException)
        {
            return new("unavailable", null);
        }
    }

    private async Task<SourceView> WikiContextAsync(string project, CancellationToken cancellationToken)
    {
        var destination = wikiDrafts.Project(project);
        if (destination is { Synthetic: false })
            return new("authenticated-wiki-required", null);
        if (destination is not null)
            project = destination.WikiProjectId;
        try
        {
            var tools = _tools ?? throw new ToolFailureException("unavailable", "Wiki is not configured.");
            var session = await tools.InvokeAsync(
                WikiTools.Read,
                new WikiCall("context", "POST", "/api/pilot/session?role=agent"),
                cancellationToken
            );
            if (!session.Success)
                return new("unavailable", null);
            var reply = await tools.InvokeAsync(
                WikiTools.Read,
                new WikiCall(
                    "context",
                    "GET",
                    "/api/agent/brief?project=" + Uri.EscapeDataString(project) + "&limit=8&bodyBudget=3000"
                ),
                cancellationToken
            );
            if (!reply.Success)
                return new("unavailable", null);
            using var document = JsonDocument.Parse(reply.Body);
            var fresh =
                document.RootElement.TryGetProperty("recheckBy", out var recheckBy)
                && recheckBy.TryGetDateTimeOffset(out var nextCheck)
                && nextCheck > DateTimeOffset.UtcNow;
            var status =
                !fresh ? "stale"
                : document.RootElement.TryGetProperty("complete", out var complete) && complete.GetBoolean() ? "ready"
                : "incomplete";
            return new(status, document.RootElement.Clone());
        }
        catch (Exception error) when (error is ToolFailureException or JsonException)
        {
            return new("unavailable", null);
        }
    }

    private static int Overlap(string left, string right)
    {
        var leftWords = left.ToLowerInvariant()
            .Split(SplitCharacters, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
        return right
            .ToLowerInvariant()
            .Split(SplitCharacters, StringSplitOptions.RemoveEmptyEntries)
            .Count(leftWords.Contains);
    }
}
