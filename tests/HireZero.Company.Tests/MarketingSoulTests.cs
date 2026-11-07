using HireZero.Core;
using HireZero.Roles.Marketing;

// The Marketing employee's personality reaches the owner-facing prompts only (soul.md, 2026-11-06).
public sealed class MarketingSoulTests
{
    [Fact]
    public void ConversationAndMorningRoundsCarryTheSoulButDraftsDoNot()
    {
        var module = new MarketingModule();
        Assert.Contains("You are a colleague, not a tool.", MarketingSoul.Text);
        Assert.EndsWith(MarketingSoul.Text, module.InstructionsFor(PlatformSkills.Chat));
        Assert.EndsWith(MarketingSoul.Text, module.InstructionsFor(MarketingShift.Skill));
        Assert.DoesNotContain(MarketingSoul.Text, module.InstructionsFor(MarketingModule.GoalDraft));
        Assert.DoesNotContain(MarketingSoul.Text, module.InstructionsFor(MarketingModule.StarterPlan));
    }
}
