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
}
