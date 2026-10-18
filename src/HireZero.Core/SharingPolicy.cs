using System.Text.Json;

namespace HireZero.Core;

// What may reach the owner's boss at all. Only projects whose repository belongs to one of the listed organizations
// (default: the organization named like the owner's login domain, e.g. Example for @example.invalid; on hosts whose
// organization names are abbreviated, a short form of it, like exam, counts too) and that the owner hasn't hidden. Personal
// repositories and loose folders never appear. OwnerName is how the boss panel refers to the owner.
public sealed record SharingSettings(string[] BossOrganizations, string[] HiddenProjects, string? OwnerName = null);

public sealed class SharingPolicy(string dataRoot, string ownerLogin, string? defaultOwnerName = null)
{
    private readonly string _path = Path.Combine(dataRoot, "sharing.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SharingSettings Defaults()
    {
        var domain = ownerLogin.Contains('@') ? ownerLogin.Split('@')[1].Split('.')[0] : "";
        return new(domain.Length > 0 && domain != "workspace" ? [domain] : [], [], defaultOwnerName);
    }

    public SharingSettings Settings()
    {
        try
        {
            var settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<SharingSettings>(File.ReadAllText(_path), Json) ?? Defaults()
                : Defaults();
            return settings.OwnerName is { Length: > 0 } ? settings : settings with { OwnerName = defaultOwnerName };
        }
        catch (JsonException)
        {
            return Defaults();
        }
    }

    public SharingSettings Save(SharingSettings settings)
    {
        static string[] Clean(string[]? values) =>
            [
                .. (values ?? [])
                    .Select(value => value.Trim())
                    .Where(value => value.Length is > 0 and <= 100)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(100),
            ];
        var name = settings.OwnerName is { } given ? ContentGuard.Clean(given, 80).Text.Trim() : null;
        var clean = new SharingSettings(
            Clean(settings.BossOrganizations),
            Clean(settings.HiddenProjects),
            name is { Length: > 0 } ? name : null
        );
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(_path, JsonSerializer.Serialize(clean, Json));
        return clean;
    }

    public bool SharesWithBoss(ProjectRef project) =>
        project.Origin is { } origin
            ? SharesWithBoss(origin, project.Name)
            : project.Repository is { } repository
                && repository.Contains('/')
                && SharesWithBoss(
                    new GitOrigin(GitOrigin.GitHub, repository.Split('/')[0], null, repository.Split('/')[1]),
                    project.Name
                );

    public bool SharesWithBoss(GitOrigin origin, string projectName)
    {
        var settings = Settings();
        if (
            settings.HiddenProjects.Any(hidden =>
                hidden.Equals(projectName, StringComparison.OrdinalIgnoreCase)
                || hidden.Equals(origin.Name, StringComparison.OrdinalIgnoreCase)
                || hidden.Equals(origin.Slug, StringComparison.OrdinalIgnoreCase)
            )
        )
            return false;
        // Some hosts' organization names are short forms of the company name ("exam" for Example).
        var devops = origin.AbbreviatedOrganization;
        return settings.BossOrganizations.Any(allowed =>
            allowed.Equals(origin.Organization, StringComparison.OrdinalIgnoreCase)
            || devops
                && origin.Organization.Length >= 4
                && allowed.StartsWith(origin.Organization, StringComparison.OrdinalIgnoreCase)
        );
    }
}
