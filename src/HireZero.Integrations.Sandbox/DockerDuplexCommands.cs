using System.Diagnostics;

namespace HireZero.Integrations.Sandbox;

public sealed record DockerExchange<T>(int ExitCode, T Value);

public interface IDockerDuplexCommands : IDockerCommands
{
    Task<DockerExchange<T>> ExchangeAsync<T>(
        string[] arguments,
        Func<Stream, Stream, CancellationToken, Task<T>> exchange,
        TimeSpan timeout,
        CancellationToken token
    );
}

public sealed partial class DockerCommands
{
    // Stopping an attached CLI is not a container-release receipt. Its resource owner must reconcile afterward.
    public async Task<DockerExchange<T>> ExchangeAsync<T>(
        string[] arguments,
        Func<Stream, Stream, CancellationToken, Task<T>> exchange,
        TimeSpan timeout,
        CancellationToken token
    )
    {
        using var process = new Process { StartInfo = StartInfo(arguments) };
        if (!process.Start())
            throw new IOException("The browser engine exchange did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var error = Read(process.StandardError, 64 * 1024, deadline);
        Task<T>? exchanging = null;
        try
        {
            exchanging = exchange(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, deadline.Token);
            var value = await exchanging.WaitAsync(deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            await error;
            return new(process.ExitCode, value);
        }
        finally
        {
            await deadline.CancelAsync();
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
                    await error;
                }
                catch (Exception failure) when (failure is IOException or OperationCanceledException) { }
                if (exchanging is not null)
                {
                    try
                    {
                        await exchanging.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException) { }
                }
            }
        }
    }
}
