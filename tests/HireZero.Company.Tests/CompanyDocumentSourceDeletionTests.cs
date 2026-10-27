using HireZero.Core;

public sealed partial class CompanyDocumentTests
{
    private static CompanyCommand DeleteSource(CompanyDocument document) =>
        C("document.source.delete") with
        {
            Id = document.Id,
            ExpectedVersion = document.Version,
            Digest = document.Digest,
            Decision = "delete-revoked-source",
        };

    [Fact]
    public void RevokedSourceDeletionErasesTheLibraryAndReceiptsButPreservesPriorTaskEvidence()
    {
        const string marker = "Fictional cobalt willow source marker";
        var store = Store();
        var work = MeasuredQueueChecks.Seed(store);
        var create = C("document.save") with
        {
            Body = CompanyJson.Encode(new CompanyDocumentInput(marker, marker + " original", SourceName: "willow.md")),
        };
        var draft = Apply<CompanyDocument>(store, create);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.Apply(DeleteSource(draft))).Status);
        var edit = create with
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Id = draft.Id,
            ExpectedVersion = draft.Version,
            Body = CompanyJson.Encode(new CompanyDocumentInput(marker, marker + " corrected", SourceName: "willow.md")),
        };
        draft = Apply<CompanyDocument>(store, edit);
        var approval = Review(draft);
        var approved = Apply<CompanyDocument>(store, approval);
        var attempt = Prepare(store, work, "Draft a cobalt willow campaign note");
        var packet = CompanyJson.Encode(store.TextCall(attempt.Id));
        Assert.Contains(marker, packet);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.Apply(DeleteSource(approved))).Status);
        var revocation = Review(approved, "revoke");
        var revoked = Apply<CompanyDocument>(store, revocation);
        var deletion = DeleteSource(revoked);
        Assert.Throws<WorkspaceException>(() => store.Apply(deletion with { ExpectedVersion = approved.Version }));
        Assert.Throws<WorkspaceException>(() => store.Apply(deletion with { Digest = "wrong" }));
        Assert.Throws<WorkspaceException>(() => store.Apply(deletion with { Decision = "delete-unapproved" }));
        var neighbor = Store("co-source-neighbor");
        var separate = Approve(neighbor, Save(neighbor, marker));
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => neighbor.Apply(deletion)).Status);

        var receipt = store.Apply(deletion);
        var deleted = CompanyJson.Decode<CompanyDocument>(receipt);
        Assert.Equal("deleted", deleted.State);
        Assert.Equal("revoked-source", deleted.DeletionScope);
        Assert.Empty(deleted.Text);
        Assert.Null(deleted.SourceName);
        Assert.Null(deleted.DepartmentId);
        Assert.Null(deleted.EmployeeId);
        Assert.Null(deleted.ReplacesId);
        Assert.DoesNotContain(marker, deleted.Title);
        Assert.Equal(revoked.Digest, deleted.Digest);
        Assert.Equal(revoked.Version + 1, deleted.Version);
        Assert.Equal(deleted, Store().Document(deleted.Id));
        Assert.Empty(store.Documents());
        Assert.Empty(store.RetrieveDocuments("cobalt willow").Excerpts);
        Assert.Equal(receipt, store.Apply(deletion, out var repeated));
        Assert.False(repeated);
        foreach (var prior in new[] { create, edit, approval, revocation })
        {
            Assert.Equal(receipt, store.Operation(prior.RequestId));
            Assert.Equal(receipt, store.Apply(prior, out var applied));
            Assert.False(applied);
        }
        Assert.Throws<WorkspaceException>(() => store.Apply(edit with { RequestId = Guid.NewGuid().ToString("D") }));
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(deleted)));
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.Apply(Run(attempt))).Status);
        Assert.Equal(packet, CompanyJson.Encode(Store().TextCall(attempt.Id)));
        Assert.Equal(marker, neighbor.Document(separate.Id).Text);
        Assert.True(store.VerifyAudit().Intact);
        Assert.DoesNotContain(marker, CompanyJson.Encode(store.AuditEntries()));

        using var db = new StoreConnection(store.Database.Open(), store.Database);
        foreach (var table in new[] { "co_documents", "co_document_chunks", "co_events" })
            Assert.DoesNotContain(
                marker,
                string.Join(
                    "\n",
                    db.Rows($"SELECT payload FROM {table} WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
                )
            );
        Assert.Empty(
            db.Rows("SELECT term FROM co_document_terms WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
        );
        foreach (var prior in new[] { create, edit, approval, revocation })
            Assert.DoesNotContain(marker, store.Operation(prior.RequestId)!);
    }

    [Fact]
    public void DeletingTheOldSourcePreservesSeparateRevisionDraftsWithoutRestoringItsApproval()
    {
        var store = Store();
        var approved = Approve(store, Save(store));
        var revision = Save(store, "Fictional revised cobalt plan", replaces: approved.Id);
        var revoked = Apply<CompanyDocument>(store, Review(approved, "revoke"));
        var deleted = Apply<CompanyDocument>(store, DeleteSource(revoked));
        Assert.Equal(revision, store.Document(revision.Id));
        var edit = C("document.save") with
        {
            Id = revision.Id,
            ExpectedVersion = revision.Version,
            Body = CompanyJson.Encode(
                new CompanyDocumentInput("Revised guide", "Fictional new cobalt draft", ReplacesId: deleted.Id)
            ),
        };
        revision = Apply<CompanyDocument>(store, edit);
        Assert.Equal("draft", revision.State);
        Assert.Empty(store.RetrieveDocuments("cobalt").Excerpts);
        var reviewed = Approve(store, revision);
        Assert.Equal(reviewed.Id, Assert.Single(store.RetrieveDocuments("cobalt").Excerpts).DocumentId);
        Assert.Equal("deleted", store.Document(deleted.Id).State);
        var later = Save(store, "Fictional further correction", replaces: deleted.Id);
        Assert.Equal("draft", later.State);
        var unused = Apply<CompanyDocument>(store, Delete(Save(store)));
        Assert.Equal("unapproved", unused.DeletionScope);
        Assert.Throws<WorkspaceException>(() => Save(store, "Fictional revision", replaces: unused.Id));
        Assert.True(store.VerifyAudit().Intact);
    }
}
