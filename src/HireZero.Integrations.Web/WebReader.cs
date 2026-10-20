using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using HireZero.Tools.Web;

namespace HireZero.Integrations.Web;

// The web.read tool (docs/adr/0041-context-drafted-from-sources.md): one public https page as readable text.
// - Only https URLs, and at most three redirects, each checked again.
// - Every address is checked when the socket connects, so a name that resolves to loopback, a private network,
//   link-local, multicast or another non-public range is refused even if it changes between lookups.
// - At most 2 MB is read, within 15 seconds, and only text or HTML.
// Owner-started and read-only; nothing is retried.
public sealed partial class WebReader : IToolProvider
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxRedirects = 3;
    private readonly HttpClient _http;

    public WebReader(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(
            handler
                ?? new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    ConnectCallback = ConnectPublicAsync,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                }
        )
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HireZero-source-reader/1.0 (+https://hirezero.app)");
    }

    public void Register(ToolRegistry registry) => registry.Register(WebTools.Read, ReadAsync);

    public async Task<WebPage> ReadAsync(WebReadRequest request, CancellationToken token)
    {
        var url = Checked(request.Url);
        for (var hop = 0; ; hop++)
        {
            using var response = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, url),
                HttpCompletionOption.ResponseHeadersRead,
                token
            );
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                if (hop >= MaxRedirects)
                    throw Refused("The page redirected too many times.");
                url = Checked(new Uri(url, location).ToString());
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new ToolFailureException(
                    "failed",
                    "The page answered " + (int)response.StatusCode + ".",
                    (int)response.StatusCode
                );
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!(type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || type.Contains("html")))
                throw Refused(
                    "Only text and web pages can be read, not " + (type.Length > 0 ? type : "this file") + "."
                );
            var body = await ReadBoundedAsync(response.Content, token);
            var html = type.Contains("html", StringComparison.OrdinalIgnoreCase);
            var title = html ? Title(body) : "";
            var text = html ? Text(body) : Collapse(body);
            var clipped = text.Length > WebTools.MaxTextCharacters;
            return new(
                url.ToString(),
                title.Length > 200 ? title[..200] : title,
                clipped ? text[..WebTools.MaxTextCharacters] : text,
                clipped
            );
        }
    }

    private static ToolFailureException Refused(string message) => new("refused", message, 422);

    // An absolute https URL with a host name or address, no credentials in it.
    public static Uri Checked(string text)
    {
        if (
            !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || url.UserInfo.Length > 0
            || url.Host.Length == 0
        )
            throw Refused("Use a public https address, such as https://example.com/.");
        if (IPAddress.TryParse(url.Host.Trim('[', ']'), out var literal) && !IsPublic(literal))
            throw Refused("That address is not on the public internet.");
        return url;
    }

    // Connects only to public addresses: the check happens on the address actually used for the socket.
    private static async ValueTask<Stream> ConnectPublicAsync(
        SocketsHttpConnectionContext context,
        CancellationToken token
    )
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
            throw Refused("That address is not on the public internet.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // Public unicast only: no loopback, private, shared, link-local, multicast, documentation or reserved ranges.
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(
                b[0] is 0 or 10 or 127 or >= 224
                || b[0] == 100 && b[1] is >= 64 and <= 127
                || b[0] == 169 && b[1] == 254
                || b[0] == 172 && b[1] is >= 16 and <= 31
                || b[0] == 192 && b[1] == 168
                || b[0] == 192 && b[1] == 0 && b[2] is 0 or 2
                || b[0] == 198 && b[1] is 18 or 19
                || b[0] == 198 && b[1] == 51 && b[2] == 100
                || b[0] == 203 && b[1] == 0 && b[2] == 113
            );
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(
                IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC
                || b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8
            );
        }
        return false;
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                throw Refused("The page is larger than 2 MB.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string Title(string html) =>
        TitlePattern().Match(html) is { Success: true } m ? Collapse(WebUtility.HtmlDecode(m.Groups[1].Value)) : "";

    // A main region shorter than this falls back to the whole page, so a thin <main> loses nothing.
    private const int MainMinimumCharacters = 200;

    // Readable text: scripts, styles, comments and markup removed; site navigation, footers and sidebars dropped; the
    // page's <main> (or its longest <article>) preferred when it holds real text; block ends become line breaks;
    // entities decoded.
    public static string Text(string html)
    {
        var page = Chrome().Replace(Hidden().Replace(html, " "), " ");
        var main = MainPattern().Match(page) is { Success: true } m
            ? m.Groups[1].Value
            : ArticlePattern().Matches(page).Select(a => a.Groups[1].Value).MaxBy(a => a.Length);
        return main is not null && Readable(main) is { Length: >= MainMinimumCharacters } focused
            ? focused
            : Readable(page);
    }

    private static string Readable(string html)
    {
        var text = Breaks().Replace(html, "\n");
        text = Tags().Replace(text, " ");
        return Collapse(WebUtility.HtmlDecode(text));
    }

    private static string Collapse(string text) =>
        string.Join(
            "\n",
            text.Split('\n').Select(line => Spaces().Replace(line, " ").Trim()).Where(line => line.Length > 0)
        );

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(
        @"<(script|style|noscript|template|svg)\b[^>]*>.*?</\1>|<!--.*?-->",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    )]
    private static partial Regex Hidden();

    [GeneratedRegex(@"<(nav|footer|aside)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Chrome();

    [GeneratedRegex(@"<main\b[^>]*>(.*?)</main>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MainPattern();

    [GeneratedRegex(@"<article\b[^>]*>(.*?)</article>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ArticlePattern();

    [GeneratedRegex(
        @"<(br|/p|/div|/li|/h[1-6]|/tr|/section|/article|/header|/footer|/title|/head|/nav)\b[^>]*>",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex Breaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\r\f\v ]+")]
    private static partial Regex Spaces();
}
