using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Where company data lives (docs/adr/0025-one-schema-two-databases.md). Statements are written once, in the portable
// subset the company schema already uses, with $0, $1 … parameters; a database translates them and decides how a
// write transaction excludes other writers. SQLite is built in for single-machine installs; a server database for
// hosted installs is an integration.
public abstract class StoreDatabase
{
    // The directory holding the files that must not live inside the database, such as the audit key.
    public abstract string KeyDirectory { get; }

    // False only when nothing has been created yet, so listing companies never creates a database.
    public virtual bool Exists => true;

    public abstract DbConnection Open();

    // Starts a write transaction that excludes every other writer to the same scope until it ends.
    public abstract void Begin(StoreConnection db, string scope);

    public abstract bool TableExists(StoreConnection db, string table);

    public virtual string Translate(string sql) => sql;

    public virtual string Parameter(int index) => "$" + index;
}

public sealed class SqliteStoreDatabase(string path) : StoreDatabase
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public override string KeyDirectory => System.IO.Path.GetDirectoryName(Path)!;

    public override bool Exists => File.Exists(Path);

    public override DbConnection Open()
    {
        Directory.CreateDirectory(KeyDirectory);
        var db = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Pooling = false,
                ForeignKeys = true,
                DefaultTimeout = 5,
            }.ToString()
        );
        db.Open();
        return db;
    }

    // SQLite has one writer per file: BEGIN IMMEDIATE takes it for every scope at once.
    public override void Begin(StoreConnection db, string scope) => db.Execute("BEGIN IMMEDIATE");

    public override bool TableExists(StoreConnection db, string table) =>
        db.Scalar("SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$0", table) != "0";
}

// One open connection and the database it speaks to. Every statement goes through the database's translation.
public sealed class StoreConnection(DbConnection connection, StoreDatabase database, bool owned = true) : IDisposable
{
    public StoreDatabase Database => database;

    public static StoreConnection Over(SqliteConnection connection) =>
        new(connection, new SqliteStoreDatabase(connection.DataSource), owned: false);

    public DbCommand Command(string sql, params object?[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = database.Translate(sql);
        for (var i = 0; i < values.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = database.Parameter(i);
            parameter.Value = values[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    public void Execute(string sql, params object?[] values)
    {
        using var command = Command(sql, values);
        command.ExecuteNonQuery();
    }

    public string? Scalar(string sql, params object?[] values)
    {
        using var command = Command(sql, values);
        return command.ExecuteScalar() switch
        {
            null or DBNull => null,
            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public T[] Rows<T>(string sql, Func<DbDataReader, T> read, params object?[] values)
    {
        using var command = Command(sql, values);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(read(reader));
        return [.. rows];
    }

    // A write transaction over the scope; measurements wait for its commit (docs/adr/0024).
    public T Transaction<T>(string scope, Func<T> action)
    {
        database.Begin(this, scope);
        CompanyTelemetry.BeginTransaction();
        try
        {
            var result = action();
            Execute("COMMIT");
            CompanyTelemetry.Commit();
            return result;
        }
        catch
        {
            CompanyTelemetry.Discard();
            Execute("ROLLBACK");
            throw;
        }
    }

    public void Dispose()
    {
        if (owned)
            connection.Dispose();
    }
}

// The one migration framework for the company schema and every module's own tables. Each owner's steps apply once,
// in order, and are recorded with the checksum of their portable text, so the same ledger reads the same on every
// database. A recorded step whose text changed, a gap, or a version this build does not know refuses to start.
public static class SchemaMigrations
{
    public const string Ledger = "module_schema_migrations";

    public static void EnsureLedger(StoreConnection db) =>
        db.Execute(
            "CREATE TABLE IF NOT EXISTS module_schema_migrations(owner TEXT NOT NULL, version INTEGER NOT NULL, checksum TEXT NOT NULL, PRIMARY KEY(owner, version))"
        );

    // The caller holds a write transaction.
    public static void Apply(StoreConnection db, string owner, IReadOnlyList<string> steps)
    {
        if (owner.Length is 0 or > 80 || steps.Count == 0)
            throw new ArgumentException("Name the storage owner and at least one step.");
        EnsureLedger(db);
        var applied = db.Rows(
            "SELECT version, checksum FROM module_schema_migrations WHERE owner=$0 ORDER BY version",
            r => (Version: Convert.ToInt32(r.GetValue(0)), Checksum: r.GetString(1)),
            owner
        );
        if (
            applied.Where((m, i) => m.Version != i + 1).Any()
            || applied.Any(m => m.Version > steps.Count || m.Checksum != ContentGuard.Digest(steps[m.Version - 1]))
        )
            throw new InvalidOperationException(
                "The " + owner + " storage is newer or its migration checksum changed. Nothing was started."
            );
        for (var i = applied.Length; i < steps.Count; i++)
        {
            db.Execute(steps[i]);
            db.Execute(
                "INSERT INTO module_schema_migrations(owner,version,checksum) VALUES($0,$1,$2)",
                owner,
                i + 1,
                ContentGuard.Digest(steps[i])
            );
        }
    }
}
