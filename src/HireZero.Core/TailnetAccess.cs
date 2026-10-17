using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly string _command;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, string? Login)> _cache = new();
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    public Func<string, Task<string?>> Lookup { get; init; }
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    // address: this computer's tailnet IPv4; names: its MagicDNS names (full and short), matched with the port.
    public TailnetAccess(IPAddress address, IEnumerable<string> names, int port, string? command = null)
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
        _command =
            command
            ?? (
                File.Exists(@"C:\Program Files\Tailscale\tailscale.exe")
                    ? @"C:\Program Files\Tailscale\tailscale.exe"
                    : "tailscale"
            );
        Lookup = WhoisAsync;
    }

    public static TailnetAccess? FromEnvironment(int port)
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
        return new(address, names, port, Environment.GetEnvironmentVariable("HIREZERO_TAILSCALE"));
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

    private async Task<string?> RunWhoisAsync(string address)
    {
        var start = new ProcessStartInfo(_command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "whois", "--json", address })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return null;
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            _ = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            return process.ExitCode == 0 ? await output : null;
        }
        catch (Exception error)
            when (error
                    is System.ComponentModel.Win32Exception
                        or OperationCanceledException
                        or InvalidOperationException
            )
        {
            return null;
        }
    }
}
