using HireZero.Tools.SemaForge;

namespace HireZero.Integrations.SemaForge;

// The semaforge.* tools: the owner's local SemaForge service on loopback, with its optional API key. Requests start
// through AgentEffectBoundary, so a caller's scope is checked again at the moment the request is sent.
public sealed class SemaForgeToolProvider(IHttpClientFactory clients) : IToolProvider
{
    public const string Client = "semaforge";

    public void Register(ToolRegistry registry)
    {
        foreach (var contract in new[] { SemaForgeTools.Read, SemaForgeTools.Write })
            registry.Register(
                contract,
                (call, token) =>
                    SemaForgeTools.Classify(call) == contract
                        ? SendAsync(call, token)
                        : throw new ToolFailureException(
                            "refused",
                            "This SemaForge request is not a " + contract.Id + " request.",
                            409
                        )
            );
    }

    private async Task<ServiceReply> SendAsync(ServiceCall call, CancellationToken token)
    {
        try
        {
            using var client = clients.CreateClient(Client);
            if (Environment.GetEnvironmentVariable("HIREZERO_SEMAFORGE_API_KEY") is { Length: > 0 } key)
                client.DefaultRequestHeaders.Add("X-SemaForge-Api-Key", key);
            using var request = new HttpRequestMessage(new HttpMethod(call.Method), call.Path);
            if (call.JsonBody is not null)
                request.Content = new StringContent(call.JsonBody, System.Text.Encoding.UTF8, "application/json");
            using var response = await AgentEffectBoundary.Start(() => client.SendAsync(request, token));
            return new((int)response.StatusCode, await ReadBoundedAsync(response, token));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            throw new ToolFailureException(
                "unavailable",
                "SemaForge is unavailable or the request outcome is unknown."
            );
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

public static class SemaForgeRegistration
{
    public static IServiceCollection AddSemaForge(this IServiceCollection services)
    {
        services.AddHttpClient(
            SemaForgeToolProvider.Client,
            client => client.BaseAddress = new Uri("http://127.0.0.1:5058")
        );
        return services.AddSingleton<IToolProvider, SemaForgeToolProvider>();
    }
}
