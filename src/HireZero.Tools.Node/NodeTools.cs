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
