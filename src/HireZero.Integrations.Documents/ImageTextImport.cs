using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Documents;

public sealed record ImageTextPreview(string Text, string Notice);

public static class ImageTextImport
{
    public const int FileByteLimit = 2 * 1024 * 1024;
    public const string Notice =
        "Local OCR reads text only, not layout or objects. Check and correct recognition before sending. The image is not retained or sent to a model.";
    private static readonly SemaphoreSlim Slots = new(2);

    public static async Task<ImageTextPreview> ExtractAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length > FileByteLimit)
            throw new DocumentImportException("Choose an image of at most 2 MiB.", 413);
        if (
            !(
                bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                || bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 })
            )
        )
            throw new DocumentImportException("Choose a PNG or JPEG screenshot.", 422);
        if (!OperatingSystem.IsWindows())
            throw new DocumentImportException(
                "Local screenshot OCR is available on Windows. Paste the relevant text instead.",
                503
            );
        if (!await Slots.WaitAsync(0, token))
            throw new DocumentImportException("Screenshot reading is busy. Try again in a moment.", 429);
        try
        {
            return await RunAsync(bytes, token);
        }
        finally
        {
            Slots.Release();
        }
    }

    private static async Task<ImageTextPreview> RunAsync(byte[] bytes, CancellationToken token)
    {
        using var script = new StreamReader(
            typeof(ImageTextImport).Assembly.GetManifestResourceStream("ImageTextImport")!
        );
        var start = new ProcessStartInfo(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"
            )
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Only our embedded script is code. Uploaded bytes enter through stdin, never through a shell argument.
        foreach (
            var arg in new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(await script.ReadToEndAsync(token))),
            }
        )
            start.ArgumentList.Add(arg);
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        start.Environment.Clear();
        start.Environment["SystemRoot"] = systemRoot;
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        Task<string>? stdout = null,
            stderr = null;
        Task? input = null;
        var started = false;
        try
        {
            process.Start();
            started = true;
            stdout = ReadBounded(process.StandardOutput, 512 * 1024, deadline.Token);
            stderr = ReadBounded(process.StandardError, 16 * 1024, deadline.Token);
            input = WriteInput(process, bytes, deadline.Token);
            while (!process.HasExited)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    process.Refresh();
                    if (process.PrivateMemorySize64 > 384L * 1024 * 1024 || stdout.IsFaulted || stderr.IsFaulted)
                        throw new DocumentImportException(
                            "The screenshot exceeded local reading limits. Crop it first.",
                            413
                        );
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    break;
                }
                await Task.Delay(40, deadline.Token);
            }
            await input;
            var json = await stdout;
            await stderr;
            if (process.ExitCode != 0)
                throw new DocumentImportException("Local OCR is unavailable. Paste the screenshot text instead.", 422);
            using var result = JsonDocument.Parse(json);
            if (
                !result.RootElement.TryGetProperty("text", out var value)
                || value.GetString() is not { } text
                || string.IsNullOrWhiteSpace(text)
            )
                throw new DocumentImportException(
                    "No readable text was found. Try a clear PNG or JPEG crop, or paste the text.",
                    422
                );
            if (Encoding.UTF8.GetByteCount(text) > 65536)
                throw new DocumentImportException("Too much screenshot text. Crop it first.", 413);
            return new(text, Notice);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new DocumentImportException("Screenshot reading timed out. Try a smaller crop.", 422);
        }
        catch (Exception e) when (e is JsonException or System.ComponentModel.Win32Exception or IOException)
        {
            throw new DocumentImportException("Local OCR could not read this screenshot. Paste its text instead.", 422);
        }
        finally
        {
            if (started && !process.HasExited)
                process.Kill(true);
            deadline.Cancel();
            if (started)
                await process.WaitForExitAsync(CancellationToken.None);
            foreach (var task in new Task?[] { input, stdout, stderr })
                if (task is not null)
                    try
                    {
                        await task;
                    }
                    catch
                    { /* The reader has already reported its failure. */
                    }
        }
    }

    private static async Task WriteInput(Process process, byte[] bytes, CancellationToken token)
    {
        await process.StandardInput.WriteAsync(Convert.ToBase64String(bytes).AsMemory(), token);
        process.StandardInput.Close();
    }

    private static async Task<string> ReadBounded(StreamReader reader, int maximum, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > maximum)
                throw new IOException("OCR output exceeded its bound.");
            output.Append(buffer, 0, read);
        }
        return output.ToString();
    }
}
