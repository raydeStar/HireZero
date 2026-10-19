using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HireZero.Integrations.GitHub;

// The isolated transport for automatic clones: canonical GitHub HTTPS only, no inherited configuration, proxies or
// credential helpers other than the approved one.
public static partial class GitCloneTransportPolicy
{
    public static bool IsClone(string[] args)
    {
        var index = 0;
        while (index + 1 < args.Length && args[index] == "-c")
            index += 2;
        return index < args.Length && args[index] == "clone";
    }

    public static void Configure(ProcessStartInfo start, string[] args, string approvedCredentialHelper)
    {
        var clone = Array.IndexOf(args, "clone");
        if (
            clone < 0
            || args.Length - clone != 7
            || args[clone + 1] != "--template="
            || args[clone + 2] != "--no-checkout"
            || args[clone + 3] != "--no-recurse-submodules"
            || args[clone + 4] != "--"
        )
            throw new ToolFailureException(
                "refused",
                "Automatic clone arguments are outside the approved transport profile.",
                409
            );
        if (!CanonicalRemote().IsMatch(args[^2]))
            throw new ToolFailureException(
                "refused",
                "Automatic clone accepts only the approved canonical GitHub HTTPS repository.",
                409
            );
        // Do not let inherited -c arguments override the isolated transport/credential policy.
        var permitted = new[]
        {
            "core.fsmonitor=false",
            "core.hooksPath=NUL",
            "core.hooksPath=/dev/null",
            "protocol.ext.allow=never",
            "protocol.file.allow=never",
        };
        for (var index = 0; index < clone; index += 2)
            if (
                index + 1 >= clone
                || args[index] != "-c"
                || !permitted.Contains(args[index + 1], StringComparer.Ordinal)
            )
                throw new ToolFailureException(
                    "refused",
                    "Automatic clone inherited an unsupported Git argument.",
                    409
                );

        // Remove environment configuration/rewrites/templates/traces/proxies without retrieving their values.
        foreach (
            var key in start
                .Environment.Keys.Where(key =>
                    key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)
                    || new[] { "http_proxy", "https_proxy", "all_proxy", "no_proxy" }.Contains(
                        key,
                        StringComparer.OrdinalIgnoreCase
                    )
                )
                .ToArray()
        )
            start.Environment.Remove(key);
        var empty = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_SYSTEM"] = empty;
        start.Environment["GIT_CONFIG_GLOBAL"] = empty;
        start.Environment["GIT_CONFIG_COUNT"] = "0";
        start.Environment["GIT_ALLOW_PROTOCOL"] = "https";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "Never";
        start.Environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(Path.GetFullPath(args[^1]))!;
        foreach (
            var setting in new[]
            {
                "protocol.allow=never",
                "protocol.https.allow=always",
                "http.followRedirects=false",
                "http.sslVerify=true",
                "http.proxy=",
                "http.extraHeader=",
                "transfer.bundleURI=false",
                "credential.helper=",
                "credential.helper=" + approvedCredentialHelper,
            }
        )
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(setting);
        }
    }

    // https://github.com/<owner>/<name>.git with GitHub's owner and repository name rules (as GitHubReviewPolicy).
    [GeneratedRegex(@"^https://github\.com/[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}\.git$")]
    private static partial Regex CanonicalRemote();
}
