using HireZero.Core;
using Microsoft.Data.Sqlite;

// Module and integration storage is versioned on the company schema's rules (Phase 2B.5j).
public sealed class ModuleStorageTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "module-storage-" + Guid.NewGuid().ToString("N") + ".sqlite"
    );

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString()
        );
        db.Open();
        return db;
    }

    private static long Count(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static readonly string[] Steps =
    [
        "CREATE TABLE IF NOT EXISTS fictional_runs(id TEXT PRIMARY KEY, payload TEXT NOT NULL)",
        "CREATE INDEX fictional_runs_payload ON fictional_runs(payload)",
    ];

    [Fact]
    public void StepsApplyOnceInOrderAndAdoptATableAnOlderBuildMade()
    {
        using var db = Open();
        using (var old = db.CreateCommand())
        {
            old.CommandText =
                "CREATE TABLE fictional_runs(id TEXT PRIMARY KEY, payload TEXT NOT NULL); INSERT INTO fictional_runs VALUES('r1','kept')";
            old.ExecuteNonQuery();
        }
        ModuleStorage.Migrate(db, "fictional", Steps[..1]);
        ModuleStorage.Migrate(db, "fictional", Steps);
        ModuleStorage.Migrate(db, "fictional", Steps);
        Assert.Equal(2, Count(db, "SELECT COUNT(*) FROM module_schema_migrations WHERE owner='fictional'"));
        Assert.Equal(1, Count(db, "SELECT COUNT(*) FROM fictional_runs"));
        Assert.Equal(1, Count(db, "SELECT COUNT(*) FROM sqlite_master WHERE name='fictional_runs_payload'"));
    }

    [Fact]
    public void AChangedOrNewerSchemaRefusesStartup()
    {
        using var db = Open();
        ModuleStorage.Migrate(db, "fictional", Steps);
        Assert.Throws<InvalidOperationException>(() =>
            ModuleStorage.Migrate(db, "fictional", [Steps[0] + " WITHOUT ROWID", Steps[1]])
        );
        Assert.Throws<InvalidOperationException>(() => ModuleStorage.Migrate(db, "fictional", Steps[..1]));
        // Another owner's steps are independent.
        ModuleStorage.Migrate(db, "other", ["CREATE TABLE other_rows(id TEXT)"]);
        Assert.Equal(3, Count(db, "SELECT COUNT(*) FROM module_schema_migrations"));
    }

    [Fact]
    public void AFailingStepLeavesNothingRecorded()
    {
        using var db = Open();
        Assert.ThrowsAny<SqliteException>(() =>
            ModuleStorage.Migrate(db, "fictional", [Steps[0], "CREATE INDEX broken ON missing_table(x)"])
        );
        Assert.Equal(0, Count(db, "SELECT COUNT(*) FROM sqlite_master WHERE name='fictional_runs'"));
        ModuleStorage.Migrate(db, "fictional", Steps);
        Assert.Equal(2, Count(db, "SELECT COUNT(*) FROM module_schema_migrations"));
    }
}
