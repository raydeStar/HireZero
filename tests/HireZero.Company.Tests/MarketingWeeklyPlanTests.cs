using HireZero.Roles.Marketing;

// A weekly plan's routine and tally stay as precise as its posts allow
// (docs/history/2026-10-29-weekly-plan-rubric-iteration.md).
public sealed class MarketingWeeklyPlanTests
{
    [Fact]
    public void TheWeeklyRoutineCountsRepliesAndTheTallyClaimsOnlyTheAttributionItsPostsRequest()
    {
        var instructions = MarketingText.StarterPlanInstructions;
        Assert.DoesNotContain("batch time estimate", instructions);
        Assert.Contains("replying within a day after each post to the comments and enquiries it invites", instructions);
        Assert.Contains("for the channel as a whole, as a minimum", instructions);
        Assert.Contains("an enquiry invitation in one or two posts is enough", instructions);
        Assert.Contains("never claim per-post precision", instructions);
        Assert.Contains("time estimate's parts add up to its total", instructions);
        Assert.Contains("with no disclaimer about results", instructions);
        Assert.DoesNotContain("Describe the business once across the posts", instructions);
        Assert.Contains("The brief's company sentence is a source, not copy", instructions);
        Assert.Contains("without explaining the tracking to readers", instructions);
        Assert.Contains("Phrase that aside naturally in the business's voice", instructions);
        Assert.Contains("when in-person visits are a supplied route", instructions);
    }

    // Each post serves a reader arriving on their own; the butler does not demand yesterday's password.
    [Fact]
    public void EachPostStandsAloneAndCanConnectItsOwnUsefulTakeawayToTheBusiness()
    {
        var instructions = MarketingText.StarterPlanInstructions;
        Assert.Contains("say which part of the business's supplied work removes it and how", instructions);
        Assert.Contains("reuse no run of more than four of its words", instructions);
        Assert.Contains("Every post must stand on its own for a reader who sees no other post", instructions);
        Assert.Contains("Do not refer to earlier days or posts", instructions);
        Assert.Contains("limited repeated context is appropriate", instructions);
        Assert.Contains("A brief business connection may appear in more than one post", instructions);
        Assert.Contains("when the owner's goal calls for it", instructions);
        Assert.DoesNotContain("name an earlier tip in one short clause", instructions);
        Assert.DoesNotContain("no numbered step repeats one from another post", instructions);
    }

    // Responses that name the channel are a minimum and the week's total only an upper bound; one grammatical person and
    // short paragraphs suit the channel; each post carries enough to act on (cycle 15).
    [Fact]
    public void TheTallyBoundsOffPostResponsesBothWaysAndPostsStayInOneVoice()
    {
        var instructions = MarketingText.StarterPlanInstructions;
        Assert.Contains("the week's total off-post responses are only an upper bound", instructions);
        Assert.Contains("keeping one person throughout", instructions);
        Assert.Contains("short paragraphs of one to three sentences", instructions);
        Assert.Contains("a one- or two-sentence tip is too thin", instructions);
    }

    [Fact]
    public void DirectAssetsDevelopEveryRequestedTopicAndMeetExplicitLengthWithoutInventingCompanyFacts()
    {
        var instructions = MarketingText.GoalDraftInstructions;
        Assert.Contains("Develop every requested topic or section with its own clear explanation", instructions);
        Assert.Contains("minimum and maximum word counts in the requested asset itself", instructions);
        Assert.Contains("excluding internal review notes", instructions);
        Assert.Contains("expand underdeveloped sections or trim repetition", instructions);
        Assert.Contains("Approximate length requests allow modest variation", instructions);
        Assert.Contains("not invented facts, repeated caveats or filler", instructions);
        Assert.Contains(
            "Ground company-specific claims and commitments strictly in supplied approved context",
            instructions
        );
    }
}
