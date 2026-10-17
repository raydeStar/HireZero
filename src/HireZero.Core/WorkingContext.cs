using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HireZero.Core;

// Who is asking decides how much of the owner's work a reply may draw on.
public enum Audience
{
    Owner,
    Boss,
    Visitor,
}

// A project the owner is working on, recognized from the folders and repositories of their sessions.
public sealed record ProjectRef(
    string Key,
    string Name,
    string? Repository,
    string? Root,
    string[] Aliases,
    string LastActive,
    int Sessions,
    GitOrigin? Origin = null
);

public sealed record ContextSection(string Title, string Body);

// Roles add what they know about a project (the developer role: PR fixes and open PRs).
public interface IProjectContextContributor
{
    Task<ContextSection[]> ContributeAsync(ProjectRef project, Audience audience, CancellationToken token);
}

// Assembles what HireZero knows about the projects a message mentions, from the owner's own records:
// coding sessions and their digests, the repository's recent history, role contributions, inbox subjects and memory.
// No model is involved; every piece is cleaned, bounded and handed to a prompt only as fenced data.
public sealed class WorkingContext(
    SessionStore sessions,
    DigestStore digests,
    IntakeStore intake,
    IEnumerable<IProjectContextContributor> contributors,
    MemoryService? memory = null,
    SharingPolicy? sharing = null
)
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(21);
    public const int MaxLength = 9000;
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
    public Func<string, string[], Task<string>> Git { get; init; } = RunGitAsync;

    public ProjectRef[] Projects()
    {
        var since = Clock() - Window;
        return
        [
            .. sessions
                .Recent(200)
                .Where(session => DateTimeOffset.TryParse(session.UpdatedAt, out var at) && at >= since)
                .Select(session => (session, project: Identify(session)))
                .Where(item => item.project is not null)
                .GroupBy(item => item.project!.Value.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First().project!.Value;
                    var root = group
                        .Select(item => item.project!.Value.Root)
                        .Where(path => path is not null)
                        .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(paths => paths.Count())
                        .Select(paths => paths.Key)
                        .FirstOrDefault();
                    return new ProjectRef(
                        first.Key,
                        first.Name,
                        first.Repository,
                        root,
                        Aliases(first.Name, first.Repository),
                        group.Max(item => item.session.UpdatedAt) ?? "",
                        group.Count(),
                        group.Select(item => item.project!.Value.Origin).FirstOrDefault(origin => origin is not null)
                    );
                })
                .OrderByDescending(project => project.LastActive),
        ];
    }

    // Projects named in the text, by repository, folder, or a run of words in their name ("Project" for ExampleWidgetProject).
    public ProjectRef[] Match(string text)
    {
        var flat = Flatten(text);
        return
        [
            .. Projects()
                .Where(project => project.Aliases.Any(alias => flat.Contains(alias, StringComparison.Ordinal)))
                .Take(3),
        ];
    }

    // Projects a boss may hear about: shared organizations only, never hidden ones; with no policy, none.
    public ProjectRef[] SharedWithBoss() => sharing is null ? [] : [.. Projects().Where(sharing.SharesWithBoss)];

    public async Task<string?> ForAsync(string text, Audience audience, CancellationToken token)
    {
        ProjectRef[] projects;
        if (audience == Audience.Boss)
        {
            var shared = SharedWithBoss();
            var named = Match(text).Where(project => shared.Any(item => item.Key == project.Key)).ToArray();
            projects = named.Length > 0 ? named : [.. shared.Take(8)];
        }
        else
            projects = Match(text);
        if (projects.Length == 0)
            return null;
        var output = new StringBuilder();
        foreach (var project in projects)
        {
            output
                .Append("## ")
                .Append(project.Name)
                .Append(project.Repository is null ? "" : " (" + project.Repository + ")")
                .Append('\n');
            foreach (var section in await SectionsAsync(project, audience, token))
                output.Append("### ").Append(section.Title).Append('\n').Append(section.Body.Trim()).Append("\n\n");
            if (output.Length > MaxLength)
                break;
        }
        var lines = new List<string>();
        var withheld = 0;
        foreach (var line in output.ToString().Split('\n'))
        {
            var guarded = ContentGuard.Clean(line, 1200);
            if (guarded.Suspicious)
            {
                withheld++;
                continue;
            }
            lines.Add(guarded.Text);
        }
        if (withheld > 0)
            lines.Add("(" + withheld + " item(s) withheld because they read like instructions to an agent.)");
        var body = string.Join("\n", lines).Trim();
        if (body.Length > MaxLength)
            body = body[..MaxLength];
        return body.Length == 0 ? null : ContentGuard.AsData("working context", new Guarded(body, []));
    }

    public async Task<ContextSection[]> SectionsAsync(ProjectRef project, Audience audience, CancellationToken token)
    {
        var sections = new List<ContextSection>();
        var now = Clock();
        var since = now - Window;
        var mine = sessions
            .Recent(200)
            .Where(session =>
                DateTimeOffset.TryParse(session.UpdatedAt, out var at)
                && at >= since
                && Identify(session)?.Key.Equals(project.Key, StringComparison.OrdinalIgnoreCase) == true
            )
            .ToList();
        if (audience == Audience.Boss)
        {
            // The sanitized work log: which days the project was worked on, plus what roles share for a boss (PRs opened or merged).
            var days = mine.Select(session =>
                    DateTimeOffset.TryParse(session.UpdatedAt, out var at)
                        ? at.ToLocalTime().ToString("ddd yyyy-MM-dd")
                        : null
                )
                .Where(day => day is not null)
                .Distinct()
                .Take(10)
                .ToList();
            sections.Add(
                new(
                    "Activity",
                    $"Worked on in {mine.Count} coding session(s) over the last three weeks; most recent activity {Ago(project.LastActive, now)}."
                        + (days.Count > 0 ? " Days active: " + string.Join(", ", days) + "." : "")
                )
            );
            foreach (var contributor in contributors)
            {
                try
                {
                    sections.AddRange(await contributor.ContributeAsync(project, audience, token));
                }
                catch (Exception error)
                    when (error
                            is WorkspaceException
                                or ModelUnavailableException
                                or InvalidOperationException
                                or IOException
                                or HttpRequestException
                    ) { }
            }
            return [.. sections];
        }
        if (audience == Audience.Visitor)
        {
            // Visitors learn that work is active and how recently, never titles, folders, files or transcripts.
            sections.Add(
                new(
                    "Activity",
                    $"Worked on in {mine.Count} coding session(s) in the last three weeks; most recent activity {Ago(project.LastActive, now)}."
                )
            );
            // Roles decide their own visitor-safe view (for example, PR counts only).
            foreach (var contributor in contributors)
            {
                try
                {
                    sections.AddRange(await contributor.ContributeAsync(project, audience, token));
                }
                catch (Exception error)
                    when (error
                            is WorkspaceException
                                or ModelUnavailableException
                                or InvalidOperationException
                                or IOException
                                or HttpRequestException
                    ) { }
            }
            return [.. sections];
        }

        if (mine.Count > 0)
        {
            var lines = new StringBuilder();
            foreach (var session in mine.Take(10))
            {
                lines
                    .Append("- ")
                    .Append(session.App == "codex" ? "Codex" : "Claude")
                    .Append(" \"")
                    .Append(session.Title)
                    .Append("\" — ")
                    .Append(Label(session.Status))
                    .Append(", ")
                    .Append(Ago(session.UpdatedAt, now));
                if (session.Branch is { Length: > 0 })
                    lines.Append(", branch ").Append(session.Branch);
                if (session.PrUrl is { Length: > 0 })
                    lines.Append(", PR ").Append(session.PrUrl);
                if (digests.Latest(session.Id) is { } digest)
                {
                    lines.Append(
                        $". Last active day: {digest.Turns} asks, {digest.Files.Length} files changed, {digest.Commands} commands ({digest.FailedCommands} failed)"
                    );
                    if (digest.Tests.Length > 0)
                        lines.Append("; tests: ").Append(string.Join("; ", digest.Tests.Take(3)));
                    if (digest.EndedWithQuestion)
                        lines.Append("; it ended with a question for the owner");
                    if (
                        digest.Narrative is { Length: > 0 } narrative
                        && digest.Flags?.Contains(ContentGuard.Injection) != true
                    )
                        lines.Append(". Summary: ").Append(narrative);
                }
                lines.Append('\n');
            }
            sections.Add(new("Coding sessions (most recent first)", lines.ToString()));
        }

        if (project.Root is { } root && Directory.Exists(root))
        {
            try
            {
                var branch = (await Git(root, ["rev-parse", "--abbrev-ref", "HEAD"])).Trim();
                var log = await Git(
                    root,
                    ["log", "--since=21.days", "-n", "15", "--date=short", "--pretty=format:%ad %h %s"]
                );
                var dirty = (await Git(root, ["status", "--porcelain=v1"]))
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Length;
                sections.Add(
                    new(
                        "Repository (local checkout)",
                        "Current branch: "
                            + branch
                            + (dirty > 0 ? $"; {dirty} uncommitted change(s)" : "; clean")
                            + "\nRecent commits:\n"
                            + (log.Trim().Length == 0 ? "none in three weeks" : log.Trim())
                    )
                );
            }
            catch (Exception error)
                when (error
                        is InvalidOperationException
                            or System.ComponentModel.Win32Exception
                            or IOException
                            or OperationCanceledException
                ) { }
        }

        foreach (var contributor in contributors)
        {
            try
            {
                sections.AddRange(await contributor.ContributeAsync(project, audience, token));
            }
            catch (Exception error)
                when (error
                        is WorkspaceException
                            or ModelUnavailableException
                            or InvalidOperationException
                            or IOException
                            or HttpRequestException
                ) { }
        }

        var mentions = intake
            .Recent(200)
            .Where(record =>
                DateTimeOffset.TryParse(record.CreatedAt, out var at)
                && at >= since
                && !record.Dismissed
                && record.DecidedBy != "guard"
                && project.Aliases.Any(alias => Flatten(record.Subject).Contains(alias, StringComparison.Ordinal))
            )
            .Take(8)
            .ToList();
        if (mentions.Count > 0)
            sections.Add(
                new(
                    "Inbox mentions",
                    string.Join(
                        "\n",
                        mentions.Select(record =>
                            "- "
                            + record.Subject
                            + " (from "
                            + record.From
                            + ", "
                            + Ago(record.CreatedAt, now)
                            + ", "
                            + record.Verdict
                            + ")"
                        )
                    )
                )
            );

        if (memory is not null)
        {
            try
            {
                var hits = await memory.SearchAsync(project.Name, 4, token);
                var useful = hits.Where(hit => !hit.Flags.Contains(ContentGuard.Injection)).Take(4).ToList();
                if (useful.Count > 0)
                    sections.Add(
                        new(
                            "From memory",
                            string.Join("\n", useful.Select(hit => "- " + hit.Snippet.ReplaceLineEndings(" ")))
                        )
                    );
            }
            catch (Exception error) when (error is WorkspaceException or ModelUnavailableException or IOException) { }
        }
        return [.. sections];
    }

    // Project identity for a session: its repository from the checkout's git origin, else its folder. Worktrees fold into their repository.
    public static (string Key, string Name, string? Repository, string? Root, GitOrigin? Origin)? Identify(
        AgentSession session
    )
    {
        var root = RootOf(session.Cwd);
        var origin = GitRemote.Origin(root);
        var repository = origin?.Slug ?? (session.Repository is { } named && named.Contains('/') ? named : null);
        var name =
            origin?.Name
            ?? repository?.Split('/')[1]
            ?? (root is null ? session.Repository : Path.GetFileName(root.TrimEnd('\\', '/')));
        if (
            string.IsNullOrWhiteSpace(name)
            || name.Length < 3
            || Regex.IsMatch(name, "^[0-9a-f]{16,}$", RegexOptions.None, TimeSpan.FromMilliseconds(50))
        )
            return null;
        return ((repository ?? name).ToLowerInvariant(), name, repository, root, origin);
    }

    // C:\repo\.worktrees\abc or C:\repo\.claude\worktrees\abc -> C:\repo
    public static string? RootOf(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return null;
        var path = cwd.Replace('/', '\\').TrimEnd('\\');
        foreach (var marker in new[] { "\\.claude\\worktrees\\", "\\.worktrees\\" })
        {
            var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
                return path[..index];
        }
        return path;
    }

    public static string[] Aliases(string name, string? repository)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal) { Flatten(name) };
        if (repository is not null)
            aliases.Add(Flatten(repository.Split('/')[1]));
        var words = Regex
            .Matches(name, "[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+", RegexOptions.None, TimeSpan.FromMilliseconds(50))
            .Select(match => match.Value.ToLowerInvariant())
            .ToList();
        for (var start = 0; start < words.Count; start++)
        for (var end = start + 1; end < words.Count; end++)
        {
            var run = string.Concat(words.Skip(start).Take(end - start + 1));
            if (run.Length >= 6)
                aliases.Add(run);
        }
        return [.. aliases.Where(alias => alias.Length >= 4)];
    }

    static string Flatten(string text) => new([.. text.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    static string Label(string status) =>
        status switch
        {
            "needs-you" => "waiting on the owner's approval",
            "working" => "working now",
            "waiting" => "finished, waiting on the owner",
            "ended" => "ended",
            _ => "idle",
        };

    static string Ago(string at, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(at, out var time))
            return "recently";
        var span = now - time;
        return span.TotalMinutes < 90 ? "within the last hour"
            : span.TotalHours < 24 ? $"{(int)span.TotalHours} hours ago"
            : span.TotalDays < 2 ? "yesterday"
            : $"{(int)span.TotalDays} days ago";
    }

    static async Task<string> RunGitAsync(string root, string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git could not start.");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        _ = process.StandardError.ReadToEndAsync(limit.Token);
        await process.WaitForExitAsync(limit.Token);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("git failed.");
        var text = await output;
        return text.Length > 6000 ? text[..6000] : text;
    }
}
