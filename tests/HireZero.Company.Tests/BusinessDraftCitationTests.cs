using HireZero.Core;

public sealed partial class BusinessDraftTests
{
    [Fact]
    public void LayoutWhitespaceUsesTheExactSavedQuoteButChangedWordsAreRejected()
    {
        var store = Store();
        var (identity, _) = Setup(store);
        var sourceWithLayout = store.RecordBusinessSource(
            Owner,
            identity.Version,
            "https://paper.example.invalid/layout",
            "Fictional layout",
            "Paper kits.\nFold with care.",
            false
        );
        var prepared = Prepare(store, identity, sourceWithLayout);
        store.Apply(Run(prepared));
        store.FinishBusinessDraft(
            prepared.Id,
            new(Answer(prepared.Call, quote: "Paper kits. Fold with care."), 125, "fictional-layout", new(100, 25))
        );
        var result = store.BusinessDraft(prepared.Id);
        Assert.Equal("completed", result.State);
        var profile = Items<CompanyBusinessProfile>(store, "businessProfiles").Single(p => p.Id == result.ProfileId);
        Assert.Equal(
            "Paper kits.\nFold with care.",
            profile.Fields.Single(f => f.Key == "business").Citations.Single().Quote
        );
        foreach (
            var wrong in new[]
            {
                "Paper boxes. Fold with care.",
                "paper kits. Fold with care.",
                "Paper kits! Fold with care.",
            }
        )
        {
            var next = Prepare(store, identity, sourceWithLayout);
            store.Apply(Run(next));
            store.FinishBusinessDraft(
                next.Id,
                new(Answer(next.Call, quote: wrong), 125, "fictional-wrong", new(100, 25))
            );
            Assert.Equal("invalid", store.BusinessDraft(next.Id).State);
        }
    }

    [Fact]
    public void CapturedOwnerGoalWinsBeforeCheckingModelClaimsButDoesNotAuthorizeOtherFields()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var prepared = Prepare(store, identity, source);
        var fields = CompanyStore
            .BusinessProfileFields.Select(key =>
                key == "goals"
                    ? new CompanyProfileField(
                        key,
                        "Model changed the owner's goal",
                        "sourced",
                        [new(source.Id, "Invented source for an owner goal")]
                    )
                : key == "business"
                    ? new CompanyProfileField(key, "Fictional paper kits", "sourced", [new(source.Id, SourceText)])
                : new CompanyProfileField(key, "Unknown", "unknown", [])
            )
            .ToArray();
        store.Apply(Run(prepared));
        store.FinishBusinessDraft(
            prepared.Id,
            new(CompanyJson.Encode(new { fields }), 125, "fictional-owner-goal", new(100, 25))
        );
        var result = store.BusinessDraft(prepared.Id);
        Assert.Equal("completed", result.State);
        var profile = Items<CompanyBusinessProfile>(store, "businessProfiles").Single(p => p.Id == result.ProfileId);
        var goal = profile.Fields.Single(f => f.Key == "goals");
        Assert.Equal(identity.Goal, goal.Value);
        Assert.Equal("owner", goal.Verification);
        Assert.Empty(goal.Citations);
        var forged = Prepare(store, identity, source);
        fields[0] = fields[0] with { Verification = "owner", Citations = [] };
        store.Apply(Run(forged));
        store.FinishBusinessDraft(
            forged.Id,
            new(CompanyJson.Encode(new { fields }), 125, "fictional-forged-owner", new(100, 25))
        );
        Assert.Equal("invalid", store.BusinessDraft(forged.Id).State);
    }
}
