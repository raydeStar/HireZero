namespace HireZero.Roles.Marketing;

public static class CompanyMarketing
{
    public static CompanyRole Definition => new("marketing","Marketing",MarketingRole.Instructions,
        ["chat","marketing-draft","meeting-turn"],["purpose","audience","positioning","voice","claims","constraints"])
        {ValidateInput=(skill,input)=>skill=="marketing-draft"?new MarketingBriefPolicy().Validate(input):input,
         InstructionsForSkill=skill=>skill=="marketing-draft"?MarketingRole.DraftInstructions:MarketingRole.Instructions};
}
