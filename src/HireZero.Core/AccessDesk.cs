using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// Someone on the tailnet who opened HireZero without an invitation. Status: pending (waiting for the owner),
// visitor (let in automatically from a company domain), approved or denied.
public sealed record AccessRecord(
    string Login,
    string? DisplayName,
    string Status,
    string FirstSeen,
    string LastSeen,
    string? RequestId,
    string? Role
);

public sealed record AccessSettings(string[] AutoVisitorDomains);

public sealed class AccessStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AccessStore(string path)
    {
        _path = path;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS access_requests(login TEXT PRIMARY KEY, last_seen TEXT NOT NULL, payload TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString()
        );
        connection.Open();
        return connection;
    }

    public AccessRecord? Find(string login) => Query("WHERE login=$l", ("$l", login)).SingleOrDefault();

    public AccessRecord[] All() => Query("ORDER BY last_seen DESC LIMIT 200");

    public AccessRecord? ForRequest(string requestId) => All().FirstOrDefault(record => record.RequestId == requestId);

    private AccessRecord[] Query(string where, params (string Key, object Value)[] values)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM access_requests " + where;
        foreach (var (key, value) in values)
            command.Parameters.AddWithValue(key, value);
        using var reader = command.ExecuteReader();
        var results = new List<AccessRecord>();
        while (reader.Read())
            results.Add(JsonSerializer.Deserialize<AccessRecord>(reader.GetString(0), Json)!);
        return [.. results];
    }

    public AccessRecord Save(AccessRecord record)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO access_requests VALUES($l,$s,$p) ON CONFLICT(login) DO UPDATE SET last_seen=excluded.last_seen,payload=excluded.payload";
        command.Parameters.AddWithValue("$l", record.Login);
        command.Parameters.AddWithValue("$s", record.LastSeen);
        command.Parameters.AddWithValue("$p", JsonSerializer.Serialize(record, Json));
        command.ExecuteNonQuery();
        return record;
    }
}

// The front door. An identity Tailscale vouches for but the owner hasn't invited "knocks": people from the owner's
// company domains come in as visitors (no agent effort until the owner allows it per request); anyone else waits
// for the owner. Either way the owner hears about it in a conversation and under Team, and picks a role.
public sealed class AccessDesk(RequestStore requests, AccessStore store, WorkspaceOwner owner, string dataRoot)
{
    private readonly string _path = Path.Combine(dataRoot, "access.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public AccessSettings Settings()
    {
        try
        {
            if (
                File.Exists(_path)
                && JsonSerializer.Deserialize<AccessSettings>(File.ReadAllText(_path), Json) is { } saved
            )
                return saved;
        }
        catch (JsonException) { }
        var domain = owner.Login.Contains('@') ? owner.Login.Split('@')[1] : "";
        return new(domain.Length > 0 && !domain.EndsWith(".invalid", StringComparison.Ordinal) ? [domain] : []);
    }

    public AccessSettings Save(AccessSettings settings)
    {
        var clean = new AccessSettings([
            .. (settings.AutoVisitorDomains ?? [])
                .Select(value => value.Trim().TrimStart('@').ToLowerInvariant())
                .Where(value => value.Length is > 2 and <= 100 && value.Contains('.') && !value.Contains('@'))
                .Distinct()
                .Take(20),
        ]);
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(_path, JsonSerializer.Serialize(clean, Json));
        return clean;
    }

    // Returns the role to use now, or null when the person must wait (or was denied).
    public string? Knock(string login, string? displayName)
    {
        login = login.Trim().ToLowerInvariant();
        if (login.Length is < 3 or > 320 || !login.Contains('@') || login == owner.Login)
            return null;
        lock (_gate)
        {
            var now = Clock().ToString("O");
            var name = displayName is { Length: > 0 } ? ContentGuard.Clean(displayName, 80).Text : null;
            var existing = store.Find(login);
            if (existing is { Status: "denied" })
            {
                store.Save(existing with { LastSeen = now });
                return null;
            }
            if (requests.FindPerson(login) is { Revoked: false } person)
                return PersonRoles.Normalize(person.Role);
            if (requests.FindPerson(login) is { Revoked: true })
                return null;
            var who = name is null ? login : name + " (" + login + ")";
            var domain = login.Split('@')[1];
            if (Settings().AutoVisitorDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                requests.SetPerson(login, PersonRoles.Visitor, false);
                var conversation = Conversation(
                    login,
                    who + " joined as a visitor",
                    who
                        + " opened HireZero from your tailnet and was let in as a visitor because they're on "
                        + domain
                        + ". They can file requests; no agent effort is spent until you allow it."
                );
                requests.AddOwnerAgentNotice(
                    conversation,
                    who
                        + " joined as a visitor. Promote them to trusted or boss here or under Team, or revoke their access.",
                    "access-visitor:" + login
                );
                store.Save(
                    new(login, name, "visitor", existing?.FirstSeen ?? now, now, conversation, PersonRoles.Visitor)
                );
                return PersonRoles.Visitor;
            }
            if (existing is null)
            {
                var conversation = Conversation(
                    login,
                    who + " wants access",
                    who + " opened HireZero from your tailnet but isn't invited. Choose a role to let them in, or deny."
                );
                requests.AddOwnerAgentNotice(
                    conversation,
                    who + " is waiting to be let in. Choose a role and approve, or deny.",
                    "access-pending:" + login
                );
                store.Save(new(login, name, "pending", now, now, conversation, null));
            }
            else
                store.Save(existing with { LastSeen = now, DisplayName = name ?? existing.DisplayName });
            return null;
        }
    }

    public AccessRecord Approve(string login, string role)
    {
        if (!PersonRoles.Valid(role))
            throw new WorkspaceException("Choose boss, trusted or visitor.", 400);
        var normalized = PersonRoles.Normalize(role);
        lock (_gate)
        {
            var record =
                store.Find(login) ?? throw new WorkspaceException("Nobody with that login asked for access.", 404);
            requests.SetPerson(login, normalized, false);
            if (record.RequestId is { } conversation)
                requests.AddOwnerAgentNotice(
                    conversation,
                    "You let " + (record.DisplayName ?? login) + " in as " + normalized + ".",
                    "access-approved:" + login + ":" + normalized
                );
            return store.Save(record with { Status = "approved", Role = normalized, LastSeen = Clock().ToString("O") });
        }
    }

    public AccessRecord Deny(string login)
    {
        lock (_gate)
        {
            var record =
                store.Find(login) ?? throw new WorkspaceException("Nobody with that login asked for access.", 404);
            if (requests.FindPerson(login) is { } person)
                requests.SetPerson(login, person.Role, true);
            if (record.RequestId is { } conversation)
                requests.AddOwnerAgentNotice(
                    conversation,
                    "You denied access to " + (record.DisplayName ?? login) + ".",
                    "access-denied:" + login
                );
            return store.Save(record with { Status = "denied", LastSeen = Clock().ToString("O") });
        }
    }

    private string Conversation(string login, string title, string description) =>
        requests
            .CreateRequest(
                owner.Login,
                "People",
                ContentGuard.Clean(title, 160).Text,
                ContentGuard.Clean(description, 1000).Text,
                "access:" + login
            )
            .Id;
}
