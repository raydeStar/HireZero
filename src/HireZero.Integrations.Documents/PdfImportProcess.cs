using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Documents;

public static class PdfImportProcess
{
    private static readonly SemaphoreSlim Slots = new(2);

    public static async Task<PdfTextPreview> ExtractAsync(byte[] bytes, string hostAssembly, CancellationToken token)
    {
        if (bytes.Length > PdfTextImport.FileByteLimit)
            throw new DocumentImportException("Choose a PDF of at most 2 MiB.", 413);
        if (!await Slots.WaitAsync(0, token))
            throw new DocumentImportException("PDF import is busy. Wait a moment, then select the file again.", 429);
        try
        {
            return await RunAsync(bytes, hostAssembly, token);
        }
        finally
        {
            Slots.Release();
        }
    }

    private static async Task<PdfTextPreview> RunAsync(byte[] bytes, string hostAssembly, CancellationToken token)
    {
        var dotnet = Path.GetFullPath(
            Path.Combine(
                RuntimeEnvironment.GetRuntimeDirectory(),
                "../../..",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"
            )
        );
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(typeof(PdfTextImport).Assembly.Location)!,
        };
        foreach (
            var arg in new[]
            {
                "exec",
                "--runtimeconfig",
                Path.ChangeExtension(hostAssembly, ".runtimeconfig.json"),
                "--depsfile",
                Path.ChangeExtension(hostAssembly, ".deps.json"),
                typeof(PdfTextImport).Assembly.Location,
                "--extract-pdf",
            }
        )
            start.ArgumentList.Add(arg);
        start.Environment.Clear();
        if (OperatingSystem.IsWindows())
            start.Environment["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot");
        start.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(dotnet);
        start.Environment["DOTNET_GCHeapHardLimit"] = "0x8000000";
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        start.Environment["COMPlus_DbgEnableMiniDump"] = "0";
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var started = false;
        Task<string>? stdout = null;
        Task? stderr = null;
        Task? input = null;
        try
        {
            if (!File.Exists(dotnet) || !File.Exists(Path.ChangeExtension(hostAssembly, ".runtimeconfig.json")))
                throw new DocumentImportException(
                    "The PDF reader is unavailable in this installation. Import a reviewed text copy instead.",
                    503
                );
            process.Start();
            started = true;
            stdout = ReadBounded(process.StandardOutput, 512 * 1024, deadline.Token);
            stderr = ReadBounded(process.StandardError, 4096, deadline.Token);
            input = WriteInput(process, bytes, deadline.Token);
            while (!process.HasExited)
            {
                deadline.Token.ThrowIfCancellationRequested();
                long privateBytes;
                try
                {
                    process.Refresh();
                    privateBytes = process.PrivateMemorySize64;
                }
                catch (Exception e)
                    when (e is InvalidOperationException or System.ComponentModel.Win32Exception && process.HasExited)
                {
                    // The worker can finish between HasExited and the memory probe. Still validate its reply below.
                    break;
                }
                if (privateBytes > 256L * 1024 * 1024 || stdout.IsFaulted || stderr.IsFaulted)
                    throw new DocumentImportException(
                        "This PDF exceeded the reader's processing limit. Import a smaller PDF or reviewed text copy.",
                        413
                    );
                await Task.Delay(40, deadline.Token);
            }
            await input;
            var result = await stdout;
            await stderr;
            if (process.ExitCode != 0)
                throw new DocumentImportException(
                    "PDF import did not finish within its processing limits. Use a smaller PDF or reviewed text copy.",
                    422
                );
            var reply = JsonSerializer.Deserialize<PdfImportReply>(result, PdfTextImport.Json);
            if (reply?.Error is { } error)
                throw new DocumentImportException(error, reply.Status is 413 ? 413 : 422);
            var preview = reply?.Preview;
            if (
                preview is null
                || string.IsNullOrWhiteSpace(preview.Text)
                || Encoding.UTF8.GetByteCount(preview.Text) > PdfTextImport.TextByteLimit
                || preview.Pages is < 1 or > PdfTextImport.PageLimit
                || preview.TextlessPages is null
                || preview.TextlessPages.Any(p => p < 1 || p > preview.Pages)
            )
                throw new DocumentImportException(
                    "The PDF reader returned an incomplete preview. Import a reviewed text copy instead.",
                    422
                );
            return preview;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new DocumentImportException(
                "PDF import took too long. Use a smaller PDF or reviewed text copy.",
                422
            );
        }
        catch (DocumentImportException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DocumentImportException(
                "The PDF reader could not finish. Your draft is unchanged; try a fresh PDF or reviewed text copy.",
                422
            );
        }
        finally
        {
            deadline.Cancel();
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (input is not null)
            {
                try
                {
                    await input;
                }
                catch (Exception) { }
            }
            if (stdout is not null)
            {
                try
                {
                    await stdout;
                }
                catch (Exception) { }
            }
            if (stderr is not null)
            {
                try
                {
                    await stderr;
                }
                catch (Exception) { }
            }
        }
    }

    private static async Task WriteInput(Process process, byte[] bytes, CancellationToken token)
    {
        await process.StandardInput.BaseStream.WriteAsync(bytes, token);
        process.StandardInput.Close();
    }

    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken token)
    {
        var result = new StringBuilder();
        var chunk = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(chunk, token)) > 0)
        {
            if (result.Length + count > limit)
                throw new IOException("PDF worker response limit exceeded.");
            result.Append(chunk, 0, count);
        }
        return result.ToString();
    }
}
