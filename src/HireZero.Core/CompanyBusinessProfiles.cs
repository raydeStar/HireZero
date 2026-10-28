using System.Text.RegularExpressions;

namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private CompanyProfileField[] ValidateProfile(StoreConnection db, CompanyProfileInput input)
    {
        ConfirmedBusiness(db, input.BusinessVersion);
        if (
            input.Fields is null
            || input.Fields.Length != BusinessProfileFields.Length
            || input.Fields.Any(f => f is null)
            || !input.Fields.Select(f => f.Key).Order().SequenceEqual(BusinessProfileFields.Order())
        )
            throw new WorkspaceException(
                "Provide each business profile field exactly once; mark missing facts Unknown.",
                422
            );
        return BusinessProfileFields
            .Select(key =>
            {
                var field = input.Fields.Single(f => f.Key == key);
                var value = Text(field.Value, 600).Trim();
                if (value.Length == 0 || field.Verification is not ("sourced" or "hypothesis" or "owner" or "unknown"))
                    throw new WorkspaceException(
                        $"The {key} field needs text and a recognized verification status.",
                        422
                    );
                var citations = field.Citations ?? [];
                if (
                    citations.Length > 5
                    || citations.Any(c => c is null)
                    || citations.Select(c => c.SourceId).Distinct().Count() != citations.Length
                )
                    throw new WorkspaceException("Use up to five distinct sources per field.", 422);
                if (field.Verification is "sourced" or "hypothesis" && citations.Length == 0)
                    throw new WorkspaceException($"The {key} fact or hypothesis needs source evidence.", 422);
                if (field.Verification is "owner" or "unknown" && citations.Length != 0)
                    throw new WorkspaceException(
                        "Owner corrections and unknown facts have their own provenance, not source citations.",
                        422
                    );
                var checkedCitations = citations
                    .Select(citation =>
                    {
                        var source = Need<CompanyBusinessSource>(db, citation.SourceId);
                        var quote = Text(citation.Quote, 600).Trim();
                        if (quote.Length > 0 && !source.Text.Contains(quote, StringComparison.Ordinal))
                        {
                            // Page layout may wrap a sentence. Match only whitespace; never mend a model's words.
                            var pattern = string.Join(@"\s+", Regex.Split(quote, @"\s+").Select(Regex.Escape));
                            try
                            {
                                var match = Regex.Match(
                                    source.Text,
                                    pattern,
                                    RegexOptions.CultureInvariant,
                                    TimeSpan.FromMilliseconds(100)
                                );
                                if (match.Success && match.Length <= 600)
                                    quote = match.Value;
                            }
                            catch (RegexMatchTimeoutException)
                            {
                                // Leave it unmatched; uncertain quotations do not get the family seal.
                            }
                        }
                        if (
                            source.BusinessVersion != input.BusinessVersion
                            || quote.Length == 0
                            || !source.Text.Contains(quote, StringComparison.Ordinal)
                        )
                            throw new WorkspaceException(
                                $"The {key} citation must exactly quote a source from this confirmed business revision.",
                                422
                            );
                        return citation with { Quote = quote };
                    })
                    .ToArray();
                return field with { Value = value, Citations = checkedCitations };
            })
            .ToArray();
    }

    private object SaveBusinessProfile(StoreConnection db, CompanyCommand command, string? draftId = null)
    {
        var input = CompanyJson.Decode<CompanyProfileInput>(command.Body ?? "null");
        var fields = ValidateProfile(db, input);
        var previous = command.Id is null ? null : Need<CompanyBusinessProfile>(db, command.Id);
        Version(previous?.Version ?? 0, command.ExpectedVersion);
        if (previous is not null && (previous.State != "draft" || previous.BusinessVersion != input.BusinessVersion))
            throw new WorkspaceException("Create a new profile draft for this business revision.", 409);
        PreserveEarlierProfile(db, previous);
        var profile = new CompanyBusinessProfile(
            previous?.Id ?? Id(),
            input.BusinessVersion,
            fields,
            CompanyJson.Digest(new CompanyProfileInput(input.BusinessVersion, fields)),
            "draft",
            (previous?.Version ?? 0) + 1,
            Clock()
        );
        Save(db, profile, profile.Id, BusinessIdentityId, state: profile.State, version: profile.Version);
        RecordProfileRevision(db, profile, draftId is null ? "owner" : "model", draftId);
        Event(db, "business.profile-drafted", profile.Id, null, "A sourced business brief is ready for owner review.");
        return profile;
    }

    private object ReviewBusinessProfile(StoreConnection db, CompanyCommand command)
    {
        var profile = Need<CompanyBusinessProfile>(db, command.Id);
        Version(profile.Version, command.ExpectedVersion);
        if (profile.Digest != command.Digest)
            throw new WorkspaceException("Review the exact business brief shown.", 409);
        PreserveEarlierProfile(db, profile);
        if (command.Decision == "approve" && profile.State == "draft")
        {
            ValidateProfile(db, new(profile.BusinessVersion, profile.Fields));
            if (All<CompanyBusinessProfile>(db).Any(p => p.State == "approved"))
                throw new WorkspaceException(
                    "Review and explicitly revoke the previous approved brief before approving its replacement.",
                    409
                );
            var body = BusinessKnowledgeBody(profile.Fields, profile.Id);
            var knowledge = (CompanyKnowledge)Apply(db, new(command.RequestId, "knowledge.save", Body: body));
            knowledge = (CompanyKnowledge)Apply(
                db,
                new(
                    command.RequestId,
                    "knowledge.review",
                    knowledge.Id,
                    knowledge.Version,
                    Digest: knowledge.Digest,
                    Decision: "approve"
                )
            );
            profile = profile with { State = "approved", KnowledgeId = knowledge.Id };
        }
        else if (command.Decision == "reject" && profile.State == "draft")
            profile = profile with { State = "rejected" };
        else if (command.Decision == "revoke" && profile.State == "approved" && profile.KnowledgeId is { } knowledgeId)
        {
            var knowledge = Need<CompanyKnowledge>(db, knowledgeId);
            if (knowledge.State == "approved")
                Apply(
                    db,
                    new(
                        command.RequestId,
                        "knowledge.review",
                        knowledge.Id,
                        knowledge.Version,
                        Digest: knowledge.Digest,
                        Decision: "revoke"
                    )
                );
            profile = profile with { State = "revoked" };
        }
        else
            throw new WorkspaceException("Approve or reject a draft, or revoke an approved brief.", 409);
        profile = profile with { Version = profile.Version + 1, UpdatedAt = Clock() };
        Save(db, profile, profile.Id, BusinessIdentityId, state: profile.State, version: profile.Version);
        RecordProfileRevision(db, profile, "owner");
        Event(db, "business.profile-" + profile.State, profile.Id, null, "Business brief " + profile.State + ".");
        return profile;
    }

    // The exact legacy representation is also used to recognize a redundant copy in a new task packet.
    private static string BusinessKnowledgeBody(CompanyProfileField[] fields, string profileId)
    {
        string Field(string key)
        {
            var field = fields.Single(f => f.Key == key);
            return field.Value + " [" + field.Verification + "]";
        }
        return CompanyJson.Encode(
            new
            {
                product = Field("business") + "\nServices: " + Field("services"),
                customers = Field("customers"),
                stage = "Owner goal: " + Field("goals"),
                facts = "Voice: "
                    + Field("voice")
                    + "\nMarket: "
                    + Field("market")
                    + "\nCompetitors: "
                    + Field("competitors")
                    + "\nOpportunities (not promised outcomes): "
                    + Field("opportunities"),
                boundaries = Field("boundaries"),
                profileId,
            }
        );
    }
}
