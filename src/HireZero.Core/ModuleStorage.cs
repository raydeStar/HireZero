using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Versioned storage for a module or integration's own tables (Phase 2B.5j), on the company schema's rules: each owner's
// steps apply once, in order, inside one transaction, and are recorded with their checksum. A recorded step whose text
// changed, a gap, or a version this build does not know refuses startup, so no store runs on a schema it did not make.
// A first step written as CREATE TABLE IF NOT EXISTS adopts a table an older build created without a record.
public static class ModuleStorage
{
    public static void Migrate(SqliteConnection db, string owner, IReadOnlyList<string> steps)
    {
        if (owner.Length is 0 or > 80 || steps.Count == 0)
            throw new ArgumentException("Name the storage owner and at least one step.");
        using var transaction = db.BeginTransaction();
        Run(
            db,
            transaction,
            "CREATE TABLE IF NOT EXISTS module_schema_migrations(owner TEXT NOT NULL, version INTEGER NOT NULL, checksum TEXT NOT NULL, PRIMARY KEY(owner, version))"
        );
        var applied = new List<(int Version, string Checksum)>();
        using (var read = db.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT version, checksum FROM module_schema_migrations WHERE owner=$o ORDER BY version";
            read.Parameters.AddWithValue("$o", owner);
            using var rows = read.ExecuteReader();
            while (rows.Read())
                applied.Add((rows.GetInt32(0), rows.GetString(1)));
        }
        if (
            applied.Where((m, i) => m.Version != i + 1).Any()
            || applied.Any(m => m.Version > steps.Count || m.Checksum != ContentGuard.Digest(steps[m.Version - 1]))
        )
            throw new InvalidOperationException(
                "The " + owner + " storage is newer or its migration checksum changed. Nothing was started."
            );
        for (var i = applied.Count; i < steps.Count; i++)
        {
            Run(db, transaction, steps[i]);
            using var record = db.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO module_schema_migrations VALUES($o,$v,$c)";
            record.Parameters.AddWithValue("$o", owner);
            record.Parameters.AddWithValue("$v", i + 1);
            record.Parameters.AddWithValue("$c", ContentGuard.Digest(steps[i]));
            record.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void Run(SqliteConnection db, SqliteTransaction transaction, string sql)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
