using System.Diagnostics;
using HireZero.Roles.Developer;

public static class GitCloneTransportChecks
{
    public static void Run(Action<bool, string> check)
    {
        var destination = Path.Combine(Path.GetTempPath(), "fictional-clone-transport");
        check(!GitCloneTransportPolicy.IsClone(["commit", "-m", "clone"]) && GitCloneTransportPolicy.IsClone(["-c", "core.fsmonitor=false", "clone"]), "A commit message cannot be mistaken for the clone command.");
        var args = new[] { "clone", "--template=", "--no-checkout", "--no-recurse-submodules", "--", "https://github.com/owner/example.git", destination };
        var start = new ProcessStartInfo("git");
        start.Environment["GIT_CONFIG_COUNT"] = "1";
        start.Environment["GIT_CONFIG_KEY_0"] = "url.https://outside.invalid/.insteadOf";
        start.Environment["GIT_CONFIG_VALUE_0"] = "https://github.com/";
        start.Environment["GIT_CONFIG_PARAMETERS"] = "fictional inherited rewrite";
        start.Environment["GIT_DIR"] = "fictional foreign repository config";
        start.Environment["GIT_TEMPLATE_DIR"] = "fictional injected template";
        start.Environment["HTTPS_PROXY"] = "http://outside.invalid/";
        GitCloneTransportPolicy.Configure(start, args, "!gh auth git-credential");
        check(start.Environment["GIT_CONFIG_COUNT"] == "0" && !start.Environment.ContainsKey("GIT_CONFIG_KEY_0") && !start.Environment.ContainsKey("GIT_CONFIG_VALUE_0"), "Inherited command-environment rewrites are removed before Git starts.");
        check(!start.Environment.ContainsKey("GIT_CONFIG_PARAMETERS") && !start.Environment.ContainsKey("GIT_DIR") && !start.Environment.ContainsKey("GIT_TEMPLATE_DIR"), "Inherited local-config and template selectors cannot redirect clone configuration.");
        check(start.Environment["GIT_CONFIG_NOSYSTEM"] == "1" && start.Environment["GIT_CONFIG_GLOBAL"] == (OperatingSystem.IsWindows() ? "NUL" : "/dev/null"), "System and global include/rewrite configuration is disabled without retrieving its contents.");
        check(start.Environment["GIT_ALLOW_PROTOCOL"] == "https" && start.ArgumentList.Contains("protocol.allow=never") && start.ArgumentList.Contains("protocol.https.allow=always"), "SSH, HTTP, ext and file rewrites cannot become an allowed transport.");
        check(start.ArgumentList.Contains("http.followRedirects=false") && start.ArgumentList.Contains("http.sslVerify=true"), "Redirects are refused and GitHub certificate verification stays required.");
        check(!start.Environment.ContainsKey("HTTPS_PROXY") && start.ArgumentList.Contains("http.proxy="), "Inherited proxy configuration is excluded from the approved direct transport profile.");
        check(start.ArgumentList.Contains("transfer.bundleURI=false") && start.ArgumentList.Contains("http.extraHeader="), "Alternate bundle fetches and inherited extra-header authority are disabled.");
        check(start.ArgumentList.Contains("credential.helper=") && start.ArgumentList.Contains("credential.helper=!gh auth git-credential"), "Only the existing approved GitHub CLI credential broker is selected; no credential value is retrieved.");
        foreach (var invalid in new[] { "https://outside.invalid/owner/example.git", "ssh://git@github.com/owner/example.git", "https://github.com/owner/example.git?redirect=other" })
        {
            var altered = (string[])args.Clone(); altered[^2] = invalid;
            var refused = false; try { GitCloneTransportPolicy.Configure(new("git"), altered, "!gh auth git-credential"); } catch (ReviewAccessException) { refused = true; }
            check(refused, "Only a canonical fixed-host HTTPS clone URL is accepted.");
        }
        var overrideArgs = new[] { "-c", "http.followRedirects=true" }.Concat(args).ToArray();
        var overrideRefused = false; try { GitCloneTransportPolicy.Configure(new("git"), overrideArgs, "!gh auth git-credential"); } catch (ReviewAccessException) { overrideRefused = true; }
        check(overrideRefused, "An inherited command flag cannot override the redirect policy.");
    }
}
