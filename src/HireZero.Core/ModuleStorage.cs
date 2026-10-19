using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Versioned storage for a module or integration's own SQLite file (Phase 2B.5j), through the one migration framework
// (SchemaMigrations, docs/adr/0025-one-schema-two-databases.md): each owner's steps apply once, in order, inside one
// transaction. A first step written as CREATE TABLE IF NOT EXISTS adopts a table an older build created without a
// record.
public static class ModuleStorage
{
    public static void Migrate(SqliteConnection db, string owner, IReadOnlyList<string> steps)
    {
        var store = StoreConnection.Over(db);
        store.Transaction(
            "schema",
            () =>
            {
                SchemaMigrations.Apply(store, owner, steps);
                return true;
            }
        );
    }
}
