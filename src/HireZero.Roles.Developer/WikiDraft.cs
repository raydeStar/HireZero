using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Roles.Developer;

public sealed record WikiProject(string Project, string WikiProjectId, bool Synthetic);

public sealed record WikiEvidence(string Kind, string Id);

public sealed record WikiTarget(
    string EntryId,
    string RevisionId,
    string KnowledgeKey,
    string Kind,
    string Title,
    string Body,
    int Version,
    string Status,
    string Environment,
    string Region,
    bool Supported
);

public sealed record WikiDraftContent(
    string KnowledgeKey,
    string Kind,
    string Title,
    string Body,
    string Reason,
    string SourceNote,
    bool Conflict,
    bool MissingInformation,
    string ResolutionNote
);

public sealed record WikiDraft(
    string Id,
    string RequestId,
    string Project,
    WikiProject Destination,
    WikiDraftContent Content,
    WikiTarget? Before,
    WikiEvidence[] Evidence,
    int Version,
    string Status,
    string? RevisionId,
    string? EntryId,
    string UpdatedAt
);

public sealed record WikiReceipt(string RevisionId, string EntryId, string State);

public static partial class WikiDraftPolicy
{
    public static bool ValidKey(string? key) => key is { Length: > 0 and <= 100 } && KeyPattern().IsMatch(key);

    public static bool ValidContent(WikiDraftContent? value) =>
        value is not null
        && ValidKey(value.KnowledgeKey)
        && value.Kind is "Rule" or "Project" or "Decision" or "Gotcha" or "Question" or "Artifact"
        && Text(value.Title, 180)
        && Text(value.Body, 6000)
        && Text(value.Reason, 1000)
        && Text(value.SourceNote, 1000)
        && value.ResolutionNote is not null
        && value.ResolutionNote.Length <= 1000;

    public static bool CanSubmit(WikiDraft draft) =>
        ValidContent(draft.Content)
        && !draft.Content.Conflict
        && !draft.Content.MissingInformation
        && !ScreenPattern()
            .IsMatch(
                string.Join(
                    '\n',
                    draft.Content.Title,
                    draft.Content.Body,
                    draft.Content.Reason,
                    draft.Content.SourceNote
                )
            )
        && draft.Destination.Synthetic
        && draft.Destination.WikiProjectId.StartsWith("synthetic-", StringComparison.Ordinal)
        && (draft.Before is null || draft.Before.Supported && draft.Before.Status == "current");

    public static bool Editable(string status) => status == "draft";

    public static string PublicationSummary(WikiDraft draft)
    {
        // Summarize published fields only, without another model call or private provenance.
        var title = SummaryText(draft.Content.Title, 110);
        var body =
            draft
                .Content.Body.Split(['.', '!', '?', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
            ?? draft.Content.Title;
        return $"{(draft.Before is null ? "Added" : "Updated")} Wiki knowledge ({title}): {SummaryText(body, 170)}.";
    }

    private static string SummaryText(string text, int maximum)
    {
        var clean = SummaryPattern().Replace(text, " ").Trim();
        return clean.Length <= maximum ? clean : clean[..maximum].TrimEnd();
    }

    public static WikiDraftContent? ParseSuggestion(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            if (
                json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("conflict", out var conflict)
                || conflict.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !json.RootElement.TryGetProperty("missingInformation", out var missing)
                || missing.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            )
                return null;
            var value = JsonSerializer.Deserialize<WikiDraftContent>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            return ValidContent(value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Text(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Contains('\0');

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("[\\s.!?`*#<>\\[\\]{}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryPattern();

    [GeneratedRegex(
        "-----BEGIN .*PRIVATE KEY-----|(?:password|client_secret|access_token|api[_-]?key)\\s*[:=]\\s*\\S{6,}|sk-(?:proj|ant)-[a-z0-9_-]+|[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}|ignore (?:all |the |any )?(?:previous|prior|system) instructions",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ScreenPattern();
}
