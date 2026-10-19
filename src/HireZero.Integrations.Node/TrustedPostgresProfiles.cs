using System.Text.Json;
using HireZero.Tools.Node;
using Microsoft.Data.Sqlite;

namespace HireZero.Integrations.Node;

// The owner's per-repository trusted PostgreSQL test profiles (ported from the legacy profile), kept in company storage
// and versioned through ModuleStorage (docs/adr/0018-module-storage-migrations.md). Repositories match without case.
public sealed class TrustedPostgresProfiles
{
    public const string StorageOwner = "trusted-postgres-profiles";
    public static readonly string[] Steps =
    [
        "CREATE TABLE IF NOT EXISTS trusted_postgres_profiles(repository TEXT PRIMARY KEY, payload TEXT NOT NULL)",
    ];

    private readonly string _path;

    public TrustedPostgresProfiles(string database)
    {
        _path = database;
        using var connection = Open();
        ModuleStorage.Migrate(connection, StorageOwner, Steps);
    }

    public void Register(ToolRegistry registry)
    {
        registry.Register(TrustedPostgresTools.Profile, (request, _) => Task.FromResult(Find(request.Repository)));
        registry.Register(TrustedPostgresTools.SaveProfile, (change, _) => Task.FromResult(Save(change)));
    }

    public PostgresProfile? Find(string repository)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM trusted_postgres_profiles WHERE repository=$r";
        command.Parameters.AddWithValue("$r", repository.ToLowerInvariant());
        return command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<PostgresProfile>(payload) : null;
    }

    public PostgresProfile? Save(PostgresProfileChange change)
    {
        if (TrustedPostgresTools.Refusal(change) is { } refusal)
            throw new WorkspaceException(refusal, 422);
        var profile = change.Database is null
            ? null
            : new PostgresProfile(change.Repository, change.Database, new(change.Env ?? []));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = profile is null
            ? "DELETE FROM trusted_postgres_profiles WHERE repository=$r"
            : "INSERT INTO trusted_postgres_profiles VALUES($r,$p) ON CONFLICT(repository) DO UPDATE SET payload=excluded.payload";
        command.Parameters.AddWithValue("$r", change.Repository.ToLowerInvariant());
        if (profile is not null)
            command.Parameters.AddWithValue("$p", JsonSerializer.Serialize(profile));
        command.ExecuteNonQuery();
        return profile;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString()
        );
        connection.Open();
        return connection;
    }
}
