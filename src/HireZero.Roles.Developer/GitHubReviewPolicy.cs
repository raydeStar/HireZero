using System.Text.RegularExpressions;

namespace HireZero.Roles.Developer;

// Names and paths the Developer accepts from GitHub before using them in a tool call.
public static partial class GitHubReviewPolicy
{
    public static bool ValidRepository(string? name) => name is not null && RepositoryPattern().IsMatch(name);

    public static bool Sha(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    // A changed file's repository-relative path: no absolute, parent, drive, backslash or .git segment.
    public static bool SafePath(string path) =>
        path is { Length: > 0 and <= 400 }
        && !path.StartsWith('/')
        && !path.Contains('\\')
        && !path.Contains(':')
        && !path.Split('/').Any(p => p is "" or "." or ".." || p.Equals(".git", StringComparison.OrdinalIgnoreCase))
        && !path.StartsWith(".developer-workspace-evidence/", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}$")]
    private static partial Regex RepositoryPattern();
}
