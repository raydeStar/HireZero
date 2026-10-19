namespace HireZero.Tools.Node;

// Installs one npm package at an exact version into a directory that already has its package.json.
public sealed record NpmInstall(string Directory, string Package, string Version);

public static class NodeTools
{
    public static readonly ToolContract<NpmInstall, string> InstallPinned = new(
        "npm.install-pinned",
        ToolRisk.Write,
        "Download and install one npm package at an exact version into a local directory."
    );
}

public sealed record TrustedPostgresRequest;

// HireZero's trusted PostgreSQL test toolchain: the shipped runner and the pinned binaries in HireZero's own toolchain
// folder (outside every worktree). Package and Version are the exact pin the install uses.
public sealed record TrustedPostgresStatus(
    string Directory,
    string Package,
    string Version,
    bool Installed,
    bool RunnerIntact
)
{
    public string Runner => Path.Combine(Directory, "runner.mjs");
}

// One repository's database test setup (owner/name): the database the runner creates and the environment variables its
// tests read, as templates over {host} {port} {user} {password} {database} {url} {dataDir}.
public sealed record PostgresProfile(string Repository, string Database, Dictionary<string, string> Env);

public sealed record PostgresProfileRequest(string Repository);

// Saves one repository's profile, or removes it when Database is null.
public sealed record PostgresProfileChange(string Repository, string? Database, Dictionary<string, string>? Env);

public static partial class TrustedPostgresTools
{
    // The variable through which a coding run hands the runner its repository's profile file.
    public const string ProfileVariable = "HIREZERO_POSTGRES_PROFILE";

    public static readonly ToolContract<PostgresProfileRequest, PostgresProfile?> Profile = new(
        "postgres.profile",
        ToolRisk.Read,
        "The trusted PostgreSQL test profile the owner set for one repository, if any."
    );

    public static readonly ToolContract<PostgresProfileChange, PostgresProfile?> SaveProfile = new(
        "postgres.save-profile",
        ToolRisk.Write,
        "Save or remove one repository's trusted PostgreSQL test profile."
    );

    // Why a profile change is refused, or null. The repository is owner/name; the database a lowercase identifier;
    // up to 40 UPPER_CASE variables of at most 300 characters each.
    public static string? Refusal(PostgresProfileChange change)
    {
        if (!RepositoryPattern().IsMatch(change.Repository))
            return "Use owner/name for the repository.";
        if (change.Database is null)
            return change.Env is { Count: > 0 } ? "A removed profile has no variables." : null;
        var env = change.Env ?? [];
        return
            !DatabasePattern().IsMatch(change.Database)
            || env.Count > 40
            || env.Any(item => !VariablePattern().IsMatch(item.Key) || item.Value.Length > 300)
            ? "Use a lowercase database name and up to 40 UPPER_CASE variables of at most 300 characters."
            : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}$")]
    private static partial System.Text.RegularExpressions.Regex RepositoryPattern();

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial System.Text.RegularExpressions.Regex DatabasePattern();

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z_][A-Z0-9_]{0,63}$")]
    private static partial System.Text.RegularExpressions.Regex VariablePattern();

    public static readonly ToolContract<TrustedPostgresRequest, TrustedPostgresStatus> Status = new(
        "postgres.status",
        ToolRisk.Read,
        "Whether the trusted PostgreSQL test runner and its pinned binaries are installed and intact."
    );

    public static readonly ToolContract<TrustedPostgresRequest, string> PrepareRunner = new(
        "postgres.prepare-runner",
        ToolRisk.Write,
        "Write the shipped trusted PostgreSQL runner and its package file into HireZero's toolchain folder."
    );
}
