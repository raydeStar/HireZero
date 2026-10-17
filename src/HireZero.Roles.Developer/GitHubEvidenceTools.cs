using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Roles.Developer;

public partial class GitHubReviewClient
{
    public async Task<PullRequestEvidence> GatherEvidenceAsync(
        PullRequestEvidence evidence,
        PullRequestEvidenceRequest[] requests,
        CancellationToken token
    )
    {
        var context = evidence.ContextFiles.ToList();
        var limitations = evidence.Limitations.ToList();
        foreach (var request in requests.Distinct().Take(4))
        {
            var reference = request.Kind + ":" + request.Value + ":" + request.StartLine;
            string? content = null;
            try
            {
                if (request.StartLine is < 1 or > 100000)
                    throw new ReviewAccessException("Invalid evidence range.");
                if (request.Kind == "file" && AllowedEvidencePath(request.Value))
                {
                    var data = await ApiAsync(
                        "repos/"
                            + evidence.Repository
                            + "/contents/"
                            + string.Join('/', request.Value.Split('/').Select(Uri.EscapeDataString))
                            + "?ref="
                            + evidence.HeadSha,
                        token
                    );
                    if (Text(data, "encoding") == "base64" && data.GetProperty("size").GetInt64() <= 600_000)
                    {
                        var source = Encoding.UTF8.GetString(Convert.FromBase64String(Text(data, "content")!));
                        content =
                            Path.GetFileName(request.Value) == "package-lock.json" && request.StartLine == 1
                                ? LockfileEvidence(source, evidence)
                                : Lines(source, request.StartLine);
                    }
                }
                else if (request.Kind == "advisory" && AdvisoryId(request.Value))
                {
                    var advisory = await ApiAsync("advisories/" + request.Value, token);
                    content = PullRequestPolicy.Safe(
                        JsonSerializer.Serialize(
                            new
                            {
                                reference = "https://github.com/advisories/" + request.Value,
                                summary = Text(advisory, "summary"),
                                severity = Text(advisory, "severity"),
                                vulnerabilities = advisory
                                    .GetProperty("vulnerabilities")
                                    .EnumerateArray()
                                    .Take(5)
                                    .Select(item => new
                                    {
                                        package = Text(item.GetProperty("package"), "name"),
                                        ecosystem = Text(item.GetProperty("package"), "ecosystem"),
                                        affected = Text(item, "vulnerable_version_range"),
                                        firstPatchedVersion = Text(item, "first_patched_version"),
                                    }),
                            }
                        ),
                        5000
                    );
                }
                else if (
                    request.Kind == "log"
                    && long.TryParse(request.Value, out var job)
                    && job > 0
                    && evidence.Checks.Any(check => VerifiedJob(check.JobUrl, evidence.Repository, job))
                )
                {
                    var log = await CommandAsync(
                        Executable,
                        null,
                        ["run", "view", "--repo", evidence.Repository, "--job", job.ToString(), "--log-failed"],
                        null,
                        token
                    );
                    content = Lines(log, request.StartLine);
                }
            }
            catch (Exception error)
                when (error is ReviewAccessException or JsonException or FormatException or InvalidOperationException)
            {
                content = null;
            }
            if (content is null)
            {
                content =
                    "The read-only GitHub evidence tool could not retrieve this resource or it is outside its allowed scope. Do not ask the owner to paste credentials or private files.";
                limitations.Add("Evidence unavailable: " + reference + ".");
            }
            context.RemoveAll(file => file.Path == reference);
            context.Add(new(reference, "supporting-evidence", "", content));
        }
        return evidence with { ContextFiles = [.. context], Limitations = limitations.Distinct().Take(16).ToArray() };
    }

    public static bool AllowedEvidencePath(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return GitHubPublication.SafePath(path)
            && !name.StartsWith(".env")
            && !name.StartsWith("appsettings")
            && !name.Contains("secret")
            && !name.Contains("credential")
            && !name.Contains("creds")
            && !name.StartsWith("kubeconfig")
            && name is not (".npmrc" or ".pypirc" or ".netrc" or "id_rsa" or "id_ed25519")
            && name
                is not (
                    "auth-profiles.json"
                    or "auth.json"
                    or "config.json"
                    or "accesstokens.json"
                    or "azureprofile.json"
                )
            && !path.Split('/').Any(part => part.ToLowerInvariant() is ".aws" or ".azure" or ".ssh" or ".kube")
            && !new[] { ".pem", ".pfx", ".key", ".p12", ".keystore" }.Contains(Path.GetExtension(name));
    }

    public static bool AdvisoryId(string value) =>
        Regex.IsMatch(value, "^GHSA-[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}$", RegexOptions.CultureInvariant);

    public static string[] AdvisoryIds(string log) =>
        Regex
            .Matches(log, "GHSA-[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}", RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Distinct()
            .Take(3)
            .ToArray();

    private static bool VerifiedJob(string? text, string repository, long job) =>
        text is not null
        && Regex
            .Matches(text, "https://github\\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/actions/runs/[0-9]+/job/[0-9]+")
            .Any(match =>
                Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)
                && uri.AbsolutePath.StartsWith("/" + repository + "/actions/runs/", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.EndsWith("/job/" + job, StringComparison.Ordinal)
            );

    private static string Lines(string source, int startLine) =>
        PullRequestPolicy.Safe(
            string.Join(
                '\n',
                PullRequestPolicy
                    .Safe(source, 1_000_000)
                    .Split('\n')
                    .Select((line, i) => (Line: i + 1, Text: line))
                    .Skip(startLine - 1)
                    .Take(100)
                    .Select(row => row.Line + ": " + row.Text)
            ),
            5000
        );

    private static string LockfileEvidence(string source, PullRequestEvidence evidence)
    {
        using var lockfile = JsonDocument.Parse(source);
        if (!lockfile.RootElement.TryGetProperty("packages", out var packages))
            return Lines(source, 1);
        var hints = string.Join('\n', evidence.Checks.Select(check => check.Evidence));
        var direct = new HashSet<string>(StringComparer.Ordinal);
        if (packages.TryGetProperty("", out var root))
            foreach (var group in new[] { "dependencies", "devDependencies" })
                if (root.TryGetProperty(group, out var dependencies))
                    foreach (var item in dependencies.EnumerateObject())
                        direct.Add(item.Name);
        var lines = source.Split('\n');
        int? Line(string path)
        {
            var index = Array.FindIndex(
                lines,
                line => line.Contains(JsonSerializer.Serialize(path) + ":", StringComparison.Ordinal)
            );
            return index >= 0 ? index + 1 : null;
        }
        var selected = packages
            .EnumerateObject()
            .Where(item => item.Name.StartsWith("node_modules/", StringComparison.Ordinal))
            .Select(item => new
            {
                item.Name,
                Package = item.Name[(item.Name.LastIndexOf("node_modules/", StringComparison.Ordinal) + 13)..],
                Value = item.Value,
            })
            .Where(item =>
                item.Package.Length >= 3
                && (direct.Contains(item.Package) || hints.Contains(item.Package, StringComparison.OrdinalIgnoreCase))
            )
            .OrderByDescending(item => hints.Contains(item.Package, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .Select(item => new
            {
                path = item.Name,
                line = Line(item.Name),
                version = Text(item.Value, "version"),
            })
            .ToArray();
        return PullRequestPolicy.Safe(
            "Selected resolved dependency blocks at the exact PR head; this is not the complete lockfile. "
                + "Use a file evidence request with StartLine to inspect a specific block.\n"
                + JsonSerializer.Serialize(selected),
            5000
        );
    }
}
