using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Roles.Developer;

public sealed record GitHubDiscoverySnapshot(DateTimeOffset? CheckedAt, PullRequestSignal[] Findings, string Note,
    DateTimeOffset? LastAttemptAt = null, DateTimeOffset? NextScanAt = null, string? LastError = null,
    int ConsecutiveFailures = 0, bool ScanRunning = false, DateTimeOffset? ChangedAt = null,
    int RepositoriesChecked = 0, int PullRequestsChecked = 0);

// Observation has no agent queue, model gateway, coding runner or publication service.
public sealed class GitHubDiscovery(string databasePath, GitHubReviewClient github, GitHubReviewStore reviews)
{
    private readonly SemaphoreSlim _scan = new(1, 1);
    public TimeSpan Interval { get; } = TimeSpan.FromMinutes(15);
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
    public GitHubDiscoverySnapshot Read()
    {
        using var database = Open(); using var command = database.CreateCommand();
        command.CommandText = "SELECT payload FROM github_discovery_snapshot WHERE id=1";
        var result = command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<GitHubDiscoverySnapshot>(payload)!
            : new(null, [], "Ready for read-only discovery in the connected account's configured repositories.");
        return result with { ScanRunning = _scan.CurrentCount == 0 };
    }

    public async Task<bool> TickAsync(DateTimeOffset now, CancellationToken token)
    {
        if (Read().NextScanAt is { } next && next > now || _scan.CurrentCount == 0) return false;
        await ScanAsync(token); return true;
    }

    public async Task<GitHubDiscoverySnapshot> ScanAsync(CancellationToken token)
    {
        if (!await _scan.WaitAsync(0, token)) throw new ReviewAccessException("A discovery scan is already running.", 409);
        GitHubDiscoverySnapshot before;
        try { before = Read(); } catch { _scan.Release(); throw; }
        var started = Clock();
        try
        {
            Save(before with { LastAttemptAt = started, NextScanAt = started + Interval, ScanRunning = false });
            using (var queueDatabase = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
            {
                queueDatabase.Open(); using var queue = queueDatabase.CreateCommand();
                queue.CommandText = "SELECT (SELECT count(*) FROM requests WHERE status='queued')+(SELECT count(*) FROM agent_tasks WHERE state IN ('queued','active') AND kind<>'reply')+(SELECT count(*) FROM coding_runs WHERE state IN ('queued','running'))";
                if (Convert.ToInt32(queue.ExecuteScalar()) != 0) throw new ReviewAccessException("Archive or cancel previous queued work before discovery.");
            }
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
            bounded.CancelAfter(TimeSpan.FromMinutes(3));
            var (findings, repositories, packets, unavailable) = await ObserveAsync(bounded.Token);
            if (unavailable > 0) throw new ReviewAccessException("A configured source could not be checked completely.");
            var changed = JsonSerializer.Serialize(before.Findings) != JsonSerializer.Serialize(findings);
            var note = "Checked " + repositories + " configured repositories and " + packets + " PR evidence packets: " +
                findings.Length + " PRs need attention; 0 checks unavailable. Assigned issues are not scanned.";
            var result = new GitHubDiscoverySnapshot(Clock(), findings, note, started, Clock() + Interval,
                null, 0, false, changed ? Clock() : before.ChangedAt, repositories, packets);
            Save(result);
            return result;
        }
        catch (Exception error) when (error is WorkspaceException or JsonException or IOException or InvalidOperationException or OperationCanceledException or HttpRequestException or SqliteException)
        {
            var failures = Math.Min(6, before.ConsecutiveFailures + 1);
            Save(before with { LastAttemptAt = started, NextScanAt = Clock().AddMinutes(Math.Min(60, Interval.TotalMinutes * Math.Pow(2, failures - 1))),
                LastError = "Discovery did not complete. Last successful findings were retained. Verify GitHub sign-in and configured repository access.",
                ConsecutiveFailures = failures, ScanRunning = false });
            throw new ReviewAccessException("Discovery did not complete; previous observations were retained.", 503);
        }
        finally { _scan.Release(); }
    }

    private async Task<(PullRequestSignal[] Findings, int Repositories, int Packets, int Unavailable)> ObserveAsync(CancellationToken token)
    {
        var account = await github.IdentityAsync(token);
        var repositories = reviews.Repositories(account.Id).DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        var candidates = new List<(ReviewRepository Repository, CodeReview Review)>(); var unavailable = 0;
        foreach (var repository in repositories)
        {
            token.ThrowIfCancellationRequested();
            try { foreach (var review in (await github.ReviewsAsync(repository, account, token)).Take(8)) candidates.Add((repository, review)); }
            catch (Exception error) when (error is WorkspaceException or JsonException or HttpRequestException or InvalidOperationException) { unavailable++; }
        }
        var findings = new List<PullRequestSignal>(); var packets = 0;
        foreach (var (repository, review) in candidates.Take(24))
        {
            token.ThrowIfCancellationRequested();
            if (!review.Complete) { unavailable++; continue; }
            if (!GitHubReviewPolicy.Owned(account, repository, review)) continue;
            PullRequestEvidence evidence;
            try { evidence = await github.EvidenceAsync(repository.Name, review.Number, false, token); packets++; }
            catch (Exception error) when (error is WorkspaceException or JsonException or HttpRequestException or InvalidOperationException) { unavailable++; continue; }
            if (evidence.RepositoryId != repository.GitHubId || !evidence.Repository.Equals(repository.Name, StringComparison.OrdinalIgnoreCase) ||
                !evidence.Open || evidence.Draft || !evidence.Author.Equals(account.Login, StringComparison.OrdinalIgnoreCase)) continue;
            if (!evidence.Complete || !review.Complete) { unavailable++; continue; }
            var failing = evidence.Checks.Count(check => check.State is "failure" or "error" or "timed_out" or "action_required");
            var threads = evidence.Threads.Count(thread => !thread.Resolved && !thread.Outdated);
            if (failing == 0 && threads == 0) continue;
            var reason = string.Join(" and ", new[] { failing > 0 ? failing + " failing checks" : null, threads > 0 ? threads + " unresolved threads" : null }.OfType<string>());
            findings.Add(new(evidence.Repository, evidence.Number, evidence.HeadSha, ContentGuard.Clean(evidence.Title, 180).Text, reason));
        }
        return (findings.DistinctBy(item => (item.Repository.ToLowerInvariant(), item.Number)).OrderBy(item => item.Repository).ThenBy(item => item.Number).ToArray(), repositories.Length, packets, unavailable);
    }

    private void Save(GitHubDiscoverySnapshot result)
    {
        using var database = Open(); using var command = database.CreateCommand();
        command.CommandText = "INSERT INTO github_discovery_snapshot VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(result)); command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        database.Open(); using var command = database.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS github_discovery_snapshot(id INTEGER PRIMARY KEY CHECK(id=1),payload TEXT NOT NULL)";
        command.ExecuteNonQuery(); return database;
    }
}
