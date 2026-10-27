using HireZero.Integrations.Google;

public static class CompanyGoogleSignIn
{
    public const string CallbackPath = "/oauth/google/callback";

    public static void MapGoogleSignIn(this WebApplication app, Func<CompanyIntegrationScope> connection, string origin)
    {
        var pending = new CompanyConnectionSignIns("google");
        var redirect = origin + CallbackPath;
        CompanyIntegrationScope Owner(HttpContext context)
        {
            var scope = connection();
            CompanyAccess.Demand(
                scope.Store.Actor((string)context.Items["login"]!),
                CompanyRoles.Owner,
                "manage Google connections"
            );
            return scope;
        }
        app.MapGet(
            "/api/connections/google",
            (HttpContext context) =>
            {
                var scope = Owner(context);
                return Results.Ok(
                    new
                    {
                        configured = scope.Google.OAuth.Configured,
                        signedIn = scope.Google.OAuth.SignedIn,
                        redirectUri = redirect,
                        local = scope.PastedSecrets is not null,
                        companyId = scope.Store.CompanyId,
                    }
                );
            }
        );
        app.MapPost(
            "/api/connections/google/start",
            (HttpContext context) =>
            {
                var scope = Owner(context);
                if (scope.PastedSecrets is null)
                    throw new WorkspaceException("Google sign-in is available in local mode.", 403);
                if (!scope.Google.OAuth.Configured)
                    throw new WorkspaceException("Add your Google OAuth client ID and secret first.", 409);
                return Results.Ok(
                    new
                    {
                        url = pending.Begin(
                            scope,
                            (string)context.Items["login"]!,
                            () => scope.Google.OAuth.Begin(redirect)
                        ),
                    }
                );
            }
        );
        app.MapGet(
            CallbackPath,
            async (string? code, string? state, string? error, CancellationToken token) =>
                Results.Redirect(
                    await pending.CompleteAsync(
                        state,
                        code,
                        error,
                        async (sign, value, cancellation) =>
                        {
                            var refresh = await sign.Scope.Google.OAuth.CompleteAsync(
                                value,
                                sign.Verifier,
                                redirect,
                                cancellation
                            );
                            sign.DemandCurrent();
                            sign.Scope.PastedSecrets!.Write(GoogleOAuth.RefreshTokenName, refresh);
                            sign.Scope.Store.IntegrationEvent(
                                "integration.connected",
                                "google-analytics",
                                "Signed in with Google for this company."
                            );
                        },
                        token
                    )
                )
        );
        app.MapGet(
            "/api/connections/google/choices",
            async (HttpContext context, CancellationToken token) =>
            {
                var scope = Owner(context);
                var (properties, sites, problems) = await scope.Google.ChoicesAsync(token);
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
            (HttpContext context) =>
            {
                var scope = Owner(context);
                if (scope.PastedSecrets is null)
                    throw new WorkspaceException(
                        "Remove the refresh token from this company's credential source.",
                        403
                    );
                scope.Change(() =>
                {
                    if (
                        scope.Secrets.Describe(GoogleOAuth.RefreshTokenName).Origin
                        is not (SecretOrigin.App or SecretOrigin.Missing)
                    )
                        throw new WorkspaceException(
                            "The Google refresh token is managed outside the app. Remove it from its credential source to sign out.",
                            409
                        );
                    scope.PastedSecrets.Remove(GoogleOAuth.RefreshTokenName);
                    scope.Store.IntegrationEvent(
                        "integration.secret",
                        "google-analytics",
                        "Signed out of Google for this company."
                    );
                });
                return Results.Ok(new { signedOut = true, analyticsActive = scope.Google.Status().Active });
            }
        );
    }
}
