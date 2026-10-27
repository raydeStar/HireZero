using HireZero.Core;

public sealed partial class CompanyDocumentTests
{
    private static CompanyCommand Delete(CompanyDocument document) =>
        C("document.delete") with
        {
            Id = document.Id,
            ExpectedVersion = document.Version,
            Digest = document.Digest,
            Decision = "delete-unapproved",
        };

    private static CompanyCommand Retain(CompanyDocumentRetention policy, int days) =>
        C("document.retention") with
        {
            ExpectedVersion = policy.Version,
            Body = CompanyJson.Encode(new CompanyDocumentRetentionInput(days)),
            Decision = "apply-retention",
        };

    [Fact]
    public void DeletingAnUnapprovedDocumentErasesHistoricalReceiptsWithoutReplayingThem()
    {
        const string marker = "Fictional amber erasure marker";
        var store = Store();
        var create = C("document.save") with
        {
            Body = CompanyJson.Encode(
                new CompanyDocumentInput(marker, marker + " first text", SourceName: "amber.txt")
            ),
        };
        var first = Apply<CompanyDocument>(store, create);
        var edit = create with
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Id = first.Id,
            ExpectedVersion = first.Version,
            Body = CompanyJson.Encode(new CompanyDocumentInput(marker + " revised", marker + " second text")),
        };
        var revised = Apply<CompanyDocument>(store, edit);
        var discard = Review(revised, "discard");
        var discarded = Apply<CompanyDocument>(store, discard);
        var neighbor = Store("co-erasure-neighbor");
        var separate = Save(neighbor, marker);
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => neighbor.Apply(Delete(discarded))).Status);
        var delete = Delete(discarded);
        Assert.Throws<WorkspaceException>(() => store.Apply(delete with { ExpectedVersion = first.Version }));
        Assert.Throws<WorkspaceException>(() => store.Apply(delete with { Digest = "wrong" }));
        Assert.Throws<WorkspaceException>(() => store.Apply(delete with { Decision = "discard" }));
        var receipt = store.Apply(delete);
        var erased = CompanyJson.Decode<CompanyDocument>(receipt);
        Assert.Equal("deleted", erased.State);
        Assert.Empty(erased.Text);
        Assert.Null(erased.SourceName);
        Assert.Null(erased.EmployeeId);
        Assert.Null(erased.DepartmentId);
        Assert.Null(erased.ReplacesId);
        Assert.Equal(discarded.Digest, erased.Digest);
        Assert.Equal(discarded.Version + 1, erased.Version);
        Assert.Empty(store.Documents());
        Assert.Equal(erased, Store().Document(erased.Id));
        Assert.Equal(receipt, store.Apply(delete, out var repeated));
        Assert.False(repeated);
        foreach (var previous in new[] { create, edit, discard })
        {
            Assert.Equal(receipt, store.Operation(previous.RequestId));
            Assert.Equal(receipt, store.Apply(previous, out var applied));
            Assert.False(applied);
        }
        Assert.Throws<WorkspaceException>(() => store.Apply(edit with { RequestId = Guid.NewGuid().ToString("D") }));
        Assert.Throws<WorkspaceException>(() => store.Apply(create with { Body = "changed" }));
        Assert.Throws<WorkspaceException>(() => store.Apply(Review(erased)));
        using var db = new StoreConnection(store.Database.Open(), store.Database);
        foreach (var table in new[] { "co_documents", "co_events", "co_document_chunks" })
            Assert.DoesNotContain(
                marker,
                string.Join(
                    "\n",
                    db.Rows($"SELECT payload FROM {table} WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
                )
            );
        Assert.DoesNotContain(
            marker,
            string.Join(
                "\n",
                db.Rows("SELECT result FROM co_operations WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
            )
        );
        Assert.DoesNotContain(marker, CompanyJson.Encode(store.AuditEntries()));
        Assert.Empty(store.RetrieveDocuments("amber").Excerpts);
        Assert.Equal(marker, neighbor.Document(separate.Id).Text);
        Assert.True(store.VerifyAudit().Intact);
        Assert.Single(store.AuditEntries(), e => e.Kind == "document.deleted");
    }

    [Fact]
    public void PreviouslyApprovedDocumentsAndTheirSavedPacketsCannotUseDraftErasure()
    {
        var store = Store();
        var work = MeasuredQueueChecks.Seed(store);
        var approved = Approve(store, Save(store));
        var attempt = Prepare(store, work);
        var context = CompanyJson.Encode(store.TextCall(attempt.Id));
        Assert.Throws<WorkspaceException>(() => store.Apply(Delete(approved)));
        var revoked = Apply<CompanyDocument>(store, Review(approved, "revoke"));
        Assert.Throws<WorkspaceException>(() => store.Apply(Delete(revoked)));
        Assert.Equal(context, CompanyJson.Encode(store.TextCall(attempt.Id)));
        Assert.Equal(revoked, Store().Document(revoked.Id));
    }

    [Fact]
    public void RetentionUsesDiscardTimeIsOptInAndNeverRemovesActiveOrReviewedDocuments()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var store = new CompanyStore(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions())
        {
            Clock = () => now,
        };
        var old = Save(store);
        var active = Save(store);
        var reviewed = Approve(store, Save(store));
        var revoked = Apply<CompanyDocument>(store, Review(Approve(store, Save(store)), "revoke"));
        var oldDiscard = Review(old, "discard");
        old = Apply<CompanyDocument>(store, oldDiscard);
        now = now.AddDays(7).AddTicks(-1);
        Assert.Equal(0, store.MaintainDocumentRetention());
        var policyCommand = Retain(store.DocumentRetention(), 7);
        var policy = Apply<CompanyDocumentRetention>(store, policyCommand);
        Assert.Equal("discarded", store.Document(old.Id).State);
        now = now.AddTicks(1);
        Assert.Equal(1, store.MaintainDocumentRetention());
        Assert.Equal("deleted", store.Document(old.Id).State);
        Assert.Equal("deleted", CompanyJson.Decode<CompanyDocument>(store.Apply(oldDiscard)).State);
        Assert.Equal(0, store.MaintainDocumentRetention());
        Assert.Equal("draft", store.Document(active.Id).State);
        var lateDiscard = Apply<CompanyDocument>(store, Review(active, "discard"));
        Assert.Equal(0, store.MaintainDocumentRetention());
        now = now.AddDays(7);
        var disabled = Apply<CompanyDocumentRetention>(store, Retain(policy, 0));
        Assert.Equal(0, store.MaintainDocumentRetention());
        // Reconciliation returns the old policy receipt, but it must not enable that policy again.
        Assert.Equal(policy, Apply<CompanyDocumentRetention>(store, policyCommand));
        Assert.Equal(disabled, Store().DocumentRetention());
        Assert.Equal("discarded", store.Document(lateDiscard.Id).State);
        Assert.Throws<WorkspaceException>(() => store.Apply(Retain(disabled, 2)));
        Assert.Throws<WorkspaceException>(() => store.Apply(Retain(disabled, 30) with { Decision = null }));
        now = now.AddDays(30);
        Apply<CompanyDocumentRetention>(store, Retain(disabled, 30));
        Assert.Equal("deleted", store.Document(lateDiscard.Id).State);
        Assert.Equal(reviewed, store.Document(reviewed.Id));
        Assert.Equal(revoked, store.Document(revoked.Id));
        Assert.Equal(0, Store("co-retention-other").DocumentRetention().DiscardedDays);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public async Task ApprovalRacingDeletionCannotAuthorizeAnErasedDocument()
    {
        var store = Store();
        var document = Save(store);
        using var ready = new ManualResetEventSlim();
        Task<bool> Submit(CompanyCommand command) =>
            Task.Run(() =>
            {
                ready.Wait();
                try
                {
                    store.Apply(command);
                    return true;
                }
                catch (WorkspaceException)
                {
                    return false;
                }
            });
        var approve = Submit(Review(document));
        var delete = Submit(Delete(document));
        ready.Set();
        var outcomes = await Task.WhenAll(approve, delete);
        Assert.Single(outcomes, succeeded => succeeded);
        var current = Store().Document(document.Id);
        if (current.State == "approved")
        {
            Assert.Equal(document.Text, current.Text);
            Assert.Single(store.RetrieveDocuments("cobalt").Excerpts);
        }
        else
        {
            Assert.Equal("deleted", current.State);
            Assert.Empty(current.Text);
            Assert.Empty(store.RetrieveDocuments("cobalt").Excerpts);
        }
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void RetentionMigrationKeepsDocumentsAndEarlierChecksumsWithDeletionDisabled()
    {
        var store = Store();
        var doc = Save(store);
        string[] checksums;
        using (var db = new StoreConnection(store.Database.Open(), store.Database))
        {
            checksums = db.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            );
            db.Execute(
                "DROP TABLE IF EXISTS co_task_cost_reviews; DROP TABLE IF EXISTS co_memory_analyses; DROP TABLE IF EXISTS co_billing_events; DROP TABLE IF EXISTS co_billing_portals; DROP TABLE IF EXISTS co_billing_objects; DROP TABLE IF EXISTS co_billing_checkouts; DROP TABLE IF EXISTS co_billing_state; DROP TABLE IF EXISTS co_browser_retention; DROP TABLE IF EXISTS co_browser_jobs; DROP TABLE co_document_retention; DELETE FROM module_schema_migrations WHERE owner='company' AND version>=22;"
            );
        }
        var reopened = Store();
        Assert.Equal(doc, reopened.Document(doc.Id));
        Assert.Equal(0, reopened.DocumentRetention().DiscardedDays);
        Assert.Equal(0, reopened.MaintainDocumentRetention());
        using var check = new StoreConnection(reopened.Database.Open(), reopened.Database);
        Assert.Equal(
            checksums,
            check.Rows(
                "SELECT checksum FROM module_schema_migrations WHERE owner='company' ORDER BY version",
                r => r.GetString(0)
            )
        );
    }
}
