using System.Text.Json;
using HireZero.Core;

public sealed partial class BusinessDraftTests
{
    [Fact]
    public void CompactPacketsPreserveEvidenceMeaningfulUncertaintyAndTheFullSavedBrief()
    {
        var store = Store();
        var (identity, source) = Setup(store);
        var fields = CompanyStore
            .BusinessProfileFields.Select(key =>
                key switch
                {
                    "business" => new CompanyProfileField(
                        key,
                        "Fictional paper kits",
                        "sourced",
                        [new(source.Id, SourceText)]
                    ),
                    "services" => new CompanyProfileField(key, "Unknown", "owner", []),
                    "boundaries" => new CompanyProfileField(key, "Delivery timing is not established.", "unknown", []),
                    _ => new CompanyProfileField(key, "Unknown", "unknown", []),
                }
            )
            .ToArray();
        var profile = Apply<CompanyBusinessProfile>(
            store,
            Cmd("business.profile.save") with
            {
                Body = CompanyJson.Encode(new CompanyProfileInput(identity.Version, fields)),
            }
        );
        profile = Apply<CompanyBusinessProfile>(
            store,
            Cmd("business.profile.review") with
            {
                Id = profile.Id,
                ExpectedVersion = profile.Version,
                Digest = profile.Digest,
                Decision = "approve",
            }
        );
        var savedProfile = CompanyJson.Encode(profile);
        var knowledge = Items<CompanyKnowledge>(store, "knowledge").Single(k => k.Id == profile.KnowledgeId);
        var work = MeasuredQueueChecks.Seed(store);
        using var result = JsonDocument.Parse(
            store.Apply(
                Cmd("conversation.prepare") with
                {
                    Id = knowledge.Id,
                    ExpectedVersion = knowledge.Version,
                    Digest = knowledge.Digest,
                    EmployeeId = work.EmployeeId,
                    ThreadId = work.ThreadId,
                    Scope = "employee",
                    Body = "Explain the supported service and its limitations.",
                    CreditAllowance = 1,
                }
            )
        );
        var attempt = result.RootElement.GetProperty("attempt").Deserialize<CompanyAttempt>(CompanyJson.Options)!;
        var call = store.TextCall(attempt.Id);
        var brief = call.Context.BusinessBrief!;
        Assert.Empty(call.Context.CompanyKnowledge);
        Assert.Equal(3, brief.Fields.Length);
        Assert.Equal(6, brief.UnfilledFields!.Length);
        Assert.Equal("owner", brief.Fields.Single(f => f.Key == "services").Verification);
        Assert.Equal("Delivery timing is not established.", brief.Fields.Single(f => f.Key == "boundaries").Value);
        Assert.Equal(SourceText, Assert.Single(brief.Fields.Single(f => f.Key == "business").Citations).Quote);
        Assert.Equal(source.Url, Assert.Single(brief.Sources).Url);
        Assert.Equal(profile.Digest, brief.Digest);
        Assert.Contains("not facts known to be absent", call.Instructions);
        Assert.Equal(
            savedProfile,
            CompanyJson.Encode(Items<CompanyBusinessProfile>(store, "businessProfiles").Single())
        );
        Assert.Equal(knowledge, Items<CompanyKnowledge>(store, "knowledge").Single(k => k.Id == knowledge.Id));
        Assert.Equal(CompanyJson.Encode(call.Context), CompanyJson.Encode(Store().TextCall(attempt.Id).Context));
        Assert.Throws<WorkspaceException>(() => Store("co-packet-neighbor").TextCall(attempt.Id));

        // Older captured packets keep their bytes; only newly captured packets are compacted.
        var older = CompanyJson.Encode(brief with { Fields = profile.Fields, UnfilledFields = null });
        Assert.DoesNotContain("unfilledFields", older);
        Assert.Equal(older, CompanyJson.Encode(CompanyJson.Decode<CompanyBusinessProvenance>(older)));
    }
}
