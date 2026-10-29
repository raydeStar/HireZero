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
        Assert.Contains("Describe the business once across the posts", instructions);
        Assert.Contains("The brief's company sentence is a source, not copy", instructions);
        Assert.Contains("without explaining the tracking to readers", instructions);
    }

    // The offer names its post's problem and how the business addresses it, without the brief's wording, and a worked
    // example adds only new steps (docs/history/2026-10-29-weekly-plan-rubric-iteration.md, cycles 11 and 13).
    [Fact]
    public void TheOfferSaysHowTheBusinessHelpsInItsOwnWordsAndNoPostRepeatsAnothersSteps()
    {
        var instructions = MarketingText.StarterPlanInstructions;
        Assert.Contains("say which part of the business's supplied work removes it and how", instructions);
        Assert.Contains("reuse no run of more than four of its words", instructions);
        Assert.Contains("name an earlier tip in one short clause instead of repeating its steps", instructions);
        Assert.Contains("no numbered step repeats one from another post", instructions);
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
}
