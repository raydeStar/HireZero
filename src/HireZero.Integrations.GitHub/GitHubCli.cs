using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using HireZero.Tools.Git;
using HireZero.Tools.GitHub;

namespace HireZero.Integrations.GitHub;

// Runs git and the GitHub CLI for the git.* and github.* tools (docs/adr/0007-tool-registry.md). Every process gets
// no interactive prompts, no repository hooks or fsmonitor, no ext/file protocols and bounded output; clones use the
// isolated transport in GitCloneTransportPolicy. The process start passes through AgentEffectBoundary, so a caller's
// scope (for example an approved workflow) is checked again at the exact moment the effect begins.
public partial class GitHubCli
{
    public static readonly string GhExecutable = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe")
        : "gh";
    private const string GitExecutable = "git";
    private static string NoHooks => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    public ToolRegistry Register(ToolRegistry registry)
    {
        foreach (var contract in new[] { GitCommands.Read, GitCommands.Write, GitCommands.Push })
            registry.Register(
                contract,
                (command, token) =>
                {
                    if (GitCommands.Classify(command.Args) != contract)
                        throw new ToolFailureException(
                            "refused",
                            "This git command is not a " + contract.Id + " command.",
                            409
                        );
                    return RunAsync(GitExecutable, command.Root, command.Args, null, token);
                }
            );
        foreach (var contract in new[] { GitHubCommands.Read, GitHubCommands.Write })
            registry.Register(
                contract,
                (command, token) =>
                {
                    if (GitHubCommands.Classify(command.Args, command.Input) != contract)
                        throw new ToolFailureException(
                            "refused",
                            "This GitHub command is not a " + contract.Id + " command.",
                            409
                        );
                    return RunAsync(GhExecutable, null, command.Args, command.Input, token);
                }
            );
        return registry;
    }

    protected virtual async Task<string> RunAsync(
        string executable,
        string? root,
        string[] args,
        string? input,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        var git = executable == GitExecutable;
        var clone = git && GitCloneTransportPolicy.IsClone(args);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(clone ? 180 : 30));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                RedirectStandardInput = input is not null,
                WorkingDirectory = root ?? Environment.CurrentDirectory,
            },
        };
        process.StartInfo.Environment["GH_PROMPT_DISABLED"] = "1";
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.Environment["GCM_INTERACTIVE"] = "Never";
        if (git)
        {
            if (clone)
                GitCloneTransportPolicy.Configure(
                    process.StartInfo,
                    args,
                    "!\"" + GhExecutable.Replace('\\', '/') + "\" auth git-credential"
                );
            foreach (
                var setting in new[]
                {
                    "core.fsmonitor=false",
                    "core.hooksPath=" + NoHooks,
                    "protocol.ext.allow=never",
                    "protocol.file.allow=never",
                }
            )
            {
                process.StartInfo.ArgumentList.Add("-c");
                process.StartInfo.ArgumentList.Add(setting);
            }
        }
        foreach (var argument in args)
            process.StartInfo.ArgumentList.Add(argument);
        var started = false;
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            started = AgentEffectBoundary.Start(() => process.Start());
            if (!started)
                throw new ToolFailureException("unavailable", "The local GitHub or Git operation could not start.");
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var error = TailAsync(process.StandardError, timeout.Token);
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
                process.StandardInput.Close();
            }
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
            var result = await output;
            var errors = await error;
            if (process.ExitCode != 0)
            {
                if (clone)
                    throw new ToolFailureException("clone-failed", "Repository clone failed.", 503, errors);
                if (!git && AuthFailure().IsMatch(errors))
                    throw new ToolFailureException("signed-out", "The GitHub CLI is not signed in.", 401, errors);
                throw new ToolFailureException(
                    "failed",
                    "The local GitHub or Git operation failed. Check the signed-in account and repository access.",
                    503,
                    errors
                );
            }
            return result;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or OperationCanceledException)
        {
            throw new ToolFailureException(
                "unavailable",
                "The local GitHub or Git operation is unavailable or timed out."
            );
        }
        finally
        {
            if (started && !process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (result.Length + count > 1_000_000)
                throw new ToolFailureException(
                    "too-large",
                    "This GitHub response exceeds the field-test size limit.",
                    422
                );
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    // Reads all of stderr but keeps only its first 4 KB, enough to recognize the failure.
    private static async Task<string> TailAsync(StreamReader reader, CancellationToken token)
    {
        var kept = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
            if (kept.Length < 4096)
                kept.Append(buffer, 0, Math.Min(count, 4096 - kept.Length));
        return kept.ToString();
    }

    [GeneratedRegex(
        @"gh auth login|not logged in|authentication required|bad credentials|HTTP 401|token .*(expired|invalid|revoked)|requires authentication|SAML enforcement",
        RegexOptions.IgnoreCase,
        50
    )]
    private static partial Regex AuthFailure();
}
