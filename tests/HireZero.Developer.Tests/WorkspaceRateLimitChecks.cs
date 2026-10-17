using System.Net;
using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class WorkspaceRateLimitChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddWorkspaceRateLimits();
        await using var app = builder.Build();
        var agentStarts = 0;
        app.Use(
            async (context, next) =>
            {
                // Synthetic authenticated identities; the real host verifies identity before this limiter.
                context.Items["login"] = context.Request.Headers["Test-Login"].ToString();
                await next();
            }
        );
        app.UseRateLimiter();
        app.MapGet("/api/read", () => Results.Ok(new { read = true }));
        app.MapPost("/api/write", () => Results.Ok(new { saved = true }));
        app.MapPost(
                "/api/agent",
                () =>
                {
                    agentStarts++;
                    return Results.Ok(new { started = true });
                }
            )
            .RequireRateLimiting("agent");
        app.MapGet("/assets/app.js", () => Results.Text("synthetic"));
        await app.StartAsync();
        try
        {
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            async Task<HttpResponseMessage> Send(
                string login,
                string path,
                HttpMethod? method = null,
                string? rawIdentity = null
            )
            {
                using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
                request.Headers.Add("Test-Login", login);
                if (rawIdentity is not null)
                    request.Headers.Add("Tailscale-User-Login", rawIdentity);
                return await client.SendAsync(request);
            }

            var allReadsSucceeded = true;
            for (var i = 0; i < 600; i++)
            {
                using var response = await Send("reader", "/api/read");
                allReadsSucceeded &= response.IsSuccessStatusCode;
            }
            check(allReadsSucceeded, "Several chat tabs' normal polling must fit without consuming action capacity.");
            using (var response = await Send("reader", "/api/write", HttpMethod.Post))
                check(response.IsSuccessStatusCode, "Polling must not prevent a saved message or action.");
            using (var response = await Send("reader", "/api/read"))
            {
                check(
                    response.StatusCode == HttpStatusCode.TooManyRequests,
                    "Read requests must still have a bounded limit."
                );
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                check(
                    problem.RootElement.GetProperty("code").GetString() == "workspace_rate_limited",
                    "A local rate limit must identify itself, not look like an OpenAI outage."
                );
                check(
                    problem.RootElement.GetProperty("retryAfterSeconds").GetInt32() is > 0 and <= 60
                        && response.Headers.RetryAfter?.Delta is { TotalSeconds: > 0 },
                    "A rejection must give the client an explicit retry delay."
                );
                check(
                    problem.RootElement.GetProperty("error").GetString()!.Contains("Try again"),
                    "Rate-limit failures need a readable recovery message."
                );
            }
            for (var i = 0; i < 3; i++)
            {
                using var response = await Send("reader", "/api/agent", HttpMethod.Post);
                check(
                    response.IsSuccessStatusCode,
                    "Background polling must not consume the separate model-call allowance."
                );
            }
            using (
                var response = await Send(
                    "reader",
                    "/api/agent",
                    HttpMethod.Post,
                    "different-raw-header@fixture.invalid"
                )
            )
                check(
                    response.StatusCode == HttpStatusCode.TooManyRequests,
                    "Changing an unverified header must not bypass the authenticated identity's action limit."
                );
            check(agentStarts == 3, "A rejected request must not start a model call.");
            using (var response = await Send("other", "/api/agent", HttpMethod.Post))
                check(response.IsSuccessStatusCode, "Different authenticated users must have independent allowances.");
            var allWritesSucceeded = true;
            for (var i = 0; i < 60; i++)
            {
                using var response = await Send("writer", "/api/write", HttpMethod.Post);
                allWritesSucceeded &= response.IsSuccessStatusCode;
            }
            check(allWritesSucceeded, "Mutation capacity must be independent of read capacity.");
            using (var response = await Send("writer", "/api/write", HttpMethod.Post))
                check(response.StatusCode == HttpStatusCode.TooManyRequests, "Mutation floods must remain bounded.");
            using (var response = await Send("writer", "/api/read"))
                check(response.IsSuccessStatusCode, "A mutation limit must not block status and recovery reads.");
            var assetsSucceeded = true;
            for (var i = 0; i < 70; i++)
            {
                using var response = await Send("reader", "/assets/app.js");
                assetsSucceeded &= response.IsSuccessStatusCode;
            }
            check(assetsSucceeded, "Page assets must remain loadable even while an API allowance is exhausted.");
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
