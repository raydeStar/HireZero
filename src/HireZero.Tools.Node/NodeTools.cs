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
);

public static class TrustedPostgresTools
{
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
