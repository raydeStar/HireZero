using System.Text.RegularExpressions;

namespace HireZero.Core;

// Where a checkout's origin lives. GitHub: Organization is the owner, Project is null. Azure DevOps: organization,
// project and repository (URL-decoded). Never carries credentials.
public sealed record GitOrigin(string Host, string Organization, string? Project, string Name)
{
    public const string GitHub = "github",
        AzureDevOps = "azure-devops";

    // The short form used across HireZero: owner/name on GitHub, project/name on Azure DevOps.
    public string Slug => (Project ?? Organization) + "/" + Name;
}

// The origin of a checkout, read from its git config (worktrees follow their commondir).
public static class GitRemote
{
    static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);

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
            // https://[user@]dev.azure.com/org/project/_git/repo, git@ssh.dev.azure.com:v3/org/project/repo, https://org.visualstudio.com/[DefaultCollection/]project/_git/repo
            var devops = Regex.Match(
                url,
                @"dev\.azure\.com/([^/]+)/([^/]+)/_git/([^/?#]+?)(?:\.git)?/?$",
                RegexOptions.IgnoreCase,
                Limit
            );
            if (!devops.Success)
                devops = Regex.Match(
                    url,
                    @"ssh\.dev\.azure\.com:v3/([^/]+)/([^/]+)/([^/?#]+?)/?$",
                    RegexOptions.IgnoreCase,
                    Limit
                );
            if (devops.Success)
                return new(
                    GitOrigin.AzureDevOps,
                    Decode(devops.Groups[1].Value),
                    Decode(devops.Groups[2].Value),
                    Decode(devops.Groups[3].Value)
                );
            var legacy = Regex.Match(
                url,
                @"//(?:[^@/]+@)?([A-Za-z0-9-]+)\.visualstudio\.com/(?:DefaultCollection/)?([^/]+)/_git/([^/?#]+?)/?$",
                RegexOptions.IgnoreCase,
                Limit
            );
            if (legacy.Success)
                return new(
                    GitOrigin.AzureDevOps,
                    legacy.Groups[1].Value,
                    Decode(legacy.Groups[2].Value),
                    Decode(legacy.Groups[3].Value)
                );
            var github = Regex.Match(
                url,
                @"[:/]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(\.git)?/?$",
                RegexOptions.None,
                Limit
            );
            return github.Success ? new(GitOrigin.GitHub, github.Groups[1].Value, null, github.Groups[2].Value) : null;
        }
        catch (Exception error) when (error is RegexMatchTimeoutException or ArgumentException)
        {
            return null;
        }
    }

    // Names may be URL-encoded ("exam%20DevOps%20Platform"); anything that could split a path is refused.
    static string Decode(string value)
    {
        var decoded = Uri.UnescapeDataString(value).Trim();
        if (decoded.Length == 0 || decoded.Length > 200 || decoded.IndexOfAny(['/', '\\', '?', '#', '\r', '\n']) >= 0)
            throw new ArgumentException("Unexpected name in a remote URL.");
        return decoded;
    }
}
