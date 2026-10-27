using System.Globalization;
using HireZero.Core;
using HireZero.Integrations.Sandbox;

// Optional operator qualification evidence for a systemd cgroup-v2 host; never used by application execution.
internal sealed class SandboxResourceObserver(IDockerCommands inner, string root, Action<string> record)
    : IDockerCommands
{
    private string? _created;

    public async Task<DockerReply> RunAsync(
        string[] arguments,
        string? input,
        TimeSpan timeout,
        int outputLimit,
        CancellationToken token
    )
    {
        if (arguments is ["container", "create", ..])
        {
            var created = await inner.RunAsync(arguments, input, timeout, outputLimit, token);
            _created = created.ExitCode == 0 ? created.Output.Trim() : null;
            return created;
        }
        if (arguments is not ["container", "start", ..])
            return await inner.RunAsync(arguments, input, timeout, outputLimit, token);
        if (
            !Path.IsPathFullyQualified(root)
            || !Directory.Exists(root)
            || _created is not { Length: 64 }
            || !_created.All(Uri.IsHexDigit)
        )
            throw new IOException("The qualification resource monitor is not bound to an owned container.");
        var group = Path.Combine(root, "docker-" + _created + ".scope");
        var running = inner.RunAsync(arguments, input, timeout, outputLimit, token);
        var samples = 0L;
        var peak = 0L;
        var rejection = 0L;
        var oom = 0L;
        var limitChanged = false;
        var unavailable = false;
        do
        {
            try
            {
                long Number(string name) =>
                    long.Parse(File.ReadAllText(Path.Combine(group, name)).Trim(), CultureInfo.InvariantCulture);
                long Event(string name, string key) =>
                    File.ReadAllLines(Path.Combine(group, name))
                        .Where(line => line.StartsWith(key + " ", StringComparison.Ordinal))
                        .Select(line => long.Parse(line[(key.Length + 1)..], CultureInfo.InvariantCulture))
                        .Single();
                var current = Number("pids.current");
                var limit = Number("pids.max");
                rejection = Math.Max(rejection, Event("pids.events", "max"));
                oom = Math.Max(oom, Math.Max(Event("memory.events", "oom"), Event("memory.events", "oom_kill")));
                peak = Math.Max(peak, current);
                limitChanged |= limit != 128 || current > 128;
                samples++;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                // A group appears after start and disappears at exit; those edges are not fabricated samples.
            }
            catch (Exception e)
                when (e is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
            {
                unavailable = true;
            }
            if (!running.IsCompleted)
                await Task.WhenAny(running, Task.Delay(20, CancellationToken.None));
        } while (!running.IsCompleted);
        var result = await running;
        record(
            CompanyJson.Encode(
                new
                {
                    resource = _created,
                    samples,
                    peakHostTasks = peak,
                    hostTaskRejections = rejection,
                    oomEvents = oom,
                    limitChanged,
                    unavailable,
                }
            )
        );
        if (samples == 0 || rejection != 0 || oom != 0 || limitChanged || unavailable)
            throw new IOException("Host resource observations failed qualification; retain the observation journal.");
        return result;
    }
}
