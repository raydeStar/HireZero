using HireZero.Integrations.Postgres;

// The company database (docs/adr/0025-one-schema-two-databases.md): SQLite in the data root for single-machine
// installs, or PostgreSQL for hosted installs when HIREZERO_DATABASE holds its connection string. Files that must not
// live in the database, such as the audit key, stay in the data root either way.
public static class CompanyDatabases
{
    public static StoreDatabase FromEnvironment(string root, string file) =>
        Environment.GetEnvironmentVariable(PostgresStoreDatabase.ConnectionVariable) is { Length: > 0 } connection
            ? new PostgresStoreDatabase(connection, root)
            : new SqliteStoreDatabase(file);
}
