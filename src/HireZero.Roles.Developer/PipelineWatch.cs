using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Roles.Developer;

// A watch only observes one run attempt and one branch head. It grants no model or write authority.
public sealed record PipelineScope(
    long AccountId,
    long RepositoryId,
    string Repository,
    string Ref,
    string HeadSha,
    long RunId,
    int Attempt
);

public sealed record PipelineObservation(
    PipelineScope Scope,
    string CurrentHead,
    string Status,
    string? Conclusion,
    string Cursor
);

public interface IPipelineRunSource
{
    Task<PipelineObservation> ReadAsync(PipelineScope scope, CancellationToken token);
}

public sealed record PipelineWatch(
    string Id,
    PipelineScope Scope,
    string State,
    string? Conclusion,
    string? Cursor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset NextPoll,
    DateTimeOffset Deadline,
    int Failures = 0,
    string? Note = null
)
{
    public bool Active => State == "watching";
    public string Url =>
        "https://github.com/" + Scope.Repository + "/actions/runs/" + Scope.RunId + "/attempts/" + Scope.Attempt;
}

public sealed class PipelineWatchStore(string path)
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        );
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS pipeline_watches(id TEXT PRIMARY KEY,scope_key TEXT UNIQUE NOT NULL,payload TEXT NOT NULL)";
        command.ExecuteNonQuery();
        return connection;
    }

    public PipelineWatch[] All()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM pipeline_watches ORDER BY rowid DESC";
        using var reader = command.ExecuteReader();
        var result = new List<PipelineWatch>();
        while (reader.Read())
            result.Add(JsonSerializer.Deserialize<PipelineWatch>(reader.GetString(0))!);
        return [.. result];
    }

    public PipelineWatch Create(PipelineScope scope, DateTimeOffset now)
    {
        if (
            scope.AccountId <= 0
            || scope.RepositoryId <= 0
            || !GitHubReviewPolicy.ValidRepository(scope.Repository)
            || !PullRequestPolicy.Sha(scope.HeadSha)
            || scope.RunId <= 0
            || scope.Attempt <= 0
            || scope.Ref.Length is < 1 or > 200
            || scope.Ref.Any(char.IsControl)
        )
            throw new ReviewAccessException("Choose an exact repository, branch, commit and run attempt.", 400);
        var key = PullRequestPolicy.Hash(JsonSerializer.Serialize(scope));
        var watch = new PipelineWatch(
            Guid.NewGuid().ToString("N"),
            scope,
            "watching",
            null,
            null,
            now,
            now,
            now,
            now.AddHours(2)
        );
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO pipeline_watches VALUES($id,$key,$payload)";
        command.Parameters.AddWithValue("$id", watch.Id);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(watch));
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "SELECT payload FROM pipeline_watches WHERE scope_key=$key";
        command.Parameters.AddWithValue("$key", key);
        return JsonSerializer.Deserialize<PipelineWatch>((string)command.ExecuteScalar()!)!;
    }

    public bool Replace(PipelineWatch before, PipelineWatch after)
    {
        if (before.Id != after.Id || before.Scope != after.Scope)
            throw new ReviewAccessException("A watch cannot change its approved observation scope.");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE pipeline_watches SET payload=$after WHERE id=$id AND payload=$before";
        command.Parameters.AddWithValue("$id", before.Id);
        command.Parameters.AddWithValue("$before", JsonSerializer.Serialize(before));
        command.Parameters.AddWithValue("$after", JsonSerializer.Serialize(after));
        return command.ExecuteNonQuery() == 1;
    }

    public PipelineWatch Cancel(string id, DateTimeOffset now)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var before =
                All().SingleOrDefault(item => item.Id == id)
                ?? throw new ReviewAccessException("This watch was not found.", 404);
            if (!before.Active)
                return before;
            var after = before with
            {
                State = "canceled",
                UpdatedAt = now,
                Note = "Canceled locally; the remote run was not changed.",
            };
            if (Replace(before, after))
                return after;
        }
        throw new ReviewAccessException("The watch changed. Refresh before canceling.");
    }
}

public sealed class PipelineWatchWorker(PipelineWatchStore store, IPipelineRunSource source) : BackgroundService
{
    public static bool Enabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("HIREZERO_PIPELINE_WATCH"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    public async Task PollAsync(DateTimeOffset now, CancellationToken token)
    {
        foreach (var before in store.All().Where(item => item.Active && item.NextPoll <= now).Take(5))
        {
            if (before.Deadline <= now)
            {
                store.Replace(
                    before,
                    before with
                    {
                        State = "timed-out",
                        UpdatedAt = now,
                        Note = "The two-hour observation deadline expired.",
                    }
                );
                continue;
            }
            // Durable lease bounds duplicate polling across restarts/competing workers. No execution follows a poll.
            var lease = before with
            {
                NextPoll = now.AddSeconds(30),
            };
            if (!store.Replace(before, lease))
                continue;
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
            bounded.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var observed = await source.ReadAsync(lease.Scope, bounded.Token);
                var drift = observed.Scope != lease.Scope || observed.CurrentHead != lease.Scope.HeadSha;
                var terminal = observed.Status == "completed";
                var state =
                    drift ? "stale"
                    : terminal ? "completed"
                    : "watching";
                store.Replace(
                    lease,
                    lease with
                    {
                        State = state,
                        Conclusion = drift ? null : observed.Conclusion,
                        Cursor = drift ? lease.Cursor : observed.Cursor,
                        UpdatedAt = now,
                        Failures = 0,
                        NextPoll = now.AddSeconds(30),
                        Note =
                            drift ? "The repository, branch, commit or run attempt changed. Select a fresh scope."
                            : terminal ? "Observed terminal result; diagnosis and repair still require owner approval."
                            : null,
                    }
                );
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
                when (error
                        is WorkspaceException
                            or HttpRequestException
                            or JsonException
                            or InvalidOperationException
                            or OperationCanceledException
                )
            {
                var failures = lease.Failures + 1;
                store.Replace(
                    lease,
                    lease with
                    {
                        Failures = failures,
                        UpdatedAt = now,
                        State = failures >= 6 ? "needs-you" : "watching",
                        NextPoll = now.AddSeconds(Math.Min(300, 30 * Math.Pow(2, failures))),
                        Note = "The exact run could not be verified. No model or remote action was started.",
                    }
                );
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAsync(DateTimeOffset.UtcNow, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}

public partial class GitHubReviewClient : IPipelineRunSource
{
    public async Task<PipelineObservation> ReadAsync(PipelineScope scope, CancellationToken token)
    {
        if ((await IdentityAsync(token)).Id != scope.AccountId)
            throw new ReviewAccessException("The connected account changed.", 403);
        var run = await ApiAsync("repos/" + scope.Repository + "/actions/runs/" + scope.RunId, token);
        var commit = await ApiAsync("repos/" + scope.Repository + "/commits/" + Uri.EscapeDataString(scope.Ref), token);
        var actual = scope with
        {
            RepositoryId = run.GetProperty("repository").GetProperty("id").GetInt64(),
            Repository = run.GetProperty("repository").GetProperty("full_name").GetString()!,
            Ref = run.GetProperty("head_branch").GetString()!,
            HeadSha = run.GetProperty("head_sha").GetString()!,
            RunId = run.GetProperty("id").GetInt64(),
            Attempt = run.GetProperty("run_attempt").GetInt32(),
        };
        return new(
            actual,
            commit.GetProperty("sha").GetString()!,
            run.GetProperty("status").GetString()!,
            run.GetProperty("conclusion").GetString(),
            run.GetProperty("updated_at").GetString()!
        );
    }
}
