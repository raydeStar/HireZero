using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace HireZero.Core;

// Direct tailnet access without Tailscale Serve: HireZero also listens on this computer's tailnet address,
// and every caller is identified by asking the local Tailscale daemon who owns the calling tailnet address
// (`tailscale whois`). WireGuard authenticates that address, so it can't be claimed by anyone else.
// Tagged devices and unknown addresses get no identity.
public sealed class TailnetAccess
{
    public IPAddress Address { get; }
    public string[] Hosts { get; }
    private readonly Func<string, Task<string?>>? _whois;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, string? Login)> _cache = new();
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    public Func<string, Task<string?>> Lookup { get; init; }
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    // address: this computer's tailnet IPv4; names: its MagicDNS names (full and short), matched with the port.
    // whois: the raw `whois --json` answer for an address, from the Tailscale integration; none identifies nobody.
    public TailnetAccess(
        IPAddress address,
        IEnumerable<string> names,
        int port,
        Func<string, Task<string?>>? whois = null
    )
    {
        Address = address;
        Hosts =
        [
            .. names
                .Append(address.ToString())
                .Where(name => name.Length > 0)
                .Select(name => name.TrimEnd('.').ToLowerInvariant() + ":" + port)
                .Distinct(),
        ];
        _whois = whois;
        Lookup = WhoisAsync;
    }

    public static TailnetAccess? FromEnvironment(int port, Func<string, Task<string?>>? whois = null)
    {
        if (
            !IPAddress.TryParse(Environment.GetEnvironmentVariable("HIREZERO_TAILNET_ADDRESS"), out var address)
            || !InTailnet(address)
        )
            return null;
        var names = (Environment.GetEnvironmentVariable("HIREZERO_TAILNET_NAMES") ?? "").Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return new(address, names, port, whois);
    }

    // Tailscale's CGNAT range, 100.64.0.0/10.
    public static bool InTailnet(IPAddress? address)
    {
        if (address is null)
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 100 && (bytes[1] & 0xC0) == 64;
    }

    // True when this connection arrived on the tailnet listener.
    public bool Arrived(HttpContext context) =>
        context.Connection.LocalIpAddress is { } local
        && (local.IsIPv4MappedToIPv6 ? local.MapToIPv4() : local).Equals(Address);

    public bool KnownHost(string host) => Hosts.Contains(host.ToLowerInvariant());

    public async Task<string?> IdentifyAsync(IPAddress? remote)
    {
        if (!InTailnet(remote))
            return null;
        var key = (remote!.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote).ToString();
        if (_cache.TryGetValue(key, out var cached) && Clock() - cached.At < CacheFor)
            return cached.Login;
        var login = await Lookup(key);
        _cache[key] = (Clock(), login);
        return login;
    }

    public static string? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (
                root.TryGetProperty("Node", out var node)
                && node.TryGetProperty("Tags", out var tags)
                && tags.ValueKind == JsonValueKind.Array
                && tags.GetArrayLength() > 0
            )
                return null;
            if (
                !root.TryGetProperty("UserProfile", out var profile)
                || !profile.TryGetProperty("LoginName", out var name)
                || name.ValueKind != JsonValueKind.String
            )
                return null;
            var login = name.GetString()!.Trim().ToLowerInvariant();
            return login.Length is > 3 and <= 320 && login.Contains('@') && login != "tagged-devices" ? login : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The display name Tailscale has for this computer's own user (used to name the owner on the boss panel).
    public async Task<string?> OwnDisplayNameAsync()
    {
        var json = await RunWhoisAsync(Address.ToString());
        if (json is null)
            return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return
                document.RootElement.TryGetProperty("UserProfile", out var profile)
                && profile.TryGetProperty("DisplayName", out var name)
                && name.ValueKind == System.Text.Json.JsonValueKind.String
                ? ContentGuard.Clean(name.GetString(), 80).Text
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public string? DisplayName(string login) => _names.TryGetValue(login, out var name) ? name : null;

    private async Task<string?> WhoisAsync(string address)
    {
        if (await RunWhoisAsync(address) is not { } json || Parse(json) is not { } login)
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (
                document.RootElement.TryGetProperty("UserProfile", out var profile)
                && profile.TryGetProperty("DisplayName", out var name)
                && name.ValueKind == JsonValueKind.String
            )
                _names[login] = ContentGuard.Clean(name.GetString(), 80).Text;
        }
        catch (JsonException) { }
        return login;
    }

    private Task<string?> RunWhoisAsync(string address) =>
        _whois is null ? Task.FromResult<string?>(null) : _whois(address);
}
