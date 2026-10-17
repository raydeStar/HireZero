using HireZero.Core;
using Microsoft.Data.Sqlite;

namespace HireZero.Roles.Developer;

public sealed record Preparation(string RequestId, string Stage, string Blocker, string NextAction, string UpdatedAt);

// The developer role's private preparation stages for a conversation (intake, scoping, gathering, ready).
public sealed class PreparationStore
{
    private readonly string _databasePath;
    private readonly object _gate = new();

    public PreparationStore(string databasePath)
    {
        _databasePath = databasePath;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS preparation(request_id TEXT PRIMARY KEY REFERENCES requests(id),
                stage TEXT NOT NULL, blocker TEXT NOT NULL, next_action TEXT NOT NULL, updated_at TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString()
        );
        connection.Open();
        return connection;
    }

    private static SqliteCommand Command(
        SqliteConnection connection,
        string sql,
        params (string Key, object? Value)[] values
    )
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (key, value) in values)
            command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }

    public Preparation[] Preparations()
    {
        using var connection = Open();
        using var command = Command(
            connection,
            "SELECT request_id,stage,blocker,next_action,updated_at FROM preparation"
        );
        using var reader = command.ExecuteReader();
        var results = new List<Preparation>();
        while (reader.Read())
            results.Add(
                new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)
                )
            );
        return [.. results];
    }

    public Preparation SavePreparation(string requestId, string stage, string blocker, string nextAction)
    {
        var result = new Preparation(requestId, stage, blocker, nextAction, DateTimeOffset.UtcNow.ToString("O"));
        lock (_gate)
        {
            using var connection = Open();
            using var command = Command(
                connection,
                "INSERT INTO preparation VALUES($id,$stage,$blocker,$next,$updated) "
                    + "ON CONFLICT(request_id) DO UPDATE SET stage=excluded.stage,blocker=excluded.blocker,"
                    + "next_action=excluded.next_action,updated_at=excluded.updated_at",
                ("$id", requestId),
                ("$stage", stage),
                ("$blocker", blocker),
                ("$next", nextAction),
                ("$updated", result.UpdatedAt)
            );
            command.ExecuteNonQuery();
        }
        return result;
    }
}
