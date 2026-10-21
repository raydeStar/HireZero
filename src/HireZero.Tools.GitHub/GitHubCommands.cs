using System.Text.RegularExpressions;

namespace HireZero.Tools.GitHub;

// A GitHub CLI (gh) command line, without the executable, and optional standard input.
public sealed record GitHubCommand(string[] Args, string? Input = null);

// Which tool a gh command belongs to. The integration enforces the same classification, so a caller cannot run a
// write through the read tool.
public static partial class GitHubCommands
{
    public static readonly ToolContract<GitHubCommand, string> Read = new(
        "github.read",
        ToolRisk.Read,
        "Read from GitHub: REST GET, GraphQL queries, PR search, view and list, and workflow run logs."
    );

    public static readonly ToolContract<GitHubCommand, string> Write = new(
        "github.write",
        ToolRisk.External,
        "Change GitHub through the REST API (POST, PUT, PATCH, DELETE), such as merging or commenting."
    );

    // The tool for a command, or null when HireZero runs no such gh command.
    public static ToolContract<GitHubCommand, string>? Classify(string[] args, string? input)
    {
        if (args.Length == 0)
            return null;
        switch (args[0])
        {
            case "search":
                return args.Length > 1 && args[1] == "prs" ? Read : null;
            case "pr":
                return args.Length > 1 && args[1] is "view" or "list" ? Read : null;
            case "run":
                return args.Length > 1 && args[1] == "view" ? Read : null;
            case "api":
                if (args.Contains("graphql"))
                    return input is not null && !Mutation().IsMatch(input) ? Read : null;
                var method =
                    Array.IndexOf(args, "--method") is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;
                // An implicit method becomes POST when fields are supplied, so only an explicit GET is a read.
                return method switch
                {
                    "GET" => args.Any(a => a is "-f" or "-F" or "--field" or "--raw-field") ? null : Read,
                    "POST" or "PUT" or "PATCH" or "DELETE" => Write,
                    _ => null,
                };
            default:
                return null;
        }
    }

    [GeneratedRegex(@"\bmutation\b", RegexOptions.IgnoreCase)]
    private static partial Regex Mutation();
}

public sealed record GitHubSignInRequest(string Hostname = "github.com");

// Account is the signed-in GitHub login when the CLI reports one.
public sealed record GitHubSignInStatus(string State, string Code, string Url, string Error, string? Account = null);

public static class GitHubSignInTools
{
    public static readonly ToolContract<GitHubSignInRequest, GitHubSignInStatus> Start = new(
        "github.sign-in",
        ToolRisk.External,
        "Start the GitHub CLI's browser sign-in on this computer; the owner approves it on github.com."
    );

    public static readonly ToolContract<GitHubSignInRequest, GitHubSignInStatus> Status = new(
        "github.sign-in-status",
        ToolRisk.Read,
        "Read the state of a GitHub CLI browser sign-in."
    );
}
