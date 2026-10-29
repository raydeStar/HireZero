using HireZero.Core;
using Microsoft.Data.Sqlite;

// Another writer holding the company database past the busy timeout skips a scheduler round instead of stopping the
// company; the work runs once the lock is gone (docs/adr/0167-a-locked-database-skips-a-scheduler-round.md).
public sealed class CompanySchedulerLockTests : IAsyncDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("scheduler-lock-");

    private string DatabasePath => Path.Combine(_root.Path, "company.sqlite");

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await _root.DisposeAsync();
    }

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    // A second connection that takes SQLite's single write lock and keeps it until disposed.
    private static SqliteConnection HoldWriteLock(string path)
    {
        var holder = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        );
        holder.Open();
        using var begin = holder.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE";
        begin.ExecuteNonQuery();
        return holder;
    }

    private static async Task Until(Func<bool> condition, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The scheduler did not get there in time.");
            await Task.Delay(50, token);
        }
    }

    // One developer with one queued task, as an owner would set them up.
    private static void Seed(CompanyStore store)
    {
        var department = CompanyJson.Decode<CompanyDepartment>(
            store.Apply(Cmd("department.create") with { Name = "Engineering" })
        );
        var fields = store
            .Roles.Single(r => r.Id == "developer")
            .OnboardingFields.ToDictionary(f => f, f => "Approved fictional " + f);
        var knowledge = CompanyJson.Decode<CompanyKnowledge>(
            store.Apply(Cmd("knowledge.save") with { DepartmentId = department.Id, Body = CompanyJson.Encode(fields) })
        );
        store.Apply(
            Cmd("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var employee = CompanyJson.Decode<CompanyEmployee>(
            store.Apply(
                Cmd("employee.hire") with
                {
                    DepartmentId = department.Id,
                    RoleId = "developer",
                    Name = "Developer partner",
                    Responsibility = "Fictional",
                }
            )
        );
        var thread = CompanyJson.Decode<CompanyThread>(
            store.Apply(Cmd("thread.create") with { EmployeeId = employee.Id, Name = "Work" })
        );
        store.Apply(
            Cmd("task.create") with
            {
                EmployeeId = employee.Id,
                ThreadId = thread.Id,
                Skill = "developer-advice",
                Body = "Fictional task.",
            }
        );
    }

    [Fact]
    public void OnlyABusyOrLockedSqliteErrorCountsAsBusy()
    {
        var database = new SqliteStoreDatabase(DatabasePath) { BusyTimeoutSeconds = 1 };
        using var holder = HoldWriteLock(DatabasePath);
        using var db = new StoreConnection(database.Open(), database);

        var locked = Assert.Throws<SqliteException>(() => database.Begin(db, "company"));

        Assert.True(database.IsBusy(locked));
        Assert.False(database.IsBusy(new SqliteException("Fictional constraint failure", 19)));
        Assert.False(database.IsBusy(new InvalidOperationException("Fictional fault")));
    }

    [Fact]
    public async Task ALockedDatabaseSkipsRoundsWithoutStoppingAndTheWorkRunsOnceWhenItIsFree()
    {
        var token = TestContext.Current.CancellationToken;
        var store = new CompanyStore(
            new SqliteStoreDatabase(DatabasePath) { BusyTimeoutSeconds = 1 },
            Owner,
            CompanyWorkspace.Definitions()
        );
        Seed(store);
        var model = new FakeModel();
        var skipped = 0;
        using var scheduler = new CompanyScheduler(store, model)
        {
            SkippedRound = _ => Interlocked.Increment(ref skipped),
        };

        using (HoldWriteLock(DatabasePath))
        {
            await scheduler.StartAsync(token);
            await Until(() => Volatile.Read(ref skipped) >= 1, token);
            Assert.False(scheduler.ExecuteTask!.IsCompleted);
            Assert.Empty(model.Calls);
        }

        await Until(() => !model.Calls.IsEmpty, token);
        await scheduler.StopAsync(token);
        Assert.Single(model.Calls);
        Assert.False(scheduler.ExecuteTask.IsFaulted);
    }
}
