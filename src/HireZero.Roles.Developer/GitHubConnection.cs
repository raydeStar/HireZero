using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HireZero.Tools.GitHub;

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

// Reconnect runs the GitHub CLI's own browser sign-in on this computer through the github.sign-in tool, approved by
// the owner's click (OwnerActionApprovals). HireZero never sees a password or token.
public sealed class GitHubReconnect(ToolRegistry? registry = null, OwnerActionApprovals? owner = null)
{
    private readonly IToolbox? _tools = registry?.For(DeveloperModule.Shared);

    public async Task<object> StartAsync(CancellationToken token)
    {
        var tools =
            _tools ?? throw new ReviewAccessException("GitHub sign-in is not available in this workspace.", 503);
        var approvals =
            owner ?? throw new ReviewAccessException("Owner approvals are not available in this workspace.", 503);
        var request = new GitHubSignInRequest();
        return Shape(
            await tools.InvokeAsync(
                GitHubSignInTools.Start,
                request,
                token,
                approvals.Approve(GitHubSignInTools.Start, request)
            )
        );
    }

    public async Task<object> StatusAsync(CancellationToken token) =>
        _tools is null
            ? Shape(new("idle", "", "https://github.com/login/device", ""))
            : Shape(await _tools.InvokeAsync(GitHubSignInTools.Status, new(), token));

    private static object Shape(GitHubSignInStatus status)
    {
        if (status.State == "connected")
            GitHubHealth.MarkOk();
        return new
        {
            state = status.State,
            code = status.Code,
            url = status.Url,
            error = status.Error,
        };
    }
}
