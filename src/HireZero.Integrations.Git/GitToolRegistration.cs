using HireZero.Tools.Git;

namespace HireZero.Integrations.Git;

public static class GitToolRegistration
{
    public static ToolRegistry AddGitTools(this ToolRegistry registry, GitCheckoutInspector inspector)
    {
        registry.Register(GitTools.InspectCheckout, (input, token) => inspector.InspectAsync(input.Root, token));
        registry.Register(
            GitTools.ReviewCheckout,
            (input, token) => inspector.ReviewAsync(input.Root, input.Revision, token)
        );
        return registry;
    }

    // Git for Windows' default location, then git on PATH; null when neither exists.
    public static string? FindGit()
    {
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var windows = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Git",
            "cmd",
            name
        );
        if (OperatingSystem.IsWindows() && File.Exists(windows))
            return windows;
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }
}
