using System.Diagnostics;

namespace HireZero.Roles.Developer;

public static class GitCloneTransportPolicy
{
    public static bool IsClone(string[] args)
    {
        var index = 0;
        while (index + 1 < args.Length && args[index] == "-c") index += 2;
        return index < args.Length && args[index] == "clone";
    }
    public static void Configure(ProcessStartInfo start, string[] args, string approvedCredentialHelper)
    {
        var clone = Array.IndexOf(args, "clone");
        if (clone < 0 || args.Length - clone != 7 || args[clone + 1] != "--template=" || args[clone + 2] != "--no-checkout" ||
            args[clone + 3] != "--no-recurse-submodules" || args[clone + 4] != "--")
            throw new ReviewAccessException("Automatic clone arguments are outside the approved transport profile.", 409);
        var repository = GitHubReviewPolicy.RemoteRepository(args[^2]);
        if (repository is null || args[^2] != "https://github.com/" + repository + ".git")
            throw new ReviewAccessException("Automatic clone accepts only the approved canonical GitHub HTTPS repository.", 409);
        // Do not let inherited -c arguments override the isolated transport/credential policy.
        var permitted = new[] { "core.fsmonitor=false", "core.hooksPath=NUL", "core.hooksPath=/dev/null", "protocol.ext.allow=never", "protocol.file.allow=never" };
        for (var index = 0; index < clone; index += 2)
            if (index + 1 >= clone || args[index] != "-c" || !permitted.Contains(args[index + 1], StringComparer.Ordinal))
                throw new ReviewAccessException("Automatic clone inherited an unsupported Git argument.", 409);

        // Remove environment configuration/rewrites/templates/traces/proxies without retrieving their values.
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
            new[] { "http_proxy", "https_proxy", "all_proxy", "no_proxy" }.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray())
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
        foreach (var setting in new[] { "protocol.allow=never", "protocol.https.allow=always", "http.followRedirects=false",
            "http.sslVerify=true", "http.proxy=", "http.extraHeader=", "transfer.bundleURI=false", "credential.helper=",
            "credential.helper=" + approvedCredentialHelper })
        { start.ArgumentList.Add("-c"); start.ArgumentList.Add(setting); }
    }
}
