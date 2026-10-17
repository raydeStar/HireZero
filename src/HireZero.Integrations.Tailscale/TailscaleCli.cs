using System.Diagnostics;

namespace HireZero.Integrations.Tailscale;

// Identifies tailnet callers with `tailscale whois --json` for TailnetAccess. Read-only; no answer means unknown.
public sealed class TailscaleCli(string? command = null)
{
    private readonly string _command =
        command
        ?? (
            File.Exists(@"C:\Program Files\Tailscale\tailscale.exe")
                ? @"C:\Program Files\Tailscale\tailscale.exe"
                : "tailscale"
        );

    public async Task<string?> WhoisJsonAsync(string address)
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
