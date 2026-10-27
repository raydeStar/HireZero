namespace HireZero.Core;

public sealed record CompanyDocumentSource(
    string Provider,
    string ExternalId,
    string Version,
    DateTimeOffset ModifiedAt,
    DateTimeOffset CopiedAt,
    string ContentDigest,
    string CopyDigest
);

public sealed record CompanyDocumentOrigin(CompanyDocumentSource Source, string Receipt, bool TextEdited);

public sealed record CompanyDocumentCitation(CompanyDocumentSource Source, bool TextEdited);

public sealed partial class CompanyStore
{
    // The host verifies its company-bound import receipt; source labels typed into a document prove nothing.
    public Func<string, CompanyDocumentSource>? DocumentSourceVerifier { get; set; }

    private CompanyDocumentOrigin? DocumentOrigin(
        StoreConnection db,
        CompanyDocumentInput input,
        CompanyDocument? previous,
        string text
    )
    {
        if (input.SourceReceipt is not { } receipt)
            return null;
        if (receipt.Length is < 1 or > 4096)
            throw new WorkspaceException(
                "The import receipt is invalid. Preview the source again; keep your edited text.",
                422
            );
        var retained = previous?.Origin;
        if (retained?.Receipt != receipt && input.ReplacesId is { } id)
            retained = Need<CompanyDocument>(db, id).Origin;
        var source =
            retained?.Receipt == receipt
                ? retained.Source
                : DocumentSourceVerifier?.Invoke(receipt)
                    ?? throw new WorkspaceException(
                        "This host cannot verify the import receipt. Preview the source again.",
                        409
                    );
        return new(source, receipt, CompanyJson.Digest(text) != source.CopyDigest);
    }
}
