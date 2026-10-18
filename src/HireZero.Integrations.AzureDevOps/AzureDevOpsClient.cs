using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Integrations.AzureDevOps;

public sealed record AdoPullRequest(
    int Id,
    string Title,
    string Organization,
    string Project,
    string ProjectId,
    string Repository,
    string RepositoryId,
    string Source,
    string Target,
    bool Draft,
    string MergeStatus,
    string Author,
    string AuthorId,
    string Created,
    string? Closed,
    int[] Votes
)
{
    public string Url =>
        "https://dev.azure.com/"
        + Uri.EscapeDataString(Organization)
        + "/"
        + Uri.EscapeDataString(Project)
        + "/_git/"
        + Uri.EscapeDataString(Repository)
        + "/pullrequest/"
        + Id;
}

public sealed record AdoAttention(
    AdoPullRequest PullRequest,
    string[] FailingPolicies,
    string[] RunningPolicies,
    int OpenThreads,
    string[] Problems
);

// Azure DevOps through the owner's Azure CLI sign-in: `az account get-access-token` with fixed arguments gives a
// short-lived token for Azure DevOps, kept in memory only (never logged or stored) until shortly before it expires.
// Requests go straight to the REST API over HTTPS.
public sealed class AzureDevOpsClient(IHttpClientFactory clients)
{
    public const string Resource = "499b84ac-1321-427f-aa17-267ca6975798";
    static readonly Regex Organization = new(
        "^[A-Za-z0-9][A-Za-z0-9-]{0,63}$",
        RegexOptions.None,
        TimeSpan.FromMilliseconds(50)
    );
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private (string Value, DateTimeOffset Expires)? _token;
    private readonly ConcurrentDictionary<string, (string Id, string Name)> _me = new(StringComparer.OrdinalIgnoreCase);
    public Func<CancellationToken, Task<(string Value, DateTimeOffset Expires)>> TokenSource { get; init; } =
        token => AzureCliTokenAsync(null, token);

    // Tokens from a chosen Azure CLI (the company workspace names it in azure-devops.json); null uses the default.
    public static Func<CancellationToken, Task<(string Value, DateTimeOffset Expires)>> AzureCli(string? command) =>
        token => AzureCliTokenAsync(command, token);

    public static bool ValidOrganization(string organization) => Organization.IsMatch(organization);

    private async Task<string> TokenAsync(CancellationToken token)
    {
        if (_token is { } cached && cached.Expires - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(5))
            return cached.Value;
        await _tokenGate.WaitAsync(token);
        try
        {
            if (_token is { } again && again.Expires - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(5))
                return again.Value;
            _token = await TokenSource(token);
            return _token.Value.Value;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    static async Task<(string Value, DateTimeOffset Expires)> AzureCliTokenAsync(
        string? chosen,
        CancellationToken token
    )
    {
        var command =
            chosen
            ?? Environment.GetEnvironmentVariable("HIREZERO_AZ")
            ?? (
                File.Exists(@"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd")
                    ? @"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
                    : "az"
            );
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "account", "get-access-token", "--resource", Resource, "--output", "json" })
            start.ArgumentList.Add(argument);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new WorkspaceException("Azure CLI (az) isn't installed, so Azure DevOps can't be reached.", 503);
        }
        using var _ = process;
        if (process is null)
            throw new WorkspaceException("Azure CLI could not start.", 503);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var errors = process.StandardError.ReadToEndAsync(limit.Token);
        await process.WaitForExitAsync(limit.Token);
        if (process.ExitCode != 0)
            throw new WorkspaceException(
                (await errors).Contains("az login", StringComparison.OrdinalIgnoreCase)
                    ? "Azure DevOps needs you to sign in again: run `az login` in a terminal."
                    : "Azure CLI could not get an Azure DevOps token.",
                503
            );
        using var document = JsonDocument.Parse(await output);
        var root = document.RootElement;
        var value =
            root.GetProperty("accessToken").GetString()
            ?? throw new WorkspaceException("Azure CLI returned no token.", 503);
        var expires =
            root.TryGetProperty("expires_on", out var epoch) && epoch.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(epoch.GetInt64())
                : DateTimeOffset.UtcNow.AddMinutes(30);
        return (value, expires);
    }

    public async Task<JsonElement> GetAsync(string organization, string pathAndQuery, CancellationToken token)
    {
        if (!ValidOrganization(organization))
            throw new WorkspaceException("That Azure DevOps organization name isn't valid.", 400);
        using var client = clients.CreateClient("azure-devops");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://dev.azure.com/" + organization + "/" + pathAndQuery
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(token));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, token);
        if (
            response.StatusCode
            is HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Redirect
                or HttpStatusCode.Found
        )
        {
            _token = null;
            throw new WorkspaceException(
                "Azure DevOps refused the request. Check `az login` and your access to " + organization + ".",
                503
            );
        }
        if (!response.IsSuccessStatusCode)
            throw new WorkspaceException("Azure DevOps answered " + (int)response.StatusCode + ".", 502);
        // An unaccepted sign-in can come back as a 203 HTML sign-in page rather than an error.
        if (
            response.StatusCode == HttpStatusCode.NonAuthoritativeInformation
            || response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase)
                != true
        )
        {
            _token = null;
            throw new WorkspaceException(
                "Azure DevOps didn't accept the sign-in. Run `az login` and check your access to " + organization + ".",
                503
            );
        }
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(token),
            cancellationToken: token
        );
        return document.RootElement.Clone();
    }

    public async Task<(string Id, string Name)> MeAsync(string organization, CancellationToken token)
    {
        if (_me.TryGetValue(organization, out var known))
            return known;
        var data = await GetAsync(organization, "_apis/connectionData?api-version=7.1-preview", token);
        var user = data.GetProperty("authenticatedUser");
        return _me[organization] = (
            user.GetProperty("id").GetString()!,
            user.TryGetProperty("providerDisplayName", out var name) ? name.GetString() ?? "" : ""
        );
    }

    // Org-wide (optionally by creator) or one repository's pull requests. status: active, completed or abandoned.
    public async Task<AdoPullRequest[]> PullRequestsAsync(
        string organization,
        string status,
        int top,
        CancellationToken token,
        string? creatorId = null,
        string? project = null,
        string? repository = null
    )
    {
        var query =
            "searchCriteria.status="
            + Uri.EscapeDataString(status)
            + "&$top="
            + Math.Clamp(top, 1, 50)
            + "&api-version=7.1"
            + (creatorId is null ? "" : "&searchCriteria.creatorId=" + Uri.EscapeDataString(creatorId));
        var path =
            project is not null && repository is not null
                ? Uri.EscapeDataString(project)
                    + "/_apis/git/repositories/"
                    + Uri.EscapeDataString(repository)
                    + "/pullrequests?"
                    + query
                : "_apis/git/pullrequests?" + query;
        var data = await GetAsync(organization, path, token);
        return [.. data.GetProperty("value").EnumerateArray().Select(item => Parse(organization, item))];
    }

    // The owner's PRs created (status all) or completed in a time window, using the API's time-range search.
    public async Task<AdoPullRequest[]> PullRequestsBetweenAsync(
        string organization,
        string creatorId,
        string range,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken token
    )
    {
        var query =
            "searchCriteria.creatorId="
            + Uri.EscapeDataString(creatorId)
            + "&searchCriteria.status="
            + (range == "closed" ? "completed" : "all")
            + "&searchCriteria.queryTimeRangeType="
            + range
            + "&searchCriteria.minTime="
            + Uri.EscapeDataString(from.UtcDateTime.ToString("O"))
            + "&searchCriteria.maxTime="
            + Uri.EscapeDataString(to.UtcDateTime.ToString("O"))
            + "&$top=100&api-version=7.1";
        var data = await GetAsync(organization, "_apis/git/pullrequests?" + query, token);
        return [.. data.GetProperty("value").EnumerateArray().Select(item => Parse(organization, item))];
    }

    public static AdoPullRequest Parse(string organization, JsonElement item)
    {
        string Text(JsonElement value, params string[] path)
        {
            foreach (var key in path)
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value))
                    return "";
            }
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }
        var repository = item.GetProperty("repository");
        return new(
            item.GetProperty("pullRequestId").GetInt32(),
            ContentGuard.Clean(Text(item, "title"), 200).Text,
            organization,
            Text(repository, "project", "name"),
            Text(repository, "project", "id"),
            Text(repository, "name"),
            Text(repository, "id"),
            Text(item, "sourceRefName").Replace("refs/heads/", ""),
            Text(item, "targetRefName").Replace("refs/heads/", ""),
            item.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True,
            Text(item, "mergeStatus"),
            Text(item, "createdBy", "displayName"),
            Text(item, "createdBy", "id"),
            Text(item, "creationDate"),
            Text(item, "closedDate") is { Length: > 0 } closed ? closed : null,
            item.TryGetProperty("reviewers", out var reviewers) && reviewers.ValueKind == JsonValueKind.Array
                ?
                [
                    .. reviewers
                        .EnumerateArray()
                        .Select(reviewer =>
                            reviewer.TryGetProperty("vote", out var vote) && vote.TryGetInt32(out var number)
                                ? number
                                : 0
                        ),
                ]
                : []
        );
    }

    // What needs the author: failing or pending policies (builds, required reviewers), open comment threads, conflicts, and blocking votes.
    public async Task<AdoAttention> AttentionAsync(AdoPullRequest pr, CancellationToken token)
    {
        var failing = new List<string>();
        var running = new List<string>();
        var threads = 0;
        try
        {
            var artifact = Uri.EscapeDataString("vstfs:///CodeReview/CodeReviewId/" + pr.ProjectId + "/" + pr.Id);
            var policies = await GetAsync(
                pr.Organization,
                Uri.EscapeDataString(pr.Project)
                    + "/_apis/policy/evaluations?artifactId="
                    + artifact
                    + "&api-version=7.1-preview.1",
                token
            );
            foreach (var evaluation in policies.GetProperty("value").EnumerateArray())
            {
                var status = evaluation.TryGetProperty("status", out var value) ? value.GetString() ?? "" : "";
                var name =
                    evaluation.TryGetProperty("configuration", out var configuration)
                    && configuration.TryGetProperty("type", out var type)
                    && type.TryGetProperty("displayName", out var display)
                        ? display.GetString() ?? "policy"
                        : "policy";
                if (
                    configuration.ValueKind == JsonValueKind.Object
                    && configuration.TryGetProperty("isBlocking", out var blocking)
                    && blocking.ValueKind == JsonValueKind.False
                )
                    continue;
                if (status is "rejected" or "broken")
                    failing.Add(name);
                else if (status is "running" or "queued")
                    running.Add(name);
            }
        }
        catch (WorkspaceException) { }
        try
        {
            var data = await GetAsync(
                pr.Organization,
                Uri.EscapeDataString(pr.Project)
                    + "/_apis/git/repositories/"
                    + Uri.EscapeDataString(pr.RepositoryId)
                    + "/pullRequests/"
                    + pr.Id
                    + "/threads?api-version=7.1",
                token
            );
            threads = data.GetProperty("value")
                .EnumerateArray()
                .Count(thread =>
                    thread.TryGetProperty("status", out var status)
                    && status.GetString() is "active" or "pending"
                    && !(thread.TryGetProperty("isDeleted", out var deleted) && deleted.ValueKind == JsonValueKind.True)
                    && thread.TryGetProperty("comments", out var comments)
                    && comments.GetArrayLength() > 0
                    && comments[0].TryGetProperty("commentType", out var kind)
                    && kind.GetString() != "system"
                );
        }
        catch (WorkspaceException) { }
        var problems = new List<string>();
        if (failing.Count > 0)
            problems.Add("failing: " + string.Join(", ", failing.Distinct()));
        if (pr.MergeStatus is "conflicts" or "failure")
            problems.Add(pr.MergeStatus == "conflicts" ? "merge conflicts" : "merge check failed");
        if (pr.Votes.Contains(-10))
            problems.Add("a reviewer rejected it");
        else if (pr.Votes.Contains(-5))
            problems.Add("a reviewer is waiting for you");
        if (threads > 0)
            problems.Add(threads + " open comment thread" + (threads == 1 ? "" : "s"));
        return new(pr, [.. failing.Distinct()], [.. running.Distinct()], threads, [.. problems]);
    }
}
