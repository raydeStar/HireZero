namespace HireZero.Core;

// Long-term memory belongs to the model gateway's memory layer (OpenClaw memory-core): dated notes that are
// searchable right away and consolidated into durable memory overnight. HireZero only decides what goes in,
// cleans it, and shows the owner what is there.
public sealed record MemoryHit(string Path, int Line, double Score, string Snippet, string Source, string[] Flags);

public sealed record MemoryFile(string Path, long Bytes, string UpdatedAt);

public sealed record MemoryOverview(
    string LongTerm,
    bool LongTermTruncated,
    MemoryFile[] Notes,
    string? Dreams,
    string Store
);

public interface IMemoryStore
{
    Task<MemoryHit[]> SearchAsync(string query, int max, CancellationToken token);

    // Writes (replaces) one note file under memory/, named <day>-hirezero-<topic>.md.
    Task WriteNoteAsync(string day, string topic, string markdown, CancellationToken token);

    // Appends one line to today's HireZero note.
    Task AppendAsync(string day, string line, CancellationToken token);
    Task<MemoryOverview> OverviewAsync(CancellationToken token);
}

public sealed class MemoryService(IMemoryStore store)
{
    public const int MaxNote = 2000;
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.Now;

    public async Task<MemoryHit[]> SearchAsync(string query, int max, CancellationToken token)
    {
        var clean = ContentGuard.Clean(query ?? "", 200).Text.Trim();
        if (clean.Length < 2)
            throw new WorkspaceException("Search for at least two characters.", 400);
        // Notes can hold text that came from mail or chats; results are cleaned and flagged before they are shown.
        return
        [
            .. (await store.SearchAsync(clean, Math.Clamp(max, 1, 25), token)).Select(hit =>
            {
                var snippet = ContentGuard.Clean(hit.Snippet, 600);
                return hit with
                {
                    Snippet = snippet.Text,
                    Path = ContentGuard.Clean(hit.Path, 200).Text,
                    Flags = [.. hit.Flags.Concat(snippet.Flags).Distinct()],
                };
            }),
        ];
    }

    // The owner's own note. Text that reads like instructions to an agent isn't stored, since every later turn reads memory.
    public async Task<string> RememberAsync(string text, string source, CancellationToken token)
    {
        var clean = ContentGuard.Clean(text ?? "", MaxNote);
        var body = clean.Text.Trim().ReplaceLineEndings(" ");
        if (body.Length == 0)
            throw new WorkspaceException("Write something to remember.", 400);
        if (clean.Suspicious)
            throw new WorkspaceException(
                "That reads like instructions to an agent, so it wasn't saved to memory. Rephrase it as a fact or preference.",
                400
            );
        var now = Clock();
        await store.AppendAsync(
            now.ToString("yyyy-MM-dd"),
            "- " + now.ToString("HH:mm") + " (" + Label(source) + ") " + body,
            token
        );
        return body;
    }

    // A day's brief as a compact note: counts, titles and summaries only. Flagged text is left out.
    public async Task RecordDayAsync(DailyBrief brief, CancellationToken token)
    {
        var lines = new List<string> { "# HireZero day " + brief.Day, "" };
        if (brief.Sessions.Length > 0)
        {
            lines.Add("## Coding sessions");
            foreach (var session in brief.Sessions)
            {
                var flagged = session.Flags?.Contains(ContentGuard.Injection) == true;
                lines.Add(
                    "- "
                        + (session.App == "codex" ? "Codex" : "Claude")
                        + ": "
                        + (flagged ? "(flagged title)" : session.Title)
                        + (session.Repository is null ? "" : " in " + session.Repository)
                        + (session.Branch is null ? "" : " on " + session.Branch)
                        + $" — {session.Turns} asks, {session.Files.Length} files, {session.Commands} commands ({session.FailedCommands} failed)"
                        + (session.EndedWithQuestion ? "; ended with a question for the owner" : "")
                        + (session.PrUrl is null ? "" : "; PR " + session.PrUrl)
                );
                if (session.Narrative is { Length: > 0 } narrative && !flagged)
                    lines.Add("  " + narrative.ReplaceLineEndings(" "));
            }
            lines.Add("");
        }
        if (brief.NeedsYou.Length > 0)
        {
            lines.Add("## Waiting on the owner at the end of the day");
            foreach (var item in brief.NeedsYou.Where(item => item.Kind != "held").Take(10))
                lines.Add("- " + item.Title + ": " + item.Detail);
            lines.Add("");
        }
        var text = ContentGuard.Clean(string.Join("\n", lines), 12_000);
        await store.WriteNoteAsync(brief.Day, "brief", text.Text, token);
    }

    public async Task<MemoryOverview> OverviewAsync(CancellationToken token)
    {
        var overview = await store.OverviewAsync(token);
        return overview with
        {
            LongTerm = ContentGuard.Clean(overview.LongTerm, 6000).Text,
            Dreams = overview.Dreams is null ? null : ContentGuard.Clean(overview.Dreams, 1500).Text,
            Notes = [.. overview.Notes.Select(note => note with { Path = ContentGuard.Clean(note.Path, 200).Text })],
        };
    }

    static string Label(string source) =>
        source is { Length: > 0 and <= 40 }
        && source.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or ' ')
            ? source
            : "owner";
}
