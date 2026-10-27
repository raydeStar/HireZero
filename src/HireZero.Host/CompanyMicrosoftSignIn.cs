using HireZero.Integrations.Microsoft365;
using Microsoft.AspNetCore.WebUtilities;

public static class CompanyMicrosoftSignIn
{
    public static void MapCompanyMicrosoft365(this WebApplication app, Func<CompanyIntegrationScope> connection)
    {
        var pending = new CompanyConnectionSignIns("microsoft");
        CompanyIntegrationScope Owner(HttpContext context)
        {
            var scope = connection();
            CompanyAccess.Demand(
                scope.Store.Actor((string)context.Items["login"]!),
                CompanyRoles.Owner,
                "manage Microsoft connections"
            );
            return scope;
        }
        app.MapGet(
            "/api/connections/microsoft",
            (HttpContext context) =>
            {
                var scope = Owner(context);
                var settings = scope.MicrosoftStore.Settings();
                var account = scope.MicrosoftStore.Account();
                return Results.Ok(
                    new
                    {
                        configured = settings.ClientId is { Length: > 0 },
                        settings.Mail,
                        settings.Teams,
                        redirectUri = scope.MicrosoftOptions.RedirectUri,
                        companyId = scope.Store.CompanyId,
                        account = account is null
                            ? null
                            : new
                            {
                                account.Address,
                                account.WorkAccount,
                                account.Scopes,
                                account.LastMailSync,
                                account.LastTeamsSync,
                                account.LastError,
                                account.ConnectedAt,
                            },
                    }
                );
            }
        );
        app.MapPut(
            "/api/connections/microsoft",
            (HttpContext context, MicrosoftSettingsInput input) =>
            {
                var scope = Owner(context);
                if (input.ClientId is not null && !Guid.TryParse(input.ClientId.Trim(), out _))
                    throw new WorkspaceException(
                        "The app (client) ID is a GUID from your Entra app registration.",
                        422
                    );
                scope.Change(() =>
                {
                    var current = scope.MicrosoftStore.Settings();
                    var next = new MicrosoftSettings(
                        input.ClientId?.Trim() ?? current.ClientId,
                        input.Mail ?? current.Mail,
                        input.Teams ?? current.Teams
                    );
                    if (next != current)
                        scope.MicrosoftStore.Disconnect();
                    scope.MicrosoftStore.SaveSettings(next);
                    scope.Store.IntegrationEvent(
                        "integration.configured",
                        "microsoft365",
                        "Microsoft settings saved for this company. Changed permissions require a new sign-in."
                    );
                });
                return Results.Ok(new { saved = true });
            }
        );
        app.MapPost(
            "/api/connections/microsoft/start",
            (HttpContext context) =>
            {
                var scope = Owner(context);
                return Results.Ok(
                    new
                    {
                        url = pending.Begin(
                            scope,
                            (string)context.Items["login"]!,
                            () =>
                            {
                                var url = scope.MicrosoftConnector.Begin();
                                return (url, QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString(), "");
                            }
                        ),
                    }
                );
            }
        );
        app.MapDelete(
            "/api/connections/microsoft",
            (HttpContext context) =>
            {
                var scope = Owner(context);
                scope.Change(() =>
                {
                    scope.MicrosoftStore.Disconnect();
                    scope.Store.IntegrationEvent(
                        "integration.disconnected",
                        "microsoft365",
                        "Disconnected Microsoft 365 for this company."
                    );
                });
                return Results.Ok(new { disconnected = true });
            }
        );
        app.MapGet(
            "/oauth/microsoft/callback",
            async (string? code, string? state, string? error, CancellationToken token) =>
                Results.Redirect(
                    await pending.CompleteAsync(
                        state,
                        code,
                        error,
                        async (sign, value, cancellation) =>
                        {
                            await sign.Scope.MicrosoftConnector.CompleteAsync(
                                value,
                                state!,
                                cancellation,
                                sign.DemandCurrent
                            );
                            sign.Scope.Store.IntegrationEvent(
                                "integration.connected",
                                "microsoft365",
                                "Signed in with Microsoft 365 for this company."
                            );
                        },
                        token
                    )
                )
        );
    }
}
