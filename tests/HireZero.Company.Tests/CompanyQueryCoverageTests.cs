using HireZero.Core;

public sealed partial class CompanyDocumentTests
{
    [Fact]
    public void ATopicAfterLongRequestInstructionsStillReachesTheCapturedContext()
    {
        var store = Store();
        var work = MeasuredQueueChecks.Seed(store);
        var document = Approve(store, Save(store, "Quartzophone calibration requires lavender shims."));
        const string query =
            "Prepare concise internal recommendations organized under clear headings. Explain assumptions, uncertainties, "
            + "tradeoffs, alternatives, dependencies, constraints, timing, costs, risks, priorities, acceptance checks, "
            + "responsibilities, inputs, outputs, examples, decisions, questions, estimates, sequencing, scope and next steps. "
            + "The actual subject is quartzophone calibration.";

        var retrieved = store.RetrieveDocuments(query, work.EmployeeId);
        Assert.Equal(document.Id, Assert.Single(retrieved.Excerpts).DocumentId);
        Assert.Contains("quartzophone", retrieved.Query);
        Assert.Contains("all request terms were searched", retrieved.Notice);
        var call = store.TextCall(Prepare(store, work, query).Id);
        var excerpt = Assert.Single(call.Context.Documents!.Excerpts);
        Assert.Equal(document.Id, excerpt.DocumentId);
        Assert.Equal(document.Text.Substring(excerpt.Start, excerpt.Text.Length), excerpt.Text);
        Assert.True(excerpt.Text.Length <= 800);
        Assert.Equal(query, call.Context.Input);
    }
}

public sealed partial class CompanyMemoryTests
{
    [Fact]
    public void ALateTopicFindsApprovedMemoryWithoutWideningItsAudience()
    {
        var store = Store();
        var first = MeasuredQueueChecks.Seed(store);
        var second = MeasuredQueueChecks.Seed(store);
        var memory = Approve(
            store,
            Save(
                store,
                new(
                    "decision",
                    "Quartzophone calibration",
                    "Use lavender shims.",
                    "Calibration note.",
                    EmployeeId: first.EmployeeId
                )
            )
        );
        var query = string.Join(' ', Enumerable.Range(0, 600).Select(i => "q" + i)) + " quartzophone";
        Assert.True(query.Length < 4000);
        var result = store.RetrieveMemory(query, first.EmployeeId);
        Assert.Equal(memory.Id, Assert.Single(result.Items).MemoryId);
        Assert.Equal(memory.Digest, result.Items[0].Digest);
        Assert.True(result.Query.Length < 200);
        Assert.Contains("quartzophone", result.Query);
        Assert.Empty(store.RetrieveMemory(query, second.EmployeeId).Items);
        Assert.Empty(store.RetrieveMemory(query, first.EmployeeId, companyOnly: true).Items);
        store.Apply(Review(memory, "revoke"));
        Assert.Empty(store.RetrieveMemory(query, first.EmployeeId).Items);
    }
}
