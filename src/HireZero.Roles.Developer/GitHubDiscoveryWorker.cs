namespace HireZero.Roles.Developer;

public sealed class GitHubDiscoveryWorker(GitHubDiscovery discovery) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await discovery.TickAsync(DateTimeOffset.UtcNow, token); }
            catch (Exception error) when (error is WorkspaceException or Microsoft.Data.Sqlite.SqliteException or IOException or HttpRequestException or System.Text.Json.JsonException or InvalidOperationException) { }
        } while (await timer.WaitForNextTickAsync(token));
    }
}
