using HireZero.Core;
using Microsoft.AspNetCore.DataProtection;

public sealed partial class CompanyDocumentTests
{
    private const string ImportedText = "Cobalt source uses patient paper demonstrations.";

    private static CompanyDocumentSource Source() =>
        new(
            "google-drive",
            "fictional_source_id",
            "7",
            new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
            CompanyJson.Digest(ImportedText),
            CompanyJson.Digest(ImportedText)
        );

    private static CompanyCommand Imported(string receipt, string text = ImportedText) =>
        C("document.save") with
        {
            Body = CompanyJson.Encode(new CompanyDocumentInput("Fictional source copy", text, SourceReceipt: receipt)),
        };

    [Fact]
    public void SourceReceiptIsCompanyBoundAndTextEditsCannotRewriteRecordedProvenance()
    {
        var protection = new EphemeralDataProtectionProvider();
        var store = Store();
        var imports = new CompanyDocumentImports(protection, store.CompanyId);
        store.DocumentSourceVerifier = imports.Verify;
        var source = Source();
        var receipt = imports.Seal(source);
        var draft = Apply<CompanyDocument>(store, Imported(receipt));
        Assert.Equal(source, draft.Origin!.Source);
        Assert.False(draft.Origin.TextEdited);
        Assert.Empty(store.RetrieveDocuments("cobalt").Excerpts);
        var other = Store("fictional-source-neighbor");
        other.DocumentSourceVerifier = new CompanyDocumentImports(protection, other.CompanyId).Verify;
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => other.Apply(Imported(receipt))).Status);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.Apply(Imported("forged-receipt"))).Status);
        var edited = Apply<CompanyDocument>(
            store,
            Imported(receipt, "Owner corrected the cobalt guidance.") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
            }
        );
        Assert.True(edited.Origin!.TextEdited);
        Assert.Equal(source, edited.Origin.Source);
        Assert.NotEqual(draft.Digest, edited.Digest);
        var approved = Approve(store, edited);
        var excerpt = Assert.Single(store.RetrieveDocuments("cobalt").Excerpts);
        Assert.Equal(new CompanyDocumentCitation(source, true), excerpt.Source);
        Assert.DoesNotContain(receipt, CompanyJson.Encode(excerpt));
        Assert.Equal(approved.Id, excerpt.DocumentId);
    }

    [Fact]
    public void SourceSurvivesExplicitRevisionAndRevocationStillFencesPreparedTasks()
    {
        var store = Store();
        var imports = new CompanyDocumentImports(new EphemeralDataProtectionProvider(), store.CompanyId);
        store.DocumentSourceVerifier = imports.Verify;
        var receipt = imports.Seal(Source());
        var approved = Approve(store, Apply<CompanyDocument>(store, Imported(receipt)));
        var work = MeasuredQueueChecks.Seed(store);
        var attempt = Prepare(store, work);
        Assert.NotNull(Assert.Single(store.TextCall(attempt.Id).Context.Documents!.Excerpts).Source);
        // An already retained source remains reviewable even if its original host key is no longer available.
        store.DocumentSourceVerifier = null;
        var draft = Apply<CompanyDocument>(
            store,
            C("document.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyDocumentInput(
                        "Revised guide",
                        "New cobalt wording",
                        ReplacesId: approved.Id,
                        SourceReceipt: receipt
                    )
                ),
            }
        );
        Assert.Equal(approved.Origin!.Source, draft.Origin!.Source);
        Assert.True(draft.Origin.TextEdited);
        Assert.Throws<WorkspaceException>(() => Approve(store, draft));
        store.Apply(Review(approved, "revoke"));
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(
                C("measured.run") with
                {
                    Id = attempt.Id,
                    ExpectedVersion = attempt.Version,
                    Digest = attempt.Measured!.Digest,
                    Decision = "accept-soft-limits",
                }
            )
        );
        Assert.Empty(store.RetrieveDocuments("cobalt").Excerpts);
        Approve(store, draft);
        Assert.Equal(draft.Id, Assert.Single(store.RetrieveDocuments("cobalt").Excerpts).DocumentId);
    }

    [Fact]
    public void DeletionErasesImportReceiptFromDocumentAndEarlierSaveResults()
    {
        var store = Store();
        var imports = new CompanyDocumentImports(new EphemeralDataProtectionProvider(), store.CompanyId);
        store.DocumentSourceVerifier = imports.Verify;
        var receipt = imports.Seal(Source());
        var command = Imported(receipt);
        var draft = Apply<CompanyDocument>(store, command);
        var deleted = Apply<CompanyDocument>(
            store,
            C("document.delete") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
                Digest = draft.Digest,
                Decision = "delete-unapproved",
            }
        );
        Assert.Null(deleted.Origin);
        Assert.DoesNotContain(receipt, store.Operation(command.RequestId)!);
        Assert.Equal(deleted, Apply<CompanyDocument>(store, command));
    }

    [Fact]
    public void ExistingDocumentDigestsAndExcerptShapesStayCompatibleWithoutSourceReceipts()
    {
        var store = Store();
        var draft = Save(store);
        var input = new CompanyDocumentInput(
            draft.Title,
            draft.Text,
            draft.DepartmentId,
            draft.EmployeeId,
            draft.SourceName,
            draft.ReplacesId
        );
        Assert.Equal(CompanyJson.Digest(input), draft.Digest);
        Assert.DoesNotContain("sourceReceipt", CompanyJson.Encode(input));
        Assert.DoesNotContain("origin", CompanyJson.Encode(draft));
        Approve(store, draft);
        Assert.DoesNotContain(
            "\"source\":",
            CompanyJson.Encode(Assert.Single(store.RetrieveDocuments("cobalt").Excerpts))
        );
    }
}
