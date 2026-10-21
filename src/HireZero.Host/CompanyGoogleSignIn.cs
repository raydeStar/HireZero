using System.Collections.Concurrent;
using HireZero.Integrations.Google;

// Signing in with Google for read-only Analytics and Search Console (docs/adr/0049-google-sign-in.md). The owner's
// click starts it; Google returns the browser to the loopback callback with a one-time state that must match a
// pending sign-in from this host; the refresh token is stored encrypted like a pasted key and never shown. Local mode.
public static class CompanyGoogleSignIn
{
    public const string CallbackPath = "/oauth/google/callback";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static void MapGoogleSignIn(
        this WebApplication app,
        GoogleAnalyticsProvider google,
        LocalSecretStore? secrets,
        Func<CompanyStore> store,
        Action activate,
        string origin
    )
    {
        var pending = new ConcurrentDictionary<string, (string Verifier, DateTimeOffset Expires)>();
        var redirect = origin + CallbackPath;
        const string ReturnTo = "/?view=departments&section=integrations&google=";

        app.MapGet(
            "/api/connections/google",
            () =>
                Results.Ok(
                    new
                    {
                        configured = google.OAuth.Configured,
                        signedIn = google.OAuth.SignedIn,
                        redirectUri = redirect,
                        local = secrets is not null,
                    }
                )
        );
        app.MapPost(
            "/api/connections/google/start",
            () =>
            {
                if (secrets is null)
                    throw new WorkspaceException("Google sign-in is available in local mode.", 403);
                if (!google.OAuth.Configured)
                    throw new WorkspaceException("Add your Google OAuth client ID and secret first.", 409);
                foreach (var stale in pending.Where(p => p.Value.Expires < DateTimeOffset.UtcNow).Select(p => p.Key))
                    pending.TryRemove(stale, out _);
                var (url, state, verifier) = google.OAuth.Begin(redirect);
                pending[state] = (verifier, DateTimeOffset.UtcNow + Window);
                return Results.Ok(new { url });
            }
        );
        // Not under /api: the browser arrives here from Google. The one-time state, not a session, authorizes it.
        app.MapGet(
            CallbackPath,
            async (string? code, string? state, string? error, CancellationToken token) =>
            {
                if (
                    error is not null
                    || code is null
                    || state is null
                    || !pending.TryRemove(state, out var sign)
                    || sign.Expires < DateTimeOffset.UtcNow
                    || secrets is null
                )
                    return Results.Redirect(ReturnTo + Uri.EscapeDataString(error ?? "expired"));
                try
                {
                    var refresh = await google.OAuth.CompleteAsync(code, sign.Verifier, redirect, token);
                    secrets.Write(GoogleOAuth.RefreshTokenName, refresh);
                    store().IntegrationEvent("integration.connected", "google-analytics", "Signed in with Google.");
                    activate();
                    return Results.Redirect(ReturnTo + "connected");
                }
                catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException)
                {
                    return Results.Redirect(ReturnTo + Uri.EscapeDataString(failure.Message));
                }
            }
        );
        app.MapGet(
            "/api/connections/google/choices",
            async (CancellationToken token) =>
            {
                var (properties, sites, problems) = await google.ChoicesAsync(token);
                return Results.Ok(
                    new
                    {
                        properties,
                        sites,
                        problems,
                    }
                );
            }
        );
        app.MapDelete(
            "/api/connections/google",
            () =>
            {
                secrets?.Remove(GoogleOAuth.RefreshTokenName);
                store().IntegrationEvent("integration.secret", "google-analytics", "Signed out of Google.");
                activate();
                return Results.Ok(new { signedOut = true });
            }
        );
    }
}
