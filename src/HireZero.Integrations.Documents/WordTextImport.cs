using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HireZero.Integrations.Documents;

public sealed class DocumentImportException(string message, int status = 422) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed record WordTextPreview(string Text, string Notice);

// Read bounded package parts in memory. No Office process, extracted files, links or macros are invited to dinner.
public static class WordTextImport
{
    public const int FileByteLimit = 2 * 1024 * 1024;
    private const int XmlByteLimit = 1024 * 1024;
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string OfficeRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string StrictOfficeRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";
    private const string DocumentContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    public const string Notice =
        "Only document-body text and tables are imported. Headers, footers, comments, notes, images, text boxes, formatting and automatic list labels are omitted. Field values are not refreshed. Review and correct the extracted text before saving or approving it. The original Word file is not retained.";

    public static WordTextPreview Extract(byte[] bytes, int textByteLimit)
    {
        if (bytes.Length > FileByteLimit)
            throw new DocumentImportException("Choose a Word .docx file of at most 2 MiB.", 413);
        if (textByteLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(textByteLimit));
        try
        {
            using var input = new MemoryStream(bytes, false);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, false);
            if (zip.Entries.Count > 256)
                throw new DocumentImportException(
                    "This Word package has too many parts. Export the relevant material as plain text.",
                    413
                );
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (!SafePartName(name.TrimEnd('/')) || !entries.TryAdd(name, entry))
                    throw new DocumentImportException("This Word package contains ambiguous or invalid part names.");
                total += entry.Length;
                if (total > 16 * 1024 * 1024)
                    throw new DocumentImportException(
                        "This Word package expands beyond the import limit. Export a smaller document as plain text.",
                        413
                    );
                if (
                    name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("word/activeX/", StringComparison.OrdinalIgnoreCase)
                )
                    throw new DocumentImportException(
                        "Macros, embedded objects and ActiveX content are not supported. Export plain text or save a clean .docx copy."
                    );
            }
            XDocument Part(string name, int limit = XmlByteLimit) =>
                entries.TryGetValue(name, out var entry)
                    ? ReadXml(entry, limit)
                    : throw new DocumentImportException("This file is missing a required Word document part.");
            XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
            var roots = Part("_rels/.rels", 128 * 1024).Root;
            if (roots?.Name != relationships + "Relationships")
                throw new DocumentImportException("This file has invalid Word package relationships.");
            var main = roots
                .Elements(relationships + "Relationship")
                .Where(e => (string?)e.Attribute("Type") is OfficeRelationship or StrictOfficeRelationship)
                .ToArray();
            if (main.Length != 1 || (string?)main[0].Attribute("TargetMode") is not (null or "Internal"))
                throw new DocumentImportException("Choose a Word document with one internal main document part.");
            var target = ((string?)main[0].Attribute("Target") ?? "").TrimStart('/');
            if (!SafePartName(target))
                throw new DocumentImportException("The Word document points to an unsupported main part.");
            XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
            var types = Part("[Content_Types].xml", 128 * 1024).Root;
            if (
                types?.Name != contentTypes + "Types"
                || types
                    .Descendants()
                    .Any(e =>
                        ((string?)e.Attribute("ContentType"))?.Contains(
                            "macroEnabled",
                            StringComparison.OrdinalIgnoreCase
                        ) == true
                    )
                || types
                    .Elements(contentTypes + "Override")
                    .Count(e =>
                        (string?)e.Attribute("PartName") == "/" + target
                        && (string?)e.Attribute("ContentType") == DocumentContentType
                    ) != 1
            )
                throw new DocumentImportException("Choose a standard .docx document without macros.");
            var document = Part(target).Root;
            if (
                document is null
                || document.Name.LocalName != "document"
                || document.Name.NamespaceName is not (WordNamespace or StrictWordNamespace)
            )
                throw new DocumentImportException("This file does not contain a supported Word document body.");
            var w = document.Name.Namespace;
            var bodies = document.Elements(w + "body").ToArray();
            if (bodies.Length != 1)
                throw new DocumentImportException("This file does not contain one Word document body.");
            if (
                document
                    .Descendants()
                    .Any(e =>
                        e.Name.Namespace == w
                        && (
                            e.Name.LocalName is "ins" or "del" or "moveFrom" or "moveTo"
                            || e.Name.LocalName.EndsWith("Change", StringComparison.Ordinal)
                        )
                    )
            )
                throw new DocumentImportException(
                    "Resolve tracked changes in Word before importing, or export the reviewed text as .txt."
                );
            if (document.Descendants(w + "altChunk").Any())
                throw new DocumentImportException(
                    "Embedded document imports are not supported. Export the reviewed text as .txt."
                );
            var output = new StringBuilder();
            var size = 0;
            void Append(string text)
            {
                size += Encoding.UTF8.GetByteCount(text);
                if (size > textByteLimit)
                    throw new DocumentImportException(
                        "The extracted text exceeds 64 KiB. Import a shorter document or selected sections.",
                        413
                    );
                output.Append(text);
            }
            void Trim(char character)
            {
                while (output.Length > 0 && output[^1] == character)
                {
                    output.Length--;
                    size--;
                }
            }
            void Visit(XElement element)
            {
                if (element.Name.Namespace != w)
                    return;
                var kind = element.Name.LocalName;
                if (kind is "drawing" or "pict" or "object" or "txbxContent" or "instrText" or "delText")
                    return;
                if (kind == "t")
                {
                    Append(element.Value);
                    return;
                }
                if (kind is "tab" or "br" or "cr")
                {
                    Append(kind == "tab" ? "\t" : "\n");
                    return;
                }
                foreach (var child in element.Elements())
                    Visit(child);
                if (kind == "p")
                    Append("\n");
                else if (kind == "tc")
                {
                    Trim('\n');
                    Append("\t");
                }
                else if (kind == "tr")
                {
                    Trim('\t');
                    Append("\n");
                }
            }
            Visit(bodies[0]);
            var result = output.ToString().Trim();
            if (result.Length == 0)
                throw new DocumentImportException(
                    "No supported body text was found. Images and scanned pages need a separate text export."
                );
            return new(result, Notice);
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or IOException)
        {
            throw new DocumentImportException(
                "This Word file could not be read safely. Use an unencrypted .docx file, or export plain text."
            );
        }
    }

    private static bool SafePartName(string name) =>
        name.Length is > 0 and <= 240
        && !name.StartsWith('/')
        && name.IndexOfAny(['\\', ':', '%', '?', '#']) < 0
        && name.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));

    private static XDocument ReadXml(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit)
            throw new DocumentImportException(
                "A Word XML part exceeds the import limit. Export a smaller document as plain text.",
                413
            );
        using var source = entry.Open();
        using var copy = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = source.Read(buffer)) != 0)
        {
            if (copy.Length + count > limit)
                throw new DocumentImportException("A Word XML part exceeds the import limit.", 413);
            copy.Write(buffer, 0, count);
        }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limit,
        };
        copy.Position = 0;
        using (var scan = XmlReader.Create(copy, settings))
            while (scan.Read())
                if (scan.Depth > 64)
                    throw new DocumentImportException("This Word document is nested too deeply to import.", 413);
        copy.Position = 0;
        using var reader = XmlReader.Create(copy, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
}
