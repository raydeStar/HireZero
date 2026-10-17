using System.Text.Json;

namespace HireZero.Core;

public sealed record RoleInfo(string Id, string Name, string Summary);

public sealed record RoleContext(string DataRoot, string DatabasePath);

// A pluggable employee (developer, marketer, ...). Hiring a role registers its services, sensors, task
// handlers, conversation responders and endpoints; firing it removes all of them on the next start.
// Its data stays on disk, so re-hiring picks up where it left off.
public interface IRole
{
    RoleInfo Info { get; }
    void AddServices(IServiceCollection services, RoleContext context);
    void MapEndpoints(WebApplication app);

    // Runs once after the app is built (warm caches, recover interrupted work).
    void Start(IServiceProvider services) { }
}

// Which compiled-in roles are hired, persisted as roles.json in the data root.
public sealed class RoleCatalog(string dataRoot, IReadOnlyList<IRole> available, string[] defaultHired)
{
    private readonly string _path = Path.Combine(dataRoot, "roles.json");
    public IReadOnlyList<IRole> Available => available;

    public string[] HiredIds()
    {
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path)) is { } saved)
                return [.. saved.Where(id => available.Any(role => role.Info.Id == id))];
        }
        catch (JsonException) { }
        return defaultHired;
    }

    public IRole[] Hired()
    {
        var ids = HiredIds();
        return [.. available.Where(role => ids.Contains(role.Info.Id))];
    }

    public string[] SetHired(string id, bool hired)
    {
        if (available.All(role => role.Info.Id != id))
            throw new WorkspaceException("Unknown role.", 404);
        var ids = HiredIds().Where(value => value != id).ToList();
        if (hired)
            ids.Add(id);
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(_path, JsonSerializer.Serialize(ids));
        return [.. ids];
    }
}
