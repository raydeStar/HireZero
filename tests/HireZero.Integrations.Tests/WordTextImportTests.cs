using HireZero.Integrations.Documents;

public sealed class WordTextImportTests
{
    private static WordTextPreview Read(Dictionary<string, string> parts) =>
        WordTextImport.Extract(WordDocumentFixture.Package(parts), 65536);

    private static Dictionary<string, string> Parts(
        string body = "<w:p><w:r><w:t>Fictional cobalt guide.</w:t></w:r></w:p>"
    ) => WordDocumentFixture.Parts(body);

    [Fact]
    public void BodyParagraphsAndTablesAreReadableWithoutIncludingOtherDocumentStories()
    {
        var result = WordTextImport.Extract(WordDocumentFixture.BrandGuide(), 65536);
        Assert.Equal(
            "Cobalt Paper Studio brand guide\nUse patient demonstrations for beginner paper artists.\nChannel\tCadence\nNewsletter\tWeekly",
            result.Text
        );
        var parts = Parts(
            "<w:p><w:r><w:t>Visible </w:t><w:tab/><w:t>text</w:t><w:br/><w:t>next line</w:t></w:r><w:hyperlink><w:r><w:t> link label</w:t></w:r></w:hyperlink><w:r><w:instrText>DO NOT EXECUTE</w:instrText></w:r><w:drawing><w:txbxContent><w:p><w:r><w:t>Omitted box</w:t></w:r></w:p></w:txbxContent></w:drawing></w:p>"
        );
        parts["word/header1.xml"] = "<header>Omitted header</header>";
        parts["word/_rels/document.xml.rels"] =
            "<Relationships><Relationship Target='https://example.invalid/do-not-fetch' TargetMode='External'/></Relationships>";
        var preview = Read(parts);
        Assert.Equal("Visible \ttext\nnext line link label", preview.Text);
        Assert.Contains("omitted", preview.Notice);
        Assert.DoesNotContain("Omitted", preview.Text);
        Assert.DoesNotContain("EXECUTE", preview.Text);
    }

    [Fact]
    public void MainPartComesFromThePackageRelationshipAndStrictWordNamespacesWork()
    {
        var parts = WordDocumentFixture.Parts(
            "<w:p><w:r><w:t>Fictional strict document.</w:t></w:r></w:p>",
            "custom/main.xml",
            "http://purl.oclc.org/ooxml/wordprocessingml/main"
        );
        parts["_rels/.rels"] = parts["_rels/.rels"]
            .Replace(
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
                "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"
            );
        Assert.Equal("Fictional strict document.", Read(parts).Text);
    }

    [Fact]
    public void PackageTraversalAmbiguityAndExternalMainPartsAreRefused()
    {
        var parts = Parts();
        parts["../outside.xml"] = "fictional";
        Assert.Throws<DocumentImportException>(() => Read(parts));
        parts = Parts();
        var duplicate = parts.Append(new("WORD/DOCUMENT.XML", "duplicate"));
        Assert.Throws<DocumentImportException>(() =>
            WordTextImport.Extract(WordDocumentFixture.Package(duplicate), 65536)
        );
        parts["_rels/.rels"] = parts["_rels/.rels"]
            .Replace("Target='word/document.xml'", "Target='https://example.invalid/private' TargetMode='External'");
        Assert.Throws<DocumentImportException>(() => Read(parts));
        parts = Parts();
        parts["_rels/.rels"] = parts["_rels/.rels"].Replace("word/document.xml", "%2e%2e/private.xml");
        Assert.Throws<DocumentImportException>(() => Read(parts));
    }

    [Fact]
    public void MacrosTrackedChangesAndEmbeddedImportsRequireAReviewedTextExport()
    {
        var parts = Parts();
        parts["word/vbaProject.bin"] = "fictional macro";
        Assert.Throws<DocumentImportException>(() => Read(parts));
        parts = Parts();
        parts["[Content_Types].xml"] = parts["[Content_Types].xml"]
            .Replace(WordDocumentFixture.ContentType, "application/vnd.ms-word.document.macroEnabled.main+xml");
        Assert.Throws<DocumentImportException>(() => Read(parts));
        Assert.Contains(
            "tracked changes",
            Assert
                .Throws<DocumentImportException>(() =>
                    Read(Parts("<w:ins><w:p><w:r><w:t>Unaccepted change</w:t></w:r></w:p></w:ins>"))
                )
                .Message
        );
        Assert.Throws<DocumentImportException>(() => Read(Parts("<w:altChunk/>")));
        parts = Parts();
        parts["word/embeddings/object.bin"] = "fictional embedded object";
        Assert.Throws<DocumentImportException>(() => Read(parts));
    }

    [Fact]
    public void DtdEntityExpansionAndDeepXmlAreRejectedWithoutReadingExternalContent()
    {
        var parts = Parts();
        parts["word/document.xml"] =
            "<!DOCTYPE x [<!ENTITY external SYSTEM 'https://example.invalid/private'>]><w:document xmlns:w='"
            + WordDocumentFixture.WordNamespace
            + "'><w:body><w:p><w:r><w:t>&external;</w:t></w:r></w:p></w:body></w:document>";
        Assert.Throws<DocumentImportException>(() => Read(parts));
        var nested =
            string.Concat(Enumerable.Repeat("<w:sdt>", 66))
            + "<w:p/>"
            + string.Concat(Enumerable.Repeat("</w:sdt>", 66));
        Assert.Equal(413, Assert.Throws<DocumentImportException>(() => Read(Parts(nested))).Status);
    }

    [Fact]
    public void CompressedSizeExpansionPartCountAndUtf8OutputEachHaveIndependentBounds()
    {
        Assert.Equal(
            413,
            Assert
                .Throws<DocumentImportException>(() =>
                    WordTextImport.Extract(new byte[WordTextImport.FileByteLimit + 1], 65536)
                )
                .Status
        );
        var parts = Parts();
        parts["word/media/large.bin"] = new string('a', 16 * 1024 * 1024);
        Assert.Equal(413, Assert.Throws<DocumentImportException>(() => Read(parts)).Status);
        parts = Parts();
        for (var i = 0; i < 254; i++)
            parts[$"extra/{i}.xml"] = "<x/>";
        Assert.Equal(413, Assert.Throws<DocumentImportException>(() => Read(parts)).Status);
        Assert.Equal(
            413,
            Assert
                .Throws<DocumentImportException>(() =>
                    Read(Parts("<w:p><w:r><w:t>" + new string('é', 32769) + "</w:t></w:r></w:p>"))
                )
                .Status
        );
        Assert.Equal(
            413,
            Assert.Throws<DocumentImportException>(() => Read(Parts(new string(' ', 1024 * 1024)))).Status
        );
    }

    [Fact]
    public void MalformedEncryptedEmptyAndWrongFormatFilesDoNotProducePartialText()
    {
        Assert.Throws<DocumentImportException>(() => WordTextImport.Extract([0xd0, 0xcf, 0x11, 0xe0], 65536));
        Assert.Throws<DocumentImportException>(() => Read(Parts("")));
        Assert.Throws<DocumentImportException>(() => Read(Parts("<w:p>")));
        var parts = Parts();
        parts.Remove("[Content_Types].xml");
        Assert.Throws<DocumentImportException>(() => Read(parts));
    }
}
