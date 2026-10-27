using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace HireZero.Integrations.Documents;

public sealed record PdfTextPreview(string Text, string Notice, int Pages, int[] TextlessPages);

public sealed record PdfImportReply(PdfTextPreview? Preview = null, string? Error = null, int Status = 200);

public static class PdfTextImport
{
    public const int FileByteLimit = 2 * 1024 * 1024,
        TextByteLimit = 64 * 1024,
        PageLimit = 50;
    public const string Notice =
        "Selectable PDF text only; page labels are included. Reading order, columns and tables may differ from the original. Images, scanned text, annotations, attachments and form fields are not imported. Compare with the original and correct missing context before approval. The original PDF is not retained.";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The host calls the worker, not this parser. Even a paper tiger gets its own room.
    public static PdfTextPreview Extract(byte[] bytes)
    {
        if (bytes.Length > FileByteLimit)
            throw new DocumentImportException("Choose a PDF of at most 2 MiB.", 413);
        if (bytes.Length < 8 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new DocumentImportException(
                "This file is not a supported PDF. Export a fresh PDF or import its text."
            );
        try
        {
            using var document = PdfDocument.Open(
                bytes,
                new ParsingOptions
                {
                    UseLenientParsing = false,
                    SkipMissingFonts = false,
                    MaxStackDepth = 64,
                }
            );
            if (document.IsEncrypted)
                throw new DocumentImportException(
                    "Encrypted PDFs are not imported. Use an unlocked copy you are authorized to read."
                );
            if (document.NumberOfPages is < 1 or > PageLimit)
                throw new DocumentImportException("Choose a PDF with 1 to 50 pages.", 413);
            var result = new StringBuilder();
            var textless = new List<int>();
            var totalBytes = 0;
            for (var index = 1; index <= document.NumberOfPages; index++)
            {
                var text = ContentOrderTextExtractor
                    .GetText(document.GetPage(index))
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Trim();
                if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\t')))
                    throw new DocumentImportException(
                        "PDF text contains unsupported characters. Export a plain-text copy and review it before import."
                    );
                if (string.IsNullOrWhiteSpace(text))
                {
                    textless.Add(index);
                    text = "[No selectable text on this page.]";
                }
                var section = (index == 1 ? "" : "\n\n") + "Page " + index + "\n" + text;
                totalBytes += Encoding.UTF8.GetByteCount(section);
                if (totalBytes > TextByteLimit)
                    throw new DocumentImportException(
                        "This PDF contains more than 64 KiB of extracted text. Import a smaller section.",
                        413
                    );
                result.Append(section);
            }
            if (textless.Count == document.NumberOfPages)
                throw new DocumentImportException(
                    "No selectable text was found. This may be a scanned PDF. Run OCR or export text, then review and import that text."
                );
            var missing =
                textless.Count == 0 ? "" : " No selectable text on pages: " + string.Join(", ", textless) + ".";
            return new(result.ToString(), Notice + missing, document.NumberOfPages, [.. textless]);
        }
        catch (DocumentImportException)
        {
            throw;
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new DocumentImportException(
                "Encrypted PDFs are not imported. Use an unlocked copy you are authorized to read."
            );
        }
        catch (Exception)
        {
            // Parser messages can contain private text. Return guidance, never the document's confessions.
            throw new DocumentImportException(
                "This PDF could not be read completely. Export a fresh PDF or a text copy, then review it before import."
            );
        }
    }
}

public static class PdfImportWorker
{
    public static async Task<int> Main(string[] args)
    {
        if (args is not ["--extract-pdf"])
            return 64;
        // Also expires if the parent disappears. This is a parser deadline, not an OS sandbox claim.
        using var deadline = new Timer(
            _ => Environment.Exit(74),
            null,
            TimeSpan.FromSeconds(12),
            Timeout.InfiniteTimeSpan
        );
        PdfImportReply reply;
        try
        {
            using var input = Console.OpenStandardInput();
            using var bytes = new MemoryStream();
            var chunk = new byte[16384];
            int count;
            while ((count = await input.ReadAsync(chunk)) > 0)
            {
                if (bytes.Length + count > PdfTextImport.FileByteLimit)
                    throw new DocumentImportException("Choose a PDF of at most 2 MiB.", 413);
                await bytes.WriteAsync(chunk.AsMemory(0, count));
            }
            reply = new(PdfTextImport.Extract(bytes.ToArray()));
        }
        catch (DocumentImportException e)
        {
            reply = new(Error: e.Message, Status: e.Status);
        }
        catch (Exception)
        {
            reply = new(Error: "PDF import did not finish. Export a fresh PDF or text copy.", Status: 422);
        }
        await Console.Out.WriteAsync(JsonSerializer.Serialize(reply, PdfTextImport.Json));
        return 0;
    }
}
