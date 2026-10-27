using System.Diagnostics;
using System.Text;

namespace HireZero.Integrations.Sandbox;

public sealed record DockerReply(int ExitCode, string Output);

public interface IDockerCommands
{
    Task<DockerReply> RunAsync(
        string[] arguments,
        string? input,
        TimeSpan timeout,
        int outputLimit,
        CancellationToken token
    );
}

// This process controls only an explicitly configured local engine. Submitted scripts never become shell arguments.
public sealed partial class DockerCommands : IDockerDuplexCommands
{
    private readonly string _executable;
    private readonly string _endpoint;

    public DockerCommands(string executable, string endpoint)
    {
        if (
            !Path.IsPathFullyQualified(executable)
            || !File.Exists(executable)
            || endpoint is not ("unix:///var/run/docker.sock" or "npipe:////./pipe/dockerDesktopLinuxEngine")
        )
            throw new ArgumentException("Select the installed Docker CLI and an approved local engine endpoint.");
        _executable = executable;
        _endpoint = endpoint;
    }

    private ProcessStartInfo StartInfo(string[] arguments)
    {
        var start = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        var machine = start
            .Environment.Where(p => p.Key is "PATH" or "SystemRoot" or "WINDIR" or "SystemDrive" or "TEMP" or "TMP")
            .ToArray();
        start.Environment.Clear();
        foreach (var pair in machine)
            start.Environment[pair.Key] = pair.Value;
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add(_endpoint);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    public async Task<DockerReply> RunAsync(
        string[] arguments,
        string? input,
        TimeSpan timeout,
        int outputLimit,
        CancellationToken token
    )
    {
        using var process = new Process { StartInfo = StartInfo(arguments) };
        if (!process.Start())
            throw new IOException("Sandbox engine command did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var output = Read(process.StandardOutput, outputLimit, deadline);
        var error = Read(process.StandardError, outputLimit, deadline);
        try
        {
            if (input is not null)
                await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var text = await output;
            await error; // Drain bounded diagnostics; never return daemon paths or arbitrary error text to a tenant.
            return new(process.ExitCode, text);
        }
        finally
        {
            deadline.Cancel();
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            finally
            {
                try
                {
                    await Task.WhenAll(output, error);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException) { }
            }
        }
    }

    private static async Task<string> Read(StreamReader stream, int limit, CancellationTokenSource deadline)
    {
        var result = new StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), deadline.Token)) > 0)
        {
            if (result.Length + count > limit)
            {
                deadline.Cancel();
                throw new IOException("Sandbox engine output exceeded its bound.");
            }
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
