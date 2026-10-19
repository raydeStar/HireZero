using HireZero.Integrations.Postgres;

// The company database (docs/adr/0025-one-schema-two-databases.md): SQLite in the data root for single-machine
// installs, or PostgreSQL for hosted installs when HIREZERO_DATABASE holds its connection string. Files that must not
// live in the database, such as the audit key, stay in the data root either way.
public static class CompanyDatabases
{
    // The connection string, its password and the audit key are secrets (docs/adr/0027-secrets-from-files.md): each can
    // come from a mounted file instead of the environment.
    public static StoreDatabase FromEnvironment(string root, string file, SecretSource? secrets = null)
    {
        secrets ??= SecretSource.Default;
        var auditKey = secrets.Read("HIREZERO_AUDIT_KEY");
        if (secrets.Read(PostgresStoreDatabase.ConnectionVariable) is not { Length: > 0 } connection)
            return new SqliteStoreDatabase(file) { AuditKey = auditKey };
        var password =
            secrets.Describe(PostgresStoreDatabase.PasswordVariable).Origin != SecretOrigin.Missing
                ? () => secrets.Read(PostgresStoreDatabase.PasswordVariable)
                : (Func<string?>?)null;
        return new PostgresStoreDatabase(connection, root, password) { AuditKey = auditKey };
    }
}
