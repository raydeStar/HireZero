using System.Text;
using System.Text.Json;
using HireZero.Tools.Node;

namespace HireZero.Roles.Developer;

// Per-repository trusted PostgreSQL test profiles (ported from the legacy profile; docs/adr/0020-trusted-postgres-in-live-
// coding-runs.md). The postgres-profile action reads a repository's current profile and names the one change the owner
// typed; approving its publication saves it through postgres.save-profile, once. The database "off" removes the profile.
public static class CompanyPostgresProfile
{
    public const string Action = "postgres-profile";

    public static readonly EmployeeAction Definition = new(
        Action,
        "Set a PostgreSQL test profile",
        "Name the database and environment a repository's tests use with the trusted PostgreSQL runner (database off turns it off), for your approval.",
        [
            new("repository", "Repository (owner/name)", 140),
            new("database", "Database (lowercase, or off)", 63),
            new("env", "Environment (JSON, e.g. {\"DATABASE_URL\":\"{url}\"}, or {})", 4000),
        ]
    )
    {
        Publishes = true,
    };

    public static async Task<string> ReadAsync(
        IReadOnlyDictionary<string, string> input,
        IToolbox tools,
        CancellationToken token
    )
    {
        var change = Change(input);
        var current = await tools.InvokeAsync(TrustedPostgresTools.Profile, new(change.Repository), token);
        var text = new StringBuilder();
        text.AppendLine($"Trusted PostgreSQL test profile for {change.Repository}, read through postgres.profile.");
        text.AppendLine("Current: " + Describe(current));
        text.AppendLine();
        var next = change.Database is null
            ? null
            : new PostgresProfile(change.Repository, change.Database, change.Env!);
        if (Describe(current) == Describe(next))
            text.AppendLine("Not eligible: The profile is already set this way.");
        else
        {
            text.AppendLine("Step: Set it to " + Describe(next) + ".");
            text.AppendLine("Call: " + JsonSerializer.Serialize(change, CompanyJson.Options));
        }
        return text.ToString();
    }

    private static PostgresProfileChange Change(IReadOnlyDictionary<string, string> input)
    {
        var repository = input.GetValueOrDefault("repository")?.Trim() ?? "";
        var database = input.GetValueOrDefault("database")?.Trim() is { Length: > 0 } d and not "off" ? d : null;
        var env = input.GetValueOrDefault("env")?.Trim() is { Length: > 0 } json ? Env(json) : null;
        var change = new PostgresProfileChange(repository, database, database is null ? null : env ?? []);
        return TrustedPostgresTools.Refusal(change) is { } refusal
            ? throw new WorkspaceException(refusal, 422)
            : change;
    }

    private static Dictionary<string, string> Env(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? throw new WorkspaceException("Write the environment as a JSON object of strings.", 422);
        }
        catch (JsonException)
        {
            throw new WorkspaceException("Write the environment as a JSON object of strings.", 422);
        }
    }

    private static string Describe(PostgresProfile? profile) =>
        profile is null
            ? "off"
            : $"database {profile.Database} with "
                + (
                    profile.Env.Count == 0
                        ? "no variables"
                        : string.Join(
                            ", ",
                            profile.Env.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + "=" + e.Value)
                        )
                );

    public static PostgresProfileChange? Parse(string body)
    {
        var line = body.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .LastOrDefault(l => l.StartsWith("Call: ", StringComparison.Ordinal));
        try
        {
            var change = line is null
                ? null
                : JsonSerializer.Deserialize<PostgresProfileChange>(line[6..], CompanyJson.Options);
            return change is not null && TrustedPostgresTools.Refusal(change) is null ? change : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Summary(PostgresProfileChange change) =>
        change.Database is null
            ? $"Turn off trusted PostgreSQL tests for {change.Repository}."
            : $"Set {change.Repository}'s trusted PostgreSQL test profile to database {change.Database} with {change.Env?.Count ?? 0} variable(s).";

    public static string[] Calls(PostgresProfileChange change) =>
        [ToolRegistry.Digest(TrustedPostgresTools.SaveProfile, change)];
}

public sealed class CompanyPostgresProfilePublication(CompanyStore store, ToolRegistry registry)
{
    private (CompanyTask Task, PostgresProfileChange Change) Current(string artifactId)
    {
        var artifact = store.Artifact(artifactId);
        var task = store.Work(artifact.TaskId);
        if (task.Skill != CompanyPostgresProfile.Action || artifact.State != "approved")
            throw new WorkspaceException("Approve a PostgreSQL test profile reading first.");
        var change =
            CompanyPostgresProfile.Parse(artifact.Body)
            ?? throw new WorkspaceException("This reading names no change; it is not eligible.");
        return (task, change);
    }

    public CompanyPublication Propose(string artifactId)
    {
        var (_, change) = Current(artifactId);
        return store.ProposePublication(
            new(null, artifactId, CompanyPostgresProfile.Summary(change), CompanyPostgresProfile.Calls(change))
        );
    }

    public async Task<CompanyPublication> PublishAsync(string publicationId, CancellationToken token = default)
    {
        var publication = store.Publication(publicationId);
        var (task, change) = Current(publication.ArtifactId);
        if (
            publication.GrantId is not null
            || CompanyPostgresProfile.Summary(change) != publication.Summary
            || !publication.Calls.SequenceEqual(CompanyPostgresProfile.Calls(change))
        )
            throw new WorkspaceException("The profile changed after approval; read it again.");
        store.ClaimPublication(publication.Id);
        try
        {
            await registry
                .For(DeveloperModule.Shared, task.EmployeeId, task.Id)
                .InvokeAsync(
                    TrustedPostgresTools.SaveProfile,
                    change,
                    CancellationToken.None,
                    new(CompanyStore.PublicationAuthorityKind, publication.Id)
                );
            return store.FinishPublication(publication.Id, true, "Saved.", landed: true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            store.FinishPublication(
                publication.Id,
                false,
                error is WorkspaceException or ToolFailureException
                    ? error.Message
                    : "The profile was not saved: " + error.GetType().Name
            );
            throw;
        }
    }
}

// What a live coding run gets when its repository has a profile and the toolchain is installed and intact: the toolchain
// folder read-only, the profile file through HIREZERO_POSTGRES_PROFILE, and the runner's usage line in its prompt.
public sealed record CompanyPostgresRun(string Directory, string ProfileJson, string PromptLine)
{
    public static async Task<CompanyPostgresRun?> ForAsync(IToolbox tools, string? repository, CancellationToken token)
    {
        if (repository is null)
            return null;
        TrustedPostgresStatus status;
        PostgresProfile? profile;
        try
        {
            status = await tools.InvokeAsync(TrustedPostgresTools.Status, new(), token);
            if (!status.Installed || !status.RunnerIntact)
                return null;
            profile = await tools.InvokeAsync(TrustedPostgresTools.Profile, new(repository), token);
        }
        catch (ToolRefusedException)
        {
            // A workspace without the toolchain's tools runs without it.
            return null;
        }
        if (profile is null)
            return null;
        // Literal values of secret-named variables (a fixed JWT_SECRET, say) are masked in the run's stored output.
        ContentGuard.RegisterSecrets(profile.Env.Where(item => !item.Value.Contains('{')));
        var runner = status.Runner.Replace('\\', '/');
        return new(
            status.Directory,
            JsonSerializer.Serialize(new { database = profile.Database, env = profile.Env }),
            "POSTGRESQL TESTS: HireZero provides a trusted PostgreSQL test runner. For any check that needs PostgreSQL, run it as ONE plain command: "
                + $"node \"{runner}\" -- <test command>, for example node \"{runner}\" -- node --test test/a.test.mjs "
                + "(the runner starts a disposable database and sets the repository's database environment; only node and npm test commands are allowed after --). "
                + "Do not build, install or run your own PostgreSQL.\n"
        );
    }
}
