using HireZero.Core;

public sealed partial class CompanyMemoryTests
{
    private static CompanyCommand DeleteSource(CompanyMemoryItem memory) =>
        C("memory.source.delete") with
        {
            Id = memory.Id,
            ExpectedVersion = memory.Version,
            Digest = memory.Digest,
            Decision = "delete-retired-source",
        };

    [Fact]
    public void RetiredMemoryDeletionClearsSavedVersionsWithoutErasingEarlierTaskEvidence()
    {
        const string marker = "Fictional cobalt amber retired statement";
        var store = Store();
        var work = MeasuredQueueChecks.Seed(store);
        var neighbor = Store("co-memory-source-neighbor");
        var separate = Approve(neighbor, Save(neighbor, Input() with { Statement = marker }));
        var create = C("memory.save") with { Body = CompanyJson.Encode(Input() with { Statement = marker }) };
        var draft = Apply<CompanyMemoryItem>(store, create);
        var edit = C("memory.save") with
        {
            Id = draft.Id,
            ExpectedVersion = draft.Version,
            Body = CompanyJson.Encode(draft.Definition with { Rationale = marker }),
        };
        draft = Apply<CompanyMemoryItem>(store, edit);
        var approval = Review(draft);
        var approved = Apply<CompanyMemoryItem>(store, approval);
        var attempt = Prepare(store, work);
        var packet = CompanyJson.Encode(store.TextCall(attempt.Id));
        Assert.Contains(marker, packet);
        var revocation = Review(approved, "revoke");
        var revoked = Apply<CompanyMemoryItem>(store, revocation);
        var deletion = DeleteSource(revoked);
        var receipt = store.Apply(deletion);
        var deleted = CompanyJson.Decode<CompanyMemoryItem>(receipt);
        Assert.Equal("deleted", deleted.State);
        Assert.Equal("retired-source", deleted.DeletionScope);
        Assert.Equal(revoked.Version + 1, deleted.Version);
        Assert.Equal(revoked.Digest, deleted.Digest);
        Assert.Equal(revoked.ApprovedAt, deleted.ApprovedAt);
        Assert.Empty(deleted.Definition.Statement);
        Assert.Empty(deleted.Definition.Rationale);
        Assert.Null(deleted.Origin);
        Assert.Null(deleted.MeetingOrigin);
        Assert.Null(deleted.Replacement);
        Assert.Null(deleted.Definition.ArtifactId);
        Assert.Null(deleted.Definition.MeetingId);
        Assert.Null(deleted.Definition.EmployeeId);
        Assert.Null(deleted.Definition.DepartmentId);
        Assert.Null(deleted.Definition.Quote);
        Assert.Null(deleted.Definition.RevisionSource);
        Assert.Equal(deleted, Store().Memory(deleted.Id).Memory);
        Assert.Empty(store.Memories());
        Assert.Empty(store.RetrieveMemory("cobalt").Items);
        Assert.False(store.Memory(deleted.Id).AnalysisEligible);
        foreach (var command in new[] { create, edit, approval, revocation, deletion })
        {
            Assert.Equal(receipt, store.Operation(command.RequestId));
            Assert.Equal(receipt, store.Apply(command, out var applied));
            Assert.False(applied);
        }
        Assert.Throws<WorkspaceException>(() => store.Apply(edit with { RequestId = Guid.NewGuid().ToString("D") }));
        Assert.Throws<WorkspaceException>(() => Approve(store, deleted));
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(attempt)));
        Assert.Equal(packet, CompanyJson.Encode(Store().TextCall(attempt.Id)));
        Assert.Equal(separate, neighbor.Memory(separate.Id).Memory);
        using var db = new StoreConnection(store.Database.Open(), store.Database);
        Assert.DoesNotContain(
            marker,
            string.Join(
                "\n",
                db.Rows("SELECT payload FROM co_memory WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
            )
        );
        Assert.DoesNotContain(
            marker,
            string.Join(
                "\n",
                db.Rows("SELECT result FROM co_operations WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
            )
        );
        Assert.Empty(
            db.Rows("SELECT term FROM co_memory_terms WHERE company_id=$0", r => r.GetString(0), store.CompanyId)
        );
        Assert.True(store.VerifyAudit().Intact);
        Assert.Single(store.AuditEntries(), e => e.Kind == "memory.deleted");
    }

    [Fact]
    public void SavedMemoryDeletionRequiresCurrentOwnerExactConsentAndPriorRetirement()
    {
        var store = Store();
        store.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = "approver" });
        var member = store.SignIn("fictional-memory-reviewer", "reviewer@example.invalid", true, "Fictional Reviewer")!;
        var draft = Save(store);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.Apply(DeleteSource(draft))).Status);
        var approved = Approve(store, draft);
        Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.Apply(DeleteSource(approved))).Status);
        var revoked = Apply<CompanyMemoryItem>(store, Review(approved, "revoke"));
        var command = DeleteSource(revoked);
        Assert.Equal(CompanyRoles.Owner, CompanyAccess.Required(command.Action));
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => store.Apply(command, member.Subject, out _)).Status);
        Assert.Equal(
            404,
            Assert.Throws<WorkspaceException>(() => Store("co-other-memory-source").Apply(command)).Status
        );
        foreach (
            var wrong in new[]
            {
                command with
                {
                    Digest = "wrong",
                },
                command with
                {
                    ExpectedVersion = approved.Version,
                },
                command with
                {
                    Decision = "delete-unapproved",
                },
            }
        )
            Assert.Equal(409, Assert.Throws<WorkspaceException>(() => store.Apply(wrong)).Status);
        Assert.Equal(revoked, Store().Memory(revoked.Id).Memory);
        Assert.Throws<WorkspaceException>(() => store.Apply(Delete(revoked)));
    }

    [Fact]
    public void DeletingReplacedMemoryPreservesItsApprovedReplacementAndOtherDrafts()
    {
        var store = Store();
        var initial = Approve(store, Save(store));
        var pending = Save(store, Input("Unfinished cobalt revision") with { ReplacesId = initial.Id });
        var replacement = Approve(
            store,
            Save(store, Input() with { ReplacesId = initial.Id, Statement = "Fictional cobalt replacement" })
        );
        var old = store.Memory(initial.Id).Memory;
        Assert.Equal("superseded", old.State);
        var deleted = Apply<CompanyMemoryItem>(store, DeleteSource(old));
        Assert.Equal(pending, store.Memory(pending.Id).Memory);
        Assert.Equal(replacement, store.Memory(replacement.Id).Memory);
        Assert.Equal(replacement.Id, Assert.Single(store.RetrieveMemory("cobalt").Items).MemoryId);
        Assert.Throws<WorkspaceException>(() => Approve(store, pending));
        Assert.Equal("deleted", deleted.State);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void ASeparatelyReviewedRevisionMayReplaceADeletedSourceWithoutRestoringIt()
    {
        var store = Store();
        var original = Approve(store, Save(store));
        var pending = Save(store, Input() with { ReplacesId = original.Id });
        var revoked = Apply<CompanyMemoryItem>(store, Review(original, "revoke"));
        var deleted = Apply<CompanyMemoryItem>(store, DeleteSource(revoked));
        Assert.Throws<WorkspaceException>(() => Approve(store, pending));
        pending = Save(store, pending.Definition, pending);
        Assert.Equal(deleted.Version, pending.Replacement!.Version);
        var replacement = Approve(store, pending);
        Assert.Equal(replacement.Id, Assert.Single(store.RetrieveMemory("cobalt").Items).MemoryId);
        Assert.Equal(deleted, Store().Memory(deleted.Id).Memory);
        var unused = Apply<CompanyMemoryItem>(store, Delete(Save(store, Input("Unused memory"))));
        Assert.Throws<WorkspaceException>(() => Save(store, Input("Invalid revision") with { ReplacesId = unused.Id }));
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public async Task RetiredSourceDeletionAndReplacementApprovalCannotUseTheSameOldVersion()
    {
        var store = Store();
        var approved = Approve(store, Save(store));
        var revoked = Apply<CompanyMemoryItem>(store, Review(approved, "revoke"));
        var draft = Save(store, Input() with { ReplacesId = revoked.Id });
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
        var deleting = Submit(DeleteSource(revoked));
        var replacing = Submit(Review(draft, "approve-and-replace"));
        start.Set();
        Assert.Single(await Task.WhenAll(deleting, replacing), success => success);
        var retired = Store().Memory(revoked.Id).Memory;
        Assert.Contains(retired.State, new[] { "deleted", "superseded" });
        Assert.Equal(retired.State == "deleted" ? "draft" : "approved", store.Memory(draft.Id).Memory.State);
        Assert.Equal(retired.State == "deleted" ? 0 : 1, store.RetrieveMemory("cobalt").Items.Length);
        Assert.True(store.VerifyAudit().Intact);
    }
}
