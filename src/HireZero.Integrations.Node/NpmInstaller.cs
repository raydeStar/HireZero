using System.Diagnostics;
using System.Text.RegularExpressions;
using HireZero.Tools.Node;

namespace HireZero.Integrations.Node;

// The npm.install-pinned tool: exactly one package at an exact version (no ranges or tags), in an existing directory.
public partial class NpmInstaller
{
    public ToolRegistry Register(ToolRegistry registry)
    {
        registry.Register(NodeTools.InstallPinned, InstallAsync);
        return registry;
    }

    protected virtual async Task<string> InstallAsync(NpmInstall install, CancellationToken token)
    {
        if (
            !Directory.Exists(install.Directory)
            || !PackageName().IsMatch(install.Package)
            || !ExactVersion().IsMatch(install.Version)
        )
            throw new ToolFailureException(
                "refused",
                "Only an exact package version can be installed into an existing directory.",
                409
            );
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "npm")
        {
            WorkingDirectory = install.Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (
            var arg in (OperatingSystem.IsWindows() ? new[] { "/d", "/c", "npm" } : []).Concat([
                "install",
                "--no-audit",
                "--no-fund",
                "--ignore-scripts=false",
                install.Package + "@" + install.Version,
            ])
        )
            start.ArgumentList.Add(arg);
        using var process =
            Process.Start(start) ?? throw new ToolFailureException("unavailable", "npm could not start.");
        var error = process.StandardError.ReadToEndAsync(token);
        await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0)
            throw new ToolFailureException("failed", "npm install failed.", 503, (await error).Trim());
        return install.Package + "@" + install.Version;
    }

    [GeneratedRegex(@"^(@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex PackageName();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex ExactVersion();
}
