using System.Net;
using HireZero.Tools.Wiki;

namespace HireZero.Integrations.Wiki;

// The wiki.* tools: the local Wiki pilot on an HTTP loopback origin. Each session has its own cookie jar; the pilot
// sign-in header, Origin and CSRF headers are added here, never by callers.
public sealed class WikiToolProvider(IHttpClientFactory clients) : IToolProvider
{
    public void Register(ToolRegistry registry)
    {
        registry.Register(
            WikiTools.Origin,
            (_, _) => Task.FromResult(WikiRegistration.Origin().GetLeftPart(UriPartial.Authority))
        );
        foreach (var contract in new[] { WikiTools.Read, WikiTools.Contribute, WikiTools.Publish })
            registry.Register(
                contract,
                (call, token) =>
                    WikiTools.Classify(call) == contract
                        ? SendAsync(call, token)
                        : throw new ToolFailureException(
                            "refused",
                            "This Wiki request is not a " + contract.Id + " request.",
                            409
                        )
            );
    }

    private async Task<ServiceReply> SendAsync(WikiCall call, CancellationToken token)
    {
        try
        {
            using var client = clients.CreateClient(WikiRegistration.Client(call.Session));
            var path = call.Path.TrimStart('/');
            using var request = new HttpRequestMessage(new HttpMethod(call.Method), path);
            if (call.Method == "POST" && path.StartsWith("api/pilot/session?", StringComparison.Ordinal))
                request.Headers.Add("X-Wiki-Pilot", "local-demo");
            else if (call.Method == "POST")
                request.Headers.Add("Origin", WikiRegistration.Origin().GetLeftPart(UriPartial.Authority));
            if (call.Csrf is { Length: > 0 } csrf)
                request.Headers.Add("X-CSRF-TOKEN", csrf);
            if (call.JsonBody is not null)
                request.Content = new StringContent(call.JsonBody, System.Text.Encoding.UTF8, "application/json");
            using var response = await AgentEffectBoundary.Start(() => client.SendAsync(request, token));
            return new((int)response.StatusCode, await ReadBoundedAsync(response, token));
        }
        catch (Exception error)
            when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            throw new ToolFailureException("unavailable", "The Wiki is unavailable or the request outcome is unknown.");
        }
    }

    // Reads at most 2 MB of the response body.
    internal static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes, token)) != 0)
        {
            if (buffer.Length + count > 2 * 1024 * 1024)
                throw new ToolFailureException("too-large", "The service response exceeds the size limit.", 503);
            buffer.Write(bytes, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}

public static class WikiRegistration
{
    public static string Client(string session) =>
        session switch
        {
            "context" => "wiki",
            "contribution" => "wiki-contribution",
            "publication" => "wiki-publication",
            _ => throw new ToolFailureException("refused", "Unknown Wiki session.", 409),
        };

    // HIREZERO_WIKI_URL, which must be an HTTP loopback origin.
    public static Uri Origin()
    {
        var url = Environment.GetEnvironmentVariable("HIREZERO_WIKI_URL") ?? "http://127.0.0.1:5094";
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var origin)
            || origin.Scheme != "http"
            || !origin.IsLoopback
            || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
        )
            throw new InvalidOperationException("The Wiki pilot must use an HTTP loopback origin.");
        return origin;
    }

    public static IServiceCollection AddWiki(this IServiceCollection services)
    {
        foreach (var session in WikiTools.Sessions)
            services
                .AddHttpClient(
                    Client(session),
                    client =>
                    {
                        client.BaseAddress = Origin();
                        client.Timeout = TimeSpan.FromSeconds(session == "context" ? 5 : 15);
                    }
                )
                // One long-lived handler per session keeps its sign-in cookies across the calls of a session.
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new HttpClientHandler
                    {
                        UseCookies = true,
                        CookieContainer = new CookieContainer(),
                        AllowAutoRedirect = false,
                    }
                );
        return services.AddSingleton<IToolProvider, WikiToolProvider>();
    }
}
