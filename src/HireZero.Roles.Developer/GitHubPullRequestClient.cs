using System.Text.Json;

namespace HireZero.Roles.Developer;

public partial class GitHubReviewClient
{
    public virtual async Task<PullRequestEvidence> EvidenceAsync(string repository, int number, bool logs, CancellationToken token)
    {
        var identity = await IdentityAsync(token);
        var remote = await RepositoryAsync(repository, token);
        if (number <= 0) throw new ReviewAccessException("Choose a valid PR number.", 400);
        var prefix = "repos/" + remote.Name;
        var pr = await ApiAsync(prefix + "/pulls/" + number, token);
        var metadata = await ApiAsync(prefix, token);
        var review = await ReviewAsync(new("", identity.Id, remote.Id, remote.Name, "", ""), number, token);
        var sha = pr.GetProperty("head").GetProperty("sha").GetString()!;
        if (!PullRequestPolicy.Sha(sha) || sha != review.HeadSha) throw new ReviewAccessException("The PR changed during retrieval. Refresh its evidence.");
        var checkData = await ApiAsync(prefix + "/commits/" + sha + "/check-runs?filter=latest&per_page=100", token);
        var statusData = await ApiAsync(prefix + "/commits/" + sha + "/status?per_page=100", token);
        var reviewData = await ApiAsync(prefix + "/pulls/" + number + "/reviews?per_page=100", token);
        var files = await ApiAsync(prefix + "/pulls/" + number + "/files?per_page=100", token);
        var discussion = await ApiAsync(prefix + "/issues/" + number + "/comments?per_page=100", token);
        var checks = new List<PullRequestCheck>(); var limitations = new List<string>();
        var workflowPaths = new HashSet<string>(StringComparer.Ordinal); var nodeFailure = false;
        var failureLogs = new System.Text.StringBuilder();
        var complete = review.Complete && review.Threads.All(thread => thread.Complete) &&
            checkData.GetProperty("total_count").GetInt32() <= 100 && statusData.GetProperty("total_count").GetInt32() < 100 &&
            reviewData.GetArrayLength() < 100 && files.GetArrayLength() == pr.GetProperty("changed_files").GetInt32();
        foreach (var check in checkData.GetProperty("check_runs").EnumerateArray())
        {
            var conclusion = Text(check, "conclusion");
            var state = Text(check, "status") != "completed" ? "pending" : conclusion == "success" ? "success" : conclusion ?? "unknown";
            string? evidence = null; string? jobUrl = null; long? runId = null; int? runAttempt = null;
            var outputRevision = PullRequestPolicy.Hash(check.GetProperty("output").GetRawText());
            var detailsUrl = Text(check, "details_url");
            // Provenance is retrieved even when log excerpts are omitted; approval snapshots must match both read paths.
            if (Uri.TryCreate(detailsUrl, UriKind.Absolute, out var jobUri) && jobUri.Scheme == "https" && jobUri.Host == "github.com")
            {
                var parts = jobUri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length == 7 && (parts[0] + "/" + parts[1]).Equals(remote.Name, StringComparison.OrdinalIgnoreCase) &&
                    parts[2] == "actions" && parts[3] == "runs" && long.TryParse(parts[4], out var selectedRun) && selectedRun > 0 &&
                    parts[5] == "job" && long.TryParse(parts[6], out var selectedJob) && selectedJob > 0)
                {
                    jobUrl = jobUri.GetLeftPart(UriPartial.Path); runId = selectedRun;
                    try
                    {
                        var metadataRun = await ApiAsync(prefix + "/actions/runs/" + selectedRun, token);
                        if (!metadataRun.TryGetProperty("head_sha", out var runHead) || runHead.ValueKind != JsonValueKind.String ||
                            !PullRequestPolicy.Sha(runHead.GetString()) || runHead.GetString() != sha) complete = false;
                        if (metadataRun.TryGetProperty("run_attempt", out var attempt) && attempt.TryGetInt32(out var value) && value > 0) runAttempt = value;
                        else complete = false;
                    }
                    catch (ReviewAccessException) { complete = false; limitations.Add("The exact CI run attempt could not be verified."); }
                }
            }
            if (logs && state != "success" && checks.Count(item => item.Evidence is not null) < 2)
            {
                var output = check.GetProperty("output");
                evidence = PullRequestPolicy.Safe(Text(output, "summary") + "\n" + Text(output, "text"), 4000);
                try
                {
                    var annotations = await ApiAsync(prefix + "/check-runs/" + check.GetProperty("id").GetInt64() + "/annotations?per_page=30", token);
                    evidence += "\n" + PullRequestPolicy.Safe(string.Join('\n', annotations.EnumerateArray().Select(a =>
                        Text(a, "path") + ":" + a.GetProperty("start_line") + " " + Text(a, "message"))), 4000);
                    var details = Text(check, "details_url");
                    // Only a verified GitHub Actions job for this exact repository may supply logs.
                    if (Uri.TryCreate(details, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com")
                    {
                        var segments = uri.AbsolutePath.Trim('/').Split('/');
                        if (segments.Length == 7 && (segments[0] + "/" + segments[1]).Equals(remote.Name, StringComparison.OrdinalIgnoreCase) &&
                            segments[2] == "actions" && segments[3] == "runs" && long.TryParse(segments[4], out _) &&
                            segments[5] == "job" && long.TryParse(segments[6], out var job) && job > 0)
                        {
                            jobUrl = uri.GetLeftPart(UriPartial.Path);
                            var log = await CommandAsync(Executable, null, ["run", "view", "--repo", remote.Name, "--job", job.ToString(), "--log-failed"], null, token);
                            failureLogs.AppendLine(PullRequestPolicy.Safe(log, 1_000_000));
                            evidence += "\nJob: " + uri.GetLeftPart(UriPartial.Path) + "\n" + PullRequestPolicy.FailureExcerpt(log);
                            nodeFailure |= log.Contains("npm", StringComparison.OrdinalIgnoreCase) || log.Contains("pnpm", StringComparison.OrdinalIgnoreCase);
                            try
                            {
                                var run = await ApiAsync(prefix + "/actions/runs/" + segments[4], token);
                                var workflow = Text(run, "path")?.Split('@')[0];
                                if (workflow is not null && workflow.StartsWith(".github/workflows/", StringComparison.Ordinal) &&
                                    GitHubPublication.SafePath(workflow) && Path.GetExtension(workflow) is ".yml" or ".yaml")
                                    workflowPaths.Add(workflow);
                            }
                            catch (ReviewAccessException) { limitations.Add("The failing job's workflow metadata is unavailable."); }
                        }
                    }
                }
                catch (ReviewAccessException) { limitations.Add("Some failure annotations or job logs are unavailable for " + PullRequestPolicy.Safe(Text(check, "name"), 100) + "."); }
            }
            checks.Add(new(PullRequestPolicy.Safe(Text(check, "name"), 140), state, evidence) { JobUrl = jobUrl,
                EventId = check.GetProperty("id").GetInt64().ToString(), UpdatedAt = Text(check, "completed_at") ?? Text(check, "started_at"),
                RunId = runId, RunAttempt = runAttempt, EvidenceRevision = outputRevision });
        }
        checks.AddRange(statusData.GetProperty("statuses").EnumerateArray().GroupBy(status => Text(status, "context"))
            .Select(group => group.OrderByDescending(status => Text(status, "updated_at")).First())
            .Select(status => new PullRequestCheck(PullRequestPolicy.Safe(Text(status, "context"), 140), Text(status, "state") ?? "unknown", null) {
                EventId = status.TryGetProperty("id", out var eventId) ? eventId.ToString() : null,
                UpdatedAt = Text(status, "updated_at"), JobUrl = Text(status, "target_url"),
                EvidenceRevision = PullRequestPolicy.Hash(status.GetRawText()) }));
        var effectiveReviews = reviewData.EnumerateArray().Where(r => Text(r, "state") is "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED")
            .GroupBy(r => r.GetProperty("user").GetProperty("id").GetInt64()).Select(group => group.OrderByDescending(r => r.GetProperty("id").GetInt64()).First())
            .Select(r => new PullRequestApproval(Text(r.GetProperty("user"), "login")!, Text(r.GetProperty("user"), "type")!, Text(r, "state")!, Text(r, "commit_id")!)).ToArray();
        if (!complete) limitations.Add("PR, check, review or file retrieval exceeds the bounded page limit; write actions are blocked.");
        if (discussion.GetArrayLength() >= 100) limitations.Add("Discussion is a bounded excerpt.");
        var head = pr.GetProperty("head"); var headRepo = head.GetProperty("repo");
        var contextFiles = new List<PullRequestFile>();
        var contextPaths = workflowPaths.Take(2).ToList();
        if (nodeFailure) contextPaths.Add("package.json");
        foreach (var path in contextPaths)
        {
            var source = await SourceExcerptAsync(prefix, path, sha, 6000, token);
            if (source is not null) contextFiles.Add(new(path, "context", "", source));
            else limitations.Add("CI context is unavailable at the PR head: " + path + ".");
        }
        var fileEvidence = new List<PullRequestFile>();
        foreach (var file in files.EnumerateArray())
        {
            var path = Text(file, "filename")!; var source = "";
            var name = Path.GetFileName(path).ToLowerInvariant();
            if (logs && fileEvidence.Count < 6 && GitHubPublication.SafePath(path) && Text(file, "status") != "removed" &&
                !name.StartsWith(".env") && !name.StartsWith("appsettings") && !name.Contains("secret") &&
                !name.Contains("credential") && !new[] { ".pem", ".pfx", ".key", ".p12" }.Contains(Path.GetExtension(name)))
            {
                try
                {
                    var content = await ApiAsync(prefix + "/contents/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString)) + "?ref=" + sha, token);
                    if (Text(content, "encoding") == "base64" && content.GetProperty("size").GetInt64() <= 100_000)
                        source = PullRequestPolicy.Safe(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Text(content, "content")!)), 4000);
                }
                catch (Exception error) when (error is ReviewAccessException or FormatException) { source = "Source excerpt unavailable; inspect the checkout before choosing the final fix."; }
            }
            fileEvidence.Add(new(path, Text(file, "status")!, PullRequestPolicy.Safe(Text(file, "patch"), 2500), source));
        }
        var result = new PullRequestEvidence(remote.Name, remote.Id, number, PullRequestPolicy.Safe(Text(pr, "title"), 180), sha, review.Author,
            review.Open, pr.GetProperty("draft").GetBoolean(), headRepo.ValueKind == JsonValueKind.Null ? "" : Text(headRepo, "full_name")!,
            review.HeadRepositoryId, Text(head, "ref")!, Text(metadata, "default_branch")!, Text(pr, "mergeable_state") ?? "unknown", complete,
            [.. checks], review.Threads.Select(t => t with { Comments = t.Comments.Select(c => c with { Body = PullRequestPolicy.Safe(c.Body) }).ToArray() }).ToArray(), effectiveReviews,
            [.. fileEvidence],
            [PullRequestPolicy.Safe(Text(pr, "body"), 2500), .. discussion.EnumerateArray().TakeLast(8).Select(c => PullRequestPolicy.Safe(Text(c, "body"), 1000))],
            [.. limitations], pr.GetProperty("labels").EnumerateArray().Select(l => PullRequestPolicy.Safe(Text(l, "name"), 100)).ToArray())
            { ContextFiles = [.. contextFiles] };
        if (logs && nodeFailure)
        {
            var extra = new List<PullRequestEvidenceRequest> { new("file", "package-lock.json") };
            extra.AddRange(AdvisoryIds(failureLogs.ToString()).Select(id => new PullRequestEvidenceRequest("advisory", id)));
            result = await GatherEvidenceAsync(result, [..extra.Take(4)], token);
        }
        return result;
    }
    private async Task<string?> SourceExcerptAsync(string prefix, string path, string sha, int limit, CancellationToken token)
    {
        try
        {
            var content = await ApiAsync(prefix + "/contents/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString)) + "?ref=" + sha, token);
            if (Text(content, "encoding") != "base64" || content.GetProperty("size").GetInt64() > 100_000) return null;
            var source = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Text(content, "content")!));
            return PullRequestPolicy.Safe(string.Join('\n', source.Split('\n').Select((line, i) => (i + 1) + ": " + line)), limit);
        }
        catch (Exception error) when (error is ReviewAccessException or FormatException) { return null; }
    }
    public async Task MergeAsync(PullRequestEvidence evidence, CancellationToken token)
    {
        var input = JsonSerializer.Serialize(new { sha = evidence.HeadSha, merge_method = "squash" });
        var result = await JsonAsync(["api", "--hostname", "github.com", "--method", "PUT", "repos/" + evidence.Repository + "/pulls/" + evidence.Number + "/merge", "--input", "-"], input, token);
        if (!result.TryGetProperty("merged", out var merged) || !merged.GetBoolean()) throw new ReviewAccessException("GitHub did not confirm a merge. Refresh the PR before any further action.");
    }
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
