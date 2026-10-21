using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HireZero.Tools.GitHub;

namespace HireZero.Integrations.GitHub;

// Runs the GitHub CLI's own browser sign-in on this computer: it shows a one-time code, opens
// github.com/login/device, and waits for the owner to approve. HireZero never sees a password or token.
public sealed class GitHubSignIn
{
    private readonly object _gate = new();
    private Process? _process;
    private string _state = "idle",
        _code = "",
        _error = "";
    private DateTimeOffset _started;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    static readonly Regex Code = new(
        @"\b([A-Z0-9]{4}-[A-Z0-9]{4})\b",
        RegexOptions.None,
        TimeSpan.FromMilliseconds(50)
    );

    static readonly Regex Account = new(
        @"Logged in to github\.com account (\S+)",
        RegexOptions.None,
        TimeSpan.FromMilliseconds(50)
    );

    // An existing sign-in on this computer (owner feedback, 2026-10-20: the page asked to sign in while the CLI was
    // already signed in). Checked with `gh auth status`, which never prints the token, and cached briefly.
    private readonly Func<string?> _signedIn;
    private (DateTimeOffset At, string? Account) _known = (DateTimeOffset.MinValue, null);
    public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(30);

    public GitHubSignIn(Func<string?>? signedIn = null) => _signedIn = signedIn ?? SignedInAccount;

    public static string? ParseCode(string text) =>
        Code.Match(text) is { Success: true } match ? match.Groups[1].Value : null;

    public static string? ParseAccount(string text) =>
        Account.Match(text) is { Success: true } match ? match.Groups[1].Value : null;

    private static string? SignedInAccount()
    {
        try
        {
            var start = new ProcessStartInfo(GitHubCli.GhExecutable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "auth", "status", "--hostname", "github.com" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return process.ExitCode == 0 ? ParseAccount(output.Result + error.Result) : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    public ToolRegistry Register(ToolRegistry registry)
    {
        registry.Register(GitHubSignInTools.Start, (_, _) => Task.FromResult(Start()));
        registry.Register(GitHubSignInTools.Status, (_, _) => Task.FromResult(Status()));
        return registry;
    }

    public GitHubSignInStatus Status()
    {
        lock (_gate)
        {
            // A sign-in in progress speaks for itself; otherwise the computer's existing sign-in decides.
            if (_state is "starting" or "waiting")
                return new(_state, _code, "https://github.com/login/device", _error);
            if (DateTimeOffset.UtcNow - _known.At > Recheck)
                _known = (DateTimeOffset.UtcNow, _signedIn());
            return _known.Account is { } account
                ? new("connected", "", "https://github.com/login/device", "", account)
                : new(_state == "connected" ? "idle" : _state, _code, "https://github.com/login/device", _error);
        }
    }

    public GitHubSignInStatus Start()
    {
        lock (_gate)
        {
            if (_state == "waiting" && _process is { HasExited: false } && DateTimeOffset.UtcNow - _started < Window)
                return Status();
            _state = "starting";
            _code = "";
            _error = "";
            _started = DateTimeOffset.UtcNow;
            var start = new ProcessStartInfo(GitHubCli.GhExecutable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (
                var argument in new[]
                {
                    "auth",
                    "login",
                    "--hostname",
                    "github.com",
                    "--web",
                    "--git-protocol",
                    "https",
                }
            )
                start.ArgumentList.Add(argument);
            try
            {
                _process = Process.Start(start) ?? throw new InvalidOperationException();
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _state = "failed";
                _error = "The GitHub CLI (gh) isn't installed or couldn't start.";
                return Status();
            }
            var process = _process;
            _ = Task.Run(() => WatchAsync(process));
        }
        // Give gh a moment to print its one-time code.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            lock (_gate)
                if (_code.Length > 0 || _state is "failed" or "connected")
                    break;
            Thread.Sleep(150);
        }
        return Status();
    }

    private async Task WatchAsync(Process process)
    {
        var output = new StringBuilder();
        async Task Read(StreamReader reader)
        {
            var buffer = new char[512];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0)
            {
                lock (_gate)
                {
                    if (output.Length < 8000)
                        output.Append(buffer, 0, count);
                    if (_code.Length == 0 && ParseCode(output.ToString()) is { } code)
                    {
                        _code = code;
                        _state = "waiting";
                        // gh waits for Enter before opening the browser and polling.
                        try
                        {
                            process.StandardInput.WriteLine();
                            process.StandardInput.Flush();
                        }
                        catch (IOException) { }
                    }
                }
            }
        }
        using var limit = new CancellationTokenSource(Window);
        try
        {
            await Task.WhenAll(
                Read(process.StandardOutput),
                Read(process.StandardError),
                process.WaitForExitAsync(limit.Token)
            );
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
        }
        lock (_gate)
        {
            if (process.HasExited && process.ExitCode == 0)
            {
                _state = "connected";
                _known = (DateTimeOffset.MinValue, null);
            }
            else
            {
                _state = "failed";
                _error = limit.IsCancellationRequested
                    ? "The sign-in wasn't approved within 15 minutes."
                    : "GitHub sign-in didn't finish. Try again, or run `gh auth login` in a terminal.";
            }
        }
        process.Dispose();
    }
}
