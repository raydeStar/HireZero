using System.Net;
using HireZero.Core;

// Direct tailnet identity with a fake whois: no Tailscale daemon is called.
public static class TailnetChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(TailnetAccess.InTailnet(IPAddress.Parse("100.64.0.11")) && TailnetAccess.InTailnet(IPAddress.Parse("100.127.255.1")) &&
            !TailnetAccess.InTailnet(IPAddress.Parse("100.128.0.1")) && !TailnetAccess.InTailnet(IPAddress.Parse("192.168.1.20")) &&
            !TailnetAccess.InTailnet(IPAddress.Loopback) && TailnetAccess.InTailnet(IPAddress.Parse("::ffff:100.64.0.11")),
            "Only Tailscale's 100.64.0.0/10 addresses count as tailnet callers.");
        check(TailnetAccess.Parse("""{"Node":{"ComputedName":"laptop","Tags":null},"UserProfile":{"LoginName":"avery@northwind.example"}}""") == "avery@northwind.example" &&
            TailnetAccess.Parse("""{"Node":{"Tags":["tag:server"]},"UserProfile":{"LoginName":"tagged-devices"}}""") is null &&
            TailnetAccess.Parse("""{"UserProfile":{"LoginName":"tagged-devices"}}""") is null && TailnetAccess.Parse("peer not found") is null,
            "Whois gives a person's login; tagged devices and errors give no identity.");

        var lookups = new List<string>(); var now = DateTimeOffset.UtcNow;
        var access = new TailnetAccess(IPAddress.Parse("100.64.0.11"), ["host-1.tail0000.ts.net.", "host-1"], 5064)
        {
            Clock = () => now,
            Lookup = address => { lookups.Add(address); return Task.FromResult<string?>(address == "100.64.0.12" ? "avery@northwind.example" : null); }
        };
        check(access.KnownHost("host-1.tail0000.ts.net:5064") && access.KnownHost("host-1:5064") && access.KnownHost("100.64.0.11:5064") &&
            !access.KnownHost("evil.example:5064") && !access.KnownHost("host-1.tail0000.ts.net"), "Only this computer's own tailnet names and port are accepted (no DNS rebinding).");
        check(await access.IdentifyAsync(IPAddress.Parse("100.64.0.12")) == "avery@northwind.example" && await access.IdentifyAsync(IPAddress.Parse("100.64.0.12")) == "avery@northwind.example" &&
            lookups.Count == 1, "A caller is identified once and cached.");
        check(await access.IdentifyAsync(IPAddress.Parse("192.168.1.20")) is null && await access.IdentifyAsync(IPAddress.Parse("100.90.9.9")) is null && lookups.Count == 2,
            "Addresses outside the tailnet are never looked up; unknown peers get no identity.");
        now = now.AddMinutes(6); await access.IdentifyAsync(IPAddress.Parse("100.64.0.12"));
        check(lookups.Count == 3, "Identities are looked up again after five minutes, so a removed device stops working.");
    }
}
