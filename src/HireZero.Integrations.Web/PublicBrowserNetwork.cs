using System.Net;
using System.Text;

namespace HireZero.Integrations.Web;

public sealed partial class WebReader
{
    internal static SemaphoreSlim BrowserResearchGate => ResearchGate;

    internal static Task PaceBrowserAsync(CancellationToken token, double delay) => PaceResearchAsync(token, delay);

    // A browser's broker cannot inherit cookies, credentials or an ambient proxy that resolves private addresses.
    internal static HttpMessageHandler BrowserHandler() =>
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectPublicAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
}

// One company job owns one short-lived broker. The browser itself will have no network interface.
public sealed class PublicBrowserNetwork : IBrowserNetworkSession
{
    private readonly HashSet<string> _origins;
    private readonly BrowserLimits _limits;
    private readonly Func<bool> _authorized;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _robots = new(StringComparer.Ordinal);
    private int _requests,
        _calls,
        _refused;
    private long _bytes;

    public PublicBrowserNetwork(BrowserRequest scope, Func<bool> stillAuthorized, HttpMessageHandler? handler = null)
    {
        scope.Validate();
        _origins = scope
            .Origins.Select(BrowserRequest.Address)
            .Select(BrowserRequest.Origin)
            .ToHashSet(StringComparer.Ordinal);
        _limits = scope.Limits;
        _authorized = stillAuthorized;
        _http = new HttpClient(handler ?? WebReader.BrowserHandler()) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HireZero-source-reader/1.0 (+https://hirezero.app)");
        _lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(_limits.Seconds));
    }

    public BrowserNetworkUsage Usage =>
        new(Volatile.Read(ref _requests), Interlocked.Read(ref _bytes), Volatile.Read(ref _refused));

    private void Current(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_authorized())
            throw new WorkspaceException("Browser authority was revoked.", 403);
    }

    private Uri Allowed(string address)
    {
        var uri = BrowserRequest.Address(address);
        WebReader.Checked(address);
        if (!_origins.Contains(BrowserRequest.Origin(uri)))
            throw new WorkspaceException("This request leaves the approved browser websites.", 403);
        return uri;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, double delay, CancellationToken token)
    {
        Current(token);
        if (_requests >= _limits.Requests || _bytes >= _limits.Bytes)
            throw new WorkspaceException("The browser request or response-body allowance is exhausted.", 429);
        await WebReader.PaceBrowserAsync(token, delay);
        Current(token);
        Interlocked.Increment(ref _requests);
        // One courteous knock at a time. Even a refused response consumes its request allowance.
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        return await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
    }

    private async Task<byte[]> BodyAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        Current(token);
        var available = (int)Math.Min(maximum, Math.Max(0, _limits.Bytes - _bytes));
        if (response.Content.Headers.ContentLength is { } length && length > available)
            throw new WorkspaceException("The browser response exceeds its remaining body allowance.", 413);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[Math.Min(16 * 1024, available + 1)];
        while (true)
        {
            Current(token);
            var count = await stream.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, available + 1 - (int)output.Length)),
                token
            );
            if (count == 0)
                break;
            Interlocked.Add(ref _bytes, count);
            output.Write(buffer, 0, count);
            if (output.Length > available)
                throw new WorkspaceException("The browser response exceeds its body allowance.", 413);
        }
        Current(token);
        return output.ToArray();
    }

    private async Task<(bool Allowed, double Delay)> RulesAsync(Uri uri, CancellationToken token)
    {
        var origin = BrowserRequest.Origin(uri);
        if (!_robots.TryGetValue(origin, out var policy))
        {
            var target = new Uri(origin + "/robots.txt");
            policy = "";
            for (var hop = 0; ; hop++)
            {
                using var response = await SendAsync(target, 1, token);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    break;
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    if (hop >= WebReader.MaxRedirects)
                        throw new WorkspaceException("Browser crawl policy redirected too many times.", 422);
                    target = Allowed(new Uri(target, location).AbsoluteUri);
                    if (BrowserRequest.Origin(target) != origin)
                        throw new WorkspaceException("Browser crawl policy left its website.", 403);
                    continue;
                }
                if (
                    !response.IsSuccessStatusCode
                    || !string.Equals(
                        response.Content.Headers.ContentType?.MediaType,
                        "text/plain",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    throw new WorkspaceException(
                        "The website's crawl policy is unavailable or access is restricted.",
                        403
                    );
                policy = new UTF8Encoding(false, true).GetString(await BodyAsync(response, 128 * 1024, token));
                break;
            }
            _robots.Add(origin, policy);
        }
        var rules = WebReader.ResearchRules(policy, uri);
        if (!rules.Allowed || rules.Delay > 10)
            throw new WorkspaceException(
                "The website's crawl policy excludes this browser request or requires a longer delay.",
                403
            );
        return rules;
    }

    public async Task<BrowserResourceReply> FetchAsync(BrowserResourceRequest request, CancellationToken token)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var acquired = false;
        var researchAcquired = false;
        try
        {
            await _gate.WaitAsync(cancel.Token);
            acquired = true;
            Current(cancel.Token);
            if (++_calls > _limits.Requests)
                throw new WorkspaceException("The browser resource allowance is exhausted.", 429);
            if (request is null || request.Method != "GET")
                throw new WorkspaceException("Browser work may only read public resources with GET.", 403);
            var uri = Allowed(request.Url);
            await WebReader.BrowserResearchGate.WaitAsync(cancel.Token);
            researchAcquired = true;
            var rules = await RulesAsync(uri, cancel.Token);
            using var response = await SendAsync(uri, rules.Delay, cancel.Token);
            Current(cancel.Token);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                headers["Location"] = Allowed(new Uri(uri, location).AbsoluteUri).AbsoluteUri;
                return new(status, headers, "", DateTimeOffset.UtcNow);
            }
            if (status is 401 or 403 or 407 or 429 || response.Content.Headers.ContentDisposition is not null)
                throw new WorkspaceException(
                    "The website requires access, rate-limit handling or a download outside this public reading job.",
                    403
                );
            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (
                !(
                    type.StartsWith("text/", StringComparison.Ordinal)
                    || type.StartsWith("font/", StringComparison.Ordinal)
                    || type
                        is "application/javascript"
                            or "application/json"
                            or "application/xml"
                            or "application/xhtml+xml"
                            or "image/png"
                            or "image/jpeg"
                            or "image/gif"
                            or "image/webp"
                            or "image/svg+xml"
                            or "image/x-icon"
                            or "application/font-woff"
                            or "application/vnd.ms-fontobject"
                )
            )
                throw new WorkspaceException("This resource type is outside the browser's public-page scope.", 415);
            headers["Content-Type"] = response.Content.Headers.ContentType!.ToString();
            headers["Cache-Control"] = "no-store";
            foreach (
                var name in new[]
                {
                    "Content-Security-Policy",
                    "X-Frame-Options",
                    "Cross-Origin-Resource-Policy",
                    "Cross-Origin-Opener-Policy",
                    "Cross-Origin-Embedder-Policy",
                    "Access-Control-Allow-Origin",
                    "Access-Control-Allow-Methods",
                    "Access-Control-Allow-Headers",
                    "Permissions-Policy",
                }
            )
                if (response.Headers.TryGetValues(name, out var values))
                {
                    var value = string.Join(", ", values);
                    if (value.Length <= 8192)
                        headers[name] = value;
                }
            var body = await BodyAsync(response, BrowserRequest.ResourceBytes, cancel.Token);
            return new(status, headers, Convert.ToBase64String(body), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            return Refused("The browser was canceled or its deadline expired.");
        }
        catch (WorkspaceException error)
        {
            return Refused(error.Message);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or DecoderFallbackException)
        {
            return Refused("The public browser resource could not be read. No retry was attempted.");
        }
        finally
        {
            if (researchAcquired)
                WebReader.BrowserResearchGate.Release();
            if (acquired)
                _gate.Release();
        }
    }

    private BrowserResourceReply Refused(string reason)
    {
        Interlocked.Increment(ref _refused);
        return new(0, [], "", DateTimeOffset.UtcNow, reason);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _http.Dispose();
        _lifetime.Dispose();
    }
}
