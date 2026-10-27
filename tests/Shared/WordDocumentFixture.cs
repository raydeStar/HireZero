using System.IO.Compression;
using System.Text;

internal static class WordDocumentFixture
{
    public const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    public static Dictionary<string, string> Parts(
        string body,
        string main = "word/document.xml",
        string? wordNamespace = null
    ) =>
        new()
        {
            ["[Content_Types].xml"] =
                $"<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/{main}' ContentType='{ContentType}'/></Types>",
            ["_rels/.rels"] =
                $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='main' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='{main}'/></Relationships>",
            [main] = $"<w:document xmlns:w='{wordNamespace ?? WordNamespace}'><w:body>{body}</w:body></w:document>",
        };

    public static byte[] Package(IEnumerable<KeyValuePair<string, string>> parts)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var (name, text) in parts)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        return buffer.ToArray();
    }

    public static byte[] Create(string body) => Package(Parts(body));

    public static byte[] BrandGuide() =>
        Create(
            "<w:p><w:r><w:t>Cobalt Paper Studio brand guide</w:t></w:r></w:p><w:p><w:r><w:t>Use patient demonstrations for beginner paper artists.</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Channel</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Cadence</w:t></w:r></w:p></w:tc></w:tr><w:tr><w:tc><w:p><w:r><w:t>Newsletter</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Weekly</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
        );
}
