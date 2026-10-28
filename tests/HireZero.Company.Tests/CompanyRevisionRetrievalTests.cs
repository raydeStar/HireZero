using HireZero.Core;

public sealed partial class CompanyArtifactRevisionTests
{
    private static CompanyDocument Reference(CompanyStore store, string topic)
    {
        var document = Apply<CompanyDocument>(
            store,
            C("document.save") with
            {
                Body = CompanyJson.Encode(new CompanyDocumentInput(topic, topic + " requires lavender shims.")),
            }
        );
        return Apply<CompanyDocument>(
            store,
            C("document.review") with
            {
                Id = document.Id,
                ExpectedVersion = document.Version,
                Digest = document.Digest,
                Decision = "approve",
            }
        );
    }

    private static CompanyMemoryItem Remember(CompanyStore store, string topic, string employee)
    {
        var memory = Apply<CompanyMemoryItem>(
            store,
            C("memory.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyMemoryInput(
                        "decision",
                        topic,
                        "Use lavender shims.",
                        "Calibration note.",
                        EmployeeId: employee
                    )
                ),
            }
        );
        return Apply<CompanyMemoryItem>(
            store,
            C("memory.review") with
            {
                Id = memory.Id,
                ExpectedVersion = memory.Version,
                Digest = memory.Digest,
                Decision = "approve",
            }
        );
    }

    [Fact]
    public void RevisionFeedbackRetrievesNewApprovedReferencesAndBindsTheirRevocation()
    {
        var store = Store();
        var original = Deliverable(store);
        var other = MeasuredQueueChecks.Seed(store);
        var document = Reference(store, "Quartzophone");
        var memory = Remember(store, "Copperleaf", original.EmployeeId);
        var privateMemory = Remember(store, "Copperleaf", other.EmployeeId);
        var artifact = Finish(store, original, "Fictional first draft.");
        var revision = Apply<CompanyTask>(
            store,
            Revision(artifact) with
            {
                Body = "Include quartzophone requirements and the copperleaf decision.",
            }
        );
        var prepared = Prepare(store, revision);
        var context = store.TextCall(prepared.Id).Context;

        Assert.NotNull(context.Documents);
        Assert.NotNull(context.Memory);
        Assert.Equal(document.Id, Assert.Single(context.Documents.Excerpts).DocumentId);
        Assert.Equal(memory.Id, Assert.Single(context.Memory.Items).MemoryId);
        Assert.DoesNotContain(context.Memory.Items, item => item.MemoryId == privateMemory.Id);
        Assert.Equal(original.Input, context.Input);
        Assert.Equal(revision.Revision, context.Revision!.Request);
        Assert.Equal("proposed", prepared.Measured!.State);
        store.Apply(
            C("memory.review") with
            {
                Id = memory.Id,
                ExpectedVersion = memory.Version,
                Digest = memory.Digest,
                Decision = "revoke",
            }
        );
        Assert.Throws<WorkspaceException>(() => store.Apply(Run(prepared)));
        Assert.Single(MeasuredQueueChecks.Items<CompanyArtifact>(store, "artifacts"));
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void LaterRevisionSearchIncludesEarlierFeedbackButNotTheUnapprovedDraft()
    {
        var store = Store();
        var original = Deliverable(store);
        var originalReference = Reference(store, "Folding");
        var earlierReference = Reference(store, "Quartzophone");
        var latestReference = Reference(store, "Copperleaf");
        var draftOnlyReference = Reference(store, "Zirconium");
        var earlierMemory = Remember(store, "Quartzophone", original.EmployeeId);
        var latestMemory = Remember(store, "Copperleaf", original.EmployeeId);
        var draftOnlyMemory = Remember(store, "Zirconium", original.EmployeeId);
        var first = Finish(store, original, "First fictional draft.");
        var revision = Apply<CompanyTask>(store, Revision(first) with { Body = "Add quartzophone requirements." });
        var second = Finish(store, revision, "Second fictional draft invents zirconium. Zirconium zirconium.");
        var next = Apply<CompanyTask>(store, Revision(second) with { Body = "Also apply the copperleaf decision." });
        store = Store();
        var prepared = Prepare(store, store.Work(next.Id));
        var context = store.TextCall(prepared.Id).Context;

        var documents = context.Documents!.Excerpts;
        Assert.Equal(3, documents.Length);
        Assert.Contains(documents, item => item.DocumentId == originalReference.Id);
        Assert.Contains(documents, item => item.DocumentId == earlierReference.Id);
        Assert.Contains(documents, item => item.DocumentId == latestReference.Id);
        Assert.DoesNotContain(documents, item => item.DocumentId == draftOnlyReference.Id);
        var memories = context.Memory!.Items;
        Assert.Equal(2, memories.Length);
        Assert.Contains(memories, item => item.MemoryId == earlierMemory.Id);
        Assert.Contains(memories, item => item.MemoryId == latestMemory.Id);
        Assert.DoesNotContain(memories, item => item.MemoryId == draftOnlyMemory.Id);
        Assert.Equal(revision.Revision, Assert.Single(context.Revision!.PriorFeedback!));
        Assert.Equal(second.Body, context.Revision.OriginalDraft);
        Assert.Equal(original.Input, context.Input);
        Assert.Empty(context.History);
        Assert.True(store.VerifyAudit().Intact);
    }
}
