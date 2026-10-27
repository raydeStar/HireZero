using HireZero.Core;

public sealed partial class CompanyMemoryTests
{
    private static CompanyCommand Delete(CompanyMemoryItem memory) =>
        C("memory.delete") with
        {
            Id = memory.Id,
            ExpectedVersion = memory.Version,
            Digest = memory.Digest,
            Decision = "delete-unapproved",
        };

    [Fact]
    public void DeletionClearsAllSavedDraftVersionsAndCannotReplayAnOldSave()
    {
        const string marker = "Fictional amber draft secret";
        var store = Store();
        var other = Store("memory-deletion-neighbor");
        var retained = Save(other, Input(marker) with { Statement = marker });
        var create = C("memory.save") with { Body = CompanyJson.Encode(Input(marker) with { Statement = marker }) };
        var draft = Apply<CompanyMemoryItem>(store, create);
        var edit = C("memory.save") with
        {
            Id = draft.Id,
            ExpectedVersion = draft.Version,
            Body = CompanyJson.Encode(draft.Definition with { Rationale = marker }),
        };
        var edited = Apply<CompanyMemoryItem>(store, edit);
        var discard = Review(edited, "discard");
        var discarded = Apply<CompanyMemoryItem>(store, discard);
        var deletion = Delete(discarded);
        var receipt = store.Apply(deletion);
        var deleted = CompanyJson.Decode<CompanyMemoryItem>(receipt);
        Assert.Equal("deleted", deleted.State);
        Assert.Equal(discarded.Version + 1, deleted.Version);
        Assert.Equal(discarded.Digest, deleted.Digest);
        Assert.Equal("", deleted.Definition.Statement);
        Assert.Equal("", deleted.Definition.Rationale);
        Assert.Null(deleted.Origin);
        Assert.Null(deleted.Replacement);
        Assert.Null(deleted.Definition.RevisionSource);
        Assert.Empty(store.Memories());
        Assert.False(store.Memory(deleted.Id).AnalysisEligible);
        Assert.Equal(deleted, Store().Memory(deleted.Id).Memory);
        foreach (var command in new[] { create, edit, discard, deletion })
        {
            Assert.Equal(receipt, store.Operation(command.RequestId));
            Assert.Equal(receipt, store.Apply(command, out var applied));
            Assert.False(applied);
        }
        Assert.Throws<WorkspaceException>(() => store.Apply(create with { Body = "changed" }));
        Assert.Throws<WorkspaceException>(() => Save(store, Input(), deleted));
        Assert.Throws<WorkspaceException>(() => Approve(store, deleted));
        Assert.Empty(store.RetrieveMemory("amber").Items);
        using var db = new StoreConnection(store.Database.Open(), store.Database);
        foreach (var table in new[] { "co_memory", "co_events" })
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
        Assert.Equal(retained, other.Memory(retained.Id).Memory);
        Assert.True(store.VerifyAudit().Intact);
        Assert.Single(store.AuditEntries(), e => e.Kind == "memory.deleted");
    }

    [Fact]
    public void DeletionRequiresOwnerExactConsentAndNeverApprovedState()
    {
        var store = Store();
        store.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = "approver" });
        var member = store.SignIn("fictional-memory-reviewer", "reviewer@example.invalid", true, "Fictional Reviewer")!;
        var draft = Save(store);
        var command = Delete(draft);
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => store.Apply(command, member.Subject, out _)).Status);
        Assert.Equal(404, Assert.Throws<WorkspaceException>(() => Store("other-memory").Apply(command)).Status);
        foreach (
            var wrong in new[]
            {
                command with
                {
                    Digest = "wrong",
                },
                command with
                {
                    ExpectedVersion = 0,
                },
                command with
                {
                    Decision = null,
                },
            }
        )
            Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.Apply(wrong)).Status);
        var approved = Approve(store, draft);
        Assert.Throws<WorkspaceException>(() => store.Apply(command));
        Assert.Throws<WorkspaceException>(() => store.Apply(Delete(approved)));
        var revoked = Apply<CompanyMemoryItem>(store, Review(approved, "revoke"));
        Assert.Throws<WorkspaceException>(() => store.Apply(Delete(revoked)));
        Assert.Equal(revoked, Store().Memory(revoked.Id).Memory);
    }

    [Fact]
    public void DeletedDraftsReleaseRetainedStorageButKeepTheirTombstone()
    {
        var store = Store();
        var items = Enumerable.Range(0, 200).Select(i => Save(store, Input("Fictional draft " + i))).ToArray();
        Assert.Equal(429, Assert.Throws<WorkspaceException>(() => Save(store)).Status);
        Apply<CompanyMemoryItem>(store, Delete(items[0]));
        var replacement = Save(store);
        Assert.Equal(200, store.Memories().Length);
        Assert.Equal("deleted", store.Memory(items[0].Id).Memory.State);
        Assert.NotEqual(items[0].Id, replacement.Id);
        Assert.Equal(429, Assert.Throws<WorkspaceException>(() => Save(store)).Status);
    }

    [Fact]
    public async Task ApprovalAndDeletionRaceCannotBothSucceed()
    {
        var store = Store();
        var draft = Save(store);
        using var start = new ManualResetEventSlim();
        Task<bool> Submit(CompanyCommand command) =>
            Task.Run(() =>
            {
                start.Wait();
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
        var approving = Submit(Review(draft));
        var deleting = Submit(Delete(draft));
        start.Set();
        Assert.Single(await Task.WhenAll(approving, deleting), success => success);
        var final = Store().Memory(draft.Id).Memory;
        Assert.Contains(final.State, new[] { "approved", "deleted" });
        Assert.Equal(final.State == "approved" ? 1 : 0, store.RetrieveMemory("cobalt").Items.Length);
        Assert.True(store.VerifyAudit().Intact);
    }
}
