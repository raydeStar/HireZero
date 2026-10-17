using System.Collections.Concurrent;

namespace HireZero.Core;

// One sanitized thing the owner did on a day: never session titles, files, commands or messages.
public sealed record WorkLogEntry(
    string At,
    string Kind,
    string Project,
    string Text,
    string? Url,
    GitOrigin? Origin,
    int Count = 1
)
{
    // Stable within a day: what the panel asks details for.
    public string Id => Kind + ":" + (Url ?? Project);
}

// A few more facts about one entry: still sanitized (counts, states, links, a short description excerpt).
public sealed record WorkLogFact(string Label, string Value, string? Url = null);

public sealed record WorkLogDetail(string Title, WorkLogFact[] Facts, string? Excerpt, string? Url);

public sealed record WorkLogTotals(int Projects, int CodingSessions, int PullRequestsOpened, int PullRequestsMerged);

// Integrations add what they know for a time window (pull requests opened or completed, and so on).
public interface IWorkLogSource
{
    Task<WorkLogEntry[]> EntriesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken token);

    // Details for an entry this source produced; null when it isn't this source's.
    Task<WorkLogDetail?> DetailAsync(WorkLogEntry entry, CancellationToken token) =>
        Task.FromResult<WorkLogDetail?>(null);
}

public static class WorkLogText
{
    // The first paragraph of a PR description, cleaned and short; nothing when it reads like instructions.
    public static string? Excerpt(string? body, int max = 320)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        var paragraph = body.Replace("\r", "")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
                string.Join(" ", part.Split('\n').Select(line => line.Trim().TrimStart('#', '-', '*', '>').Trim()))
                    .Trim()
            )
            .FirstOrDefault(part => part.Length > 0 && !part.StartsWith("<!--", StringComparison.Ordinal));
        if (paragraph is null)
            return null;
        var clean = ContentGuard.Clean(paragraph, max * 2);
        if (clean.Suspicious)
            return null;
        return clean.Text.Length <= max ? clean.Text : clean.Text[..max].TrimEnd() + "…";
    }
}

public sealed record WorkLogDay(
    string Day,
    string? OwnerName,
    WorkLogTotals Totals,
    WorkLogEntry[] Entries,
    string[] Notes
);

// "What the owner did" for a day, as a boss may see it: shared company projects only (the sharing policy), coding
// activity as session counts per project, and pull requests from the sources. The owner sees the same view as a preview.
public sealed class WorkLogService(
    IEnumerable<IWorkLogSource> sources,
    SharingPolicy sharing,
    BriefService brief,
    SessionStore sessions
)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, WorkLogDay Day)> _cache = new();
    private readonly ConcurrentDictionary<
        string,
        Dictionary<string, (SessionDigest Digest, AgentSession? Session)[]>
    > _coding = new();
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public async Task<WorkLogDay> DayAsync(string day, TimeZoneInfo zone, CancellationToken token)
    {
        var (from, to) = BriefService.Window(day, zone);
        var fresh = to <= Clock() ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(5);
        if (_cache.TryGetValue(day, out var cached) && Clock() - cached.At < fresh)
            return cached.Day;

        var entries = new List<WorkLogEntry>();
        var notes = new List<string>();
        var coding = new Dictionary<string, (SessionDigest, AgentSession?)[]>(StringComparer.OrdinalIgnoreCase);
        // Coding activity: the day's session digests, counted per shared project. No titles or details leave here.
        foreach (
            var group in (await brief.DigestsAsync(day, zone, token))
                .Select(digest =>
                    (
                        digest,
                        project: sessions.Find(digest.SessionId) is { } session
                            ? WorkingContext.Identify(session)
                            : null
                    )
                )
                .Where(item =>
                    item.project?.Origin is { } origin && sharing.SharesWithBoss(origin, item.project.Value.Name)
                )
                .GroupBy(item => item.project!.Value.Key)
        )
        {
            var project = group.First().project!.Value;
            coding[project.Name] = [.. group.Select(item => (item.digest, sessions.Find(item.digest.SessionId)))];
            var first = group.Min(item => item.digest.FirstAt) ?? "";
            var last = group.Max(item => item.digest.LastAt) ?? "";
            entries.Add(
                new(
                    first,
                    "coding",
                    project.Name,
                    "Worked on "
                        + project.Name
                        + " in "
                        + group.Count()
                        + " coding session"
                        + (group.Count() == 1 ? "" : "s")
                        + Span(first, last, zone),
                    null,
                    project.Origin,
                    group.Count()
                )
            );
        }
        foreach (var source in sources)
        {
            try
            {
                entries.AddRange(
                    (await source.EntriesAsync(from, to, token)).Where(entry =>
                        entry.Origin is { } origin && sharing.SharesWithBoss(origin, entry.Project)
                    )
                );
            }
            catch (Exception error)
                when (error
                        is WorkspaceException
                            or HttpRequestException
                            or TaskCanceledException
                            or InvalidOperationException
                            or System.Text.Json.JsonException
                )
            {
                notes.Add(error is WorkspaceException known ? known.Message : "One source couldn't be reached.");
            }
        }
        var totals = new WorkLogTotals(
            entries.Select(entry => entry.Project).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            entries.Where(entry => entry.Kind == "coding").Sum(entry => entry.Count),
            entries.Count(entry => entry.Kind == "pr-opened"),
            entries.Count(entry => entry.Kind == "pr-merged")
        );
        var result = new WorkLogDay(
            day,
            sharing.Settings().OwnerName,
            totals,
            [.. entries.OrderBy(entry => entry.At)],
            [.. notes.Distinct()]
        );
        _cache[day] = (Clock(), result);
        _coding[day] = coding;
        return result;
    }

    // Details only for an entry that is in the day's (sanitized) log.
    public async Task<WorkLogDetail?> DetailAsync(string day, TimeZoneInfo zone, string id, CancellationToken token)
    {
        var log = await DayAsync(day, zone, token);
        if (log.Entries.FirstOrDefault(entry => entry.Id == id) is not { } entry)
            return null;
        if (entry.Kind == "coding")
            return _coding.TryGetValue(day, out var projects) && projects.TryGetValue(entry.Project, out var items)
                ? Coding(entry, items, zone)
                : null;
        foreach (var source in sources)
        {
            try
            {
                if (await source.DetailAsync(entry, token) is { } detail)
                    return detail;
            }
            catch (Exception error)
                when (error
                        is WorkspaceException
                            or HttpRequestException
                            or TaskCanceledException
                            or InvalidOperationException
                            or System.Text.Json.JsonException
                ) { }
        }
        return new(entry.Text, [], null, entry.Url);
    }

    // Coding detail is counts only: never titles, files, commands or messages.
    static WorkLogDetail Coding(
        WorkLogEntry entry,
        (SessionDigest Digest, AgentSession? Session)[] items,
        TimeZoneInfo zone
    )
    {
        var digests = items.Select(item => item.Digest).ToList();
        var passed = digests.Sum(digest =>
            digest.Tests.Count(test => test.StartsWith("passed", StringComparison.Ordinal))
        );
        var failed = digests.Sum(digest =>
            digest.Tests.Count(test => test.StartsWith("failed", StringComparison.Ordinal))
        );
        var facts = new List<WorkLogFact>
        {
            new(
                "Sessions",
                items.Length
                    + (items.Length == 1 ? " session" : " sessions")
                    + Span(
                        digests.Min(digest => digest.FirstAt) ?? "",
                        digests.Max(digest => digest.LastAt) ?? "",
                        zone
                    )
            ),
            new("Files changed", digests.Sum(digest => digest.Files.Length).ToString()),
            new(
                "Commands run",
                digests.Sum(digest => digest.Commands)
                    + (
                        digests.Sum(digest => digest.FailedCommands) is var failures and > 0
                            ? " (" + failures + " failed)"
                            : ""
                    )
            ),
        };
        if (passed + failed > 0)
            facts.Add(new("Tests", passed + " passed" + (failed > 0 ? ", " + failed + " failed" : "")));
        var branches = items
            .Select(item => item.Session?.Branch)
            .Where(branch => branch is { Length: > 0 } && branch != "main" && branch != "master")
            .Distinct()
            .Take(4)
            .ToList();
        if (branches.Count > 0)
            facts.Add(new("Branches", string.Join(", ", branches)));
        foreach (
            var pr in items.Select(item => item.Session?.PrUrl).Where(url => url is { Length: > 0 }).Distinct().Take(4)
        )
            facts.Add(
                new(
                    "Pull request",
                    pr!.Split('/').Last() is var number && int.TryParse(number, out _) ? "#" + number : "Open",
                    pr
                )
            );
        return new(entry.Text, [.. facts], null, null);
    }

    static string Span(string first, string last, TimeZoneInfo zone) =>
        DateTimeOffset.TryParse(first, out var start) && DateTimeOffset.TryParse(last, out var end)
            ? ", "
                + TimeZoneInfo.ConvertTime(start, zone).ToString("h:mm tt")
                + "–"
                + TimeZoneInfo.ConvertTime(end, zone).ToString("h:mm tt")
            : "";
}
