using HireZero.Core;
using HireZero.Integrations.Stripe;
using Microsoft.AspNetCore.Http.Features;

public static class CompanyBillingEndpoints
{
    public static void Map(WebApplication app, Func<CompanyStore> store, CompanyBillingRuntime? runtime)
    {
        app.MapGet(
            "/api/company/billing",
            (HttpContext context) =>
            {
                DemandOwner(context, store());
                var billing = store().Billing();
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(
                    new
                    {
                        billing.Required,
                        billing.Configured,
                        billing.State,
                        billing.Plans,
                        billing.Checkout,
                        billing.Allowance,
                        portal = store().BillingPortal(),
                        enabled = runtime?.PaymentsEnabled == true,
                        liveMode = runtime?.LiveMode == true,
                        usage = store().Usage().Current,
                        compute = store().ComputeUsage(),
                    }
                );
            }
        );
        app.MapGet(
            "/api/company/billing/checkouts/{id}",
            (string id, HttpContext context) =>
            {
                DemandOwner(context, store());
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(store().BillingCheckout(id));
            }
        );
        app.MapPost(
            "/api/company/billing/refresh",
            async (HttpContext context) =>
            {
                DemandOwner(context, store());
                if (runtime is null)
                    throw new WorkspaceException(
                        "Billing is unavailable. Saved subscription records are retained.",
                        409
                    );
                try
                {
                    var refreshed = await app
                        .Services.GetRequiredService<CompanyBillingService>()
                        .RefreshAsync(store(), context.RequestAborted);
                    return Results.Ok(new { refreshed });
                }
                catch (BillingProviderException)
                {
                    return Results.Json(
                        new
                        {
                            error = "Payment could not be verified yet. Check status again; the original checkout will not be repeated.",
                        },
                        statusCode: 503
                    );
                }
            }
        );
    }

    private static void DemandOwner(HttpContext context, CompanyStore store) =>
        CompanyAccess.Demand(
            store.Actor((string)context.Items["login"]!),
            CompanyRoles.Owner,
            "view or manage company billing"
        );

    // Called only for this exact POST after validating the listener/public origin. It authenticates Stripe, not a user cookie.
    public static async Task ReceiveAsync(HttpContext context, CompanyBillingRuntime? runtime, CompanyStore inbox)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (runtime is null)
        {
            context.Response.StatusCode = 503;
            return;
        }
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = StripeWebhook.MaximumBodyBytes;
        if (!context.Request.HasJsonContentType() || context.Request.Headers["Stripe-Signature"].Count != 1)
        {
            context.Response.StatusCode = 400;
            return;
        }
        if (context.Request.ContentLength > StripeWebhook.MaximumBodyBytes)
        {
            context.Response.StatusCode = 413;
            // An unread oversized body cannot share its connection with the next request.
            if (context.Request.Protocol is "HTTP/1.0" or "HTTP/1.1")
                context.Response.Headers.Connection = "close";
            return;
        }
        try
        {
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (body.Length + count > StripeWebhook.MaximumBodyBytes)
                {
                    context.Response.StatusCode = 413;
                    return;
                }
                body.Write(buffer, 0, count);
            }
            var notice = runtime.Verify(
                body.ToArray(),
                context.Request.Headers["Stripe-Signature"].ToString(),
                DateTimeOffset.UtcNow
            );
            if (notice is not null)
                inbox.QueueBillingEvent(runtime.Provider.Identity, notice);
            context.Response.StatusCode = 200;
            await context.Response.WriteAsJsonAsync(new { received = true });
        }
        catch (BillingProviderException)
        {
            context.Response.StatusCode = 400;
        }
        catch (BadHttpRequestException error) when (error.StatusCode == 413)
        {
            context.Response.StatusCode = 413;
            if (context.Request.Protocol is "HTTP/1.0" or "HTTP/1.1")
                context.Response.Headers.Connection = "close";
        }
        catch (WorkspaceException error)
        {
            context.Response.StatusCode = error.Status;
        }
    }
}
