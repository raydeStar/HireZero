using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HireZero.Roles.Developer;

public sealed class GitHubSignInException(string message) : ReviewAccessException(message, 401);

// Whether the GitHub CLI is still signed in, as seen by the last gh call. A sign-in failure is reported once,
// in the Cockpit, the daily brief and any PR conversation with work in flight, until a call succeeds again.
public static class GitHubHealth
{
    private static readonly object Gate = new();
    public static string? SignedOutSince { get; private set; }
    public static string? Reason { get; private set; }
    public static event Action? SignedOut;
    public const string Message =
        "GitHub isn't signed in anymore, so PR work is paused. Reconnect GitHub from the Cockpit (or run `gh auth login` in a terminal); nothing was lost.";

    static readonly Regex AuthFailure = new(
        @"gh auth login|not logged in|authentication required|bad credentials|HTTP 401|token .*(expired|invalid|revoked)|requires authentication|SAML enforcement",
        RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(50)
    );

    public static bool LooksSignedOut(string? error) => error is { Length: > 0 } && AuthFailure.IsMatch(error);

    public static void MarkSignedOut(string reason)
    {
        bool changed;
        lock (Gate)
        {
            changed = SignedOutSince is null;
            SignedOutSince ??= DateTimeOffset.UtcNow.ToString("O");
            Reason = reason;
        }
        if (!changed)
            return;
        AgentActivity.Note("GitHub sign-in expired; PR work is paused until you reconnect.");
        // A listener that fails (for example a store that is shutting down) never breaks the GitHub call that noticed it.
        foreach (var listener in SignedOut?.GetInvocationList() ?? [])
            try
            {
                ((Action)listener)();
            }
            catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    public static void MarkOk()
    {
        lock (Gate)
        {
            SignedOutSince = null;
            Reason = null;
        }
    }

    public static object Status()
    {
        lock (Gate)
            return new
            {
                signedIn = SignedOutSince is null,
                since = SignedOutSince,
                reason = Reason,
            };
    }
}

// Reconnect runs the GitHub CLI's own browser sign-in on this computer: it shows a one-time code, opens
// github.com/login/device, and waits for you to approve. HireZero never sees a password or token.
public sealed class GitHubReconnect
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

    public static string? ParseCode(string text) =>
        Code.Match(text) is { Success: true } match ? match.Groups[1].Value : null;

    public object Status()
    {
        lock (_gate)
            return new
            {
                state = _state,
                code = _code,
                url = "https://github.com/login/device",
                error = _error,
            };
    }

    public object Start()
    {
        lock (_gate)
        {
            if (_state == "waiting" && _process is { HasExited: false } && DateTimeOffset.UtcNow - _started < Window)
                return Status();
            _state = "starting";
            _code = "";
            _error = "";
            _started = DateTimeOffset.UtcNow;
            var start = new ProcessStartInfo("gh")
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
                GitHubHealth.MarkOk();
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
