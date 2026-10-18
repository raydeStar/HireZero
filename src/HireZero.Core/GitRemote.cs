using System.Text.RegularExpressions;

namespace HireZero.Core;

// Where a checkout's origin lives. Host is "github" or the host an integration's origin format names; Organization is
// the owner or organization and Project an optional grouping. AbbreviatedOrganization marks hosts whose organization
// names are often short forms of a company name. Never carries credentials.
public sealed record GitOrigin(
    string Host,
    string Organization,
    string? Project,
    string Name,
    bool AbbreviatedOrganization = false
)
{
    public const string GitHub = "github";

    // The short form used across HireZero: owner/name, or project/name where the host groups repositories.
    public string Slug => (Project ?? Organization) + "/" + Name;
}

// A remote URL format an integration understands, such as a hosted Git service that groups repositories in projects.
public interface IGitOriginFormat
{
    GitOrigin? Parse(string url);
}

// The origin of a checkout, read from its git config (worktrees follow their commondir).
public static class GitRemote
{
    static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IGitOriginFormat> Formats = new();

    // Integrations register the formats they understand; anything else is read GitHub-style.
    public static void Register(IGitOriginFormat format) => Formats.TryAdd(format.GetType(), format);

    public static string? Repository(string? folder) => Origin(folder)?.Slug;

    public static GitOrigin? Origin(string? folder)
    {
        try
        {
            if (folder is not { Length: > 0 } || !Directory.Exists(folder))
                return null;
            var dotGit = Path.Combine(folder, ".git");
            string? gitDir = null;
            if (Directory.Exists(dotGit))
                gitDir = dotGit;
            else if (
                File.Exists(dotGit)
                && File.ReadAllText(dotGit).Trim() is var pointer
                && pointer.StartsWith("gitdir:", StringComparison.Ordinal)
            )
            {
                var worktree = Path.GetFullPath(Path.Combine(folder, pointer[7..].Trim()));
                var common = Path.Combine(worktree, "commondir");
                gitDir = File.Exists(common)
                    ? Path.GetFullPath(Path.Combine(worktree, File.ReadAllText(common).Trim()))
                    : worktree;
            }
            if (gitDir is null || !File.Exists(Path.Combine(gitDir, "config")))
                return null;
            var config = File.ReadAllText(Path.Combine(gitDir, "config"));
            var origin = Regex.Match(config, @"\[remote ""origin""\][^\[]*?url\s*=\s*(\S+)", RegexOptions.None, Limit);
            return origin.Success ? Parse(origin.Groups[1].Value) : null;
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException or RegexMatchTimeoutException
            )
        {
            return null;
        }
    }

    public static GitOrigin? Parse(string url)
    {
        try
        {
            foreach (var format in Formats.Values)
                if (format.Parse(url) is { } known)
                    return known;
            var github = Regex.Match(
                url,
                @"[:/]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(\.git)?/?$",
                RegexOptions.None,
                Limit
            );
            // An owner starting with "_" is a path segment of another host's format, never a GitHub owner.
            return github.Success && !github.Groups[1].Value.StartsWith('_')
                ? new(GitOrigin.GitHub, github.Groups[1].Value, null, github.Groups[2].Value)
                : null;
        }
        catch (Exception error) when (error is RegexMatchTimeoutException or ArgumentException)
        {
            return null;
        }
    }

    // Names may be URL-encoded ("North%20Platform"); anything that could split a path is refused.
    public static string DecodeName(string value)
    {
        var decoded = Uri.UnescapeDataString(value).Trim();
        if (decoded.Length == 0 || decoded.Length > 200 || decoded.IndexOfAny(['/', '\\', '?', '#', '\r', '\n']) >= 0)
            throw new ArgumentException("Unexpected name in a remote URL.");
        return decoded;
    }
}
