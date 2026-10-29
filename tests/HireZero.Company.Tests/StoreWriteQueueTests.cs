using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using HireZero.Core;
using Microsoft.Data.Sqlite;

// Writers in one process take turns per database file (docs/adr/0168-company-writers-queue-for-the-database.md): every
// turn is handed back, two database objects on one file share the queue, and waiting past the busy timeout reports
// the database locked as SQLite would. Each write's wait and hold are measured.
public sealed class StoreWriteQueueTests : IAsyncDisposable
{
    private readonly TemporaryRoot _root = new("store-write-queue-");

    private string File => Path.Combine(_root.Path, "store.sqlite");

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    private SqliteStoreDatabase Database() => new(File) { BusyTimeoutSeconds = 1 };

    private static void Write(SqliteStoreDatabase database, string scope = "fictional-scope", Action? inside = null)
    {
        using var db = new StoreConnection(database.Open(), database);
        db.Transaction(
            scope,
            () =>
            {
                db.Execute("CREATE TABLE IF NOT EXISTS fictional_rows (value INTEGER)");
                db.Execute("INSERT INTO fictional_rows VALUES (1)");
                inside?.Invoke();
                return 0;
            }
        );
    }

    [Fact]
    public void ATurnIsHandedBackAfterACommitARollbackAndALockedBegin()
    {
        var database = Database();
        Write(database);
        Assert.Throws<InvalidOperationException>(() =>
            Write(database, inside: () => throw new InvalidOperationException("Fictional fault"))
        );
        using (
            var other = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = File, Pooling = false }.ToString()
            )
        )
        {
            other.Open();
            using var begin = other.CreateCommand();
            begin.CommandText = "BEGIN IMMEDIATE";
            begin.ExecuteNonQuery();
            Assert.True(database.IsBusy(Assert.Throws<SqliteException>(() => Write(database))));
        }
        Write(database);
    }

    [Fact]
    public async Task WritersOnOneFileShareTheQueueAndAWaitPastTheTimeoutIsBusy()
    {
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var token = TestContext.Current.CancellationToken;
        var first = Task.Run(
            () =>
                Write(
                    Database(),
                    inside: () =>
                    {
                        holding.Set();
                        release.Wait(token);
                    }
                ),
            token
        );
        Assert.True(holding.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        var second = Database();
        var locked = Assert.Throws<SqliteException>(() => Write(second));

        Assert.True(second.IsBusy(locked));
        Assert.Contains("writers in this process", locked.Message);
        release.Set();
        await first;
        Write(second);
    }

    [Fact]
    public void ConnectionsUseSqlitesOwnBusyHandlerForTheBusyTimeout()
    {
        var database = Database();
        using var db = new StoreConnection(database.Open(), database);
        Assert.Equal("1000", db.Scalar("PRAGMA busy_timeout"));
    }

    [Fact]
    public void EachWriteRecordsItsWaitAndHold()
    {
        var scope = "fictional-scope-" + Guid.NewGuid().ToString("N");
        var seen = new ConcurrentBag<(string Name, string Outcome)>();
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CompanyTelemetry.Name)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        meters.SetMeasurementEventCallback<double>(
            (instrument, _, tags, _) =>
            {
                var (tagScope, outcome) = ("", "");
                foreach (var tag in tags)
                {
                    if (tag.Key == "scope")
                        tagScope = tag.Value?.ToString() ?? "";
                    if (tag.Key == "outcome")
                        outcome = tag.Value?.ToString() ?? "";
                }
                if (tagScope == scope)
                    seen.Add((instrument.Name, outcome));
            }
        );
        meters.Start();

        Write(Database(), scope);

        Assert.Contains(("hirezero.store.write.wait", "committed"), seen);
        Assert.Contains(("hirezero.store.write.hold", "committed"), seen);
    }
}
