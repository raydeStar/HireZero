namespace HireZero.Roles.Marketing;

// The goal ladder (docs/adr/0170-goal-ladder-and-weekly-numbers.md): from the owner's broad goal ("more money") to the
// one number marketing grows, the steps customers take to reach it, and the step to work on first. The owner approves
// it into the department context, where every later request sees it; the weekly numbers it names go on the scorecard.
public static class MarketingLadder
{
    public const string Skill = "marketing-goal-ladder";

    // Optional department fields, so existing context stays valid until the owner approves a ladder.
    public static readonly OnboardingField[] Fields =
    [
        new("goal", "The one number marketing should grow each week, and how growing it gets the owner what they want")
        {
            Optional = true,
        },
        new("start", "The first thing a new customer does, and where (for example: start free on the website)")
        {
            Optional = true,
        },
        new(
            "steps",
            "The steps from first hearing about the business to that number, in order, one per line as 'Step: the number that shows it each week'"
        )
        {
            Optional = true,
        },
        new(
            "focus",
            "The step too few people reach from the step before it and why, what to work on first, and what to leave alone for now"
        )
        {
            Optional = true,
        },
    ];

    public const string Instructions =
        MarketingText.Instructions
        + " Work out this business's goal ladder for an owner who knows nothing about marketing. Owners rarely state more than a broad wish such as more money or more customers; your job is to infer what marketing should grow to get there, the steps people take on the way, and where to start. "
        + "Use the approved business brief, department context, the owner's goal and any supplied website excerpts or results. Write in plain words: no marketing jargon such as funnel, conversion, KPI, north star, CTA or persona. "
        + "Return Markdown with exactly these five headings, in this order and with nothing before the first: '## Goal', '## First step', '## Steps', '## Focus', '## Notes for you'. "
        + "Goal: one number the owner can count every week that marketing should grow, such as new paying customers a week, and one or two sentences on how growing it leads to what the owner wants. Use the owner's own number if their goal names one. "
        + "First step: the one thing a new customer does first, and where, using the supplied customer route; if none is supplied, propose one and say it is a proposal. "
        + "Steps: three to five steps from first hearing about the business to the goal's number, in order, each on its own line as '- Step name: the number that shows it each week'. Keep step names to two to four plain words. Choose numbers the owner can see for free; where one needs a tool such as website analytics, name it briefly in parentheses. The last step's number is the goal's number. "
        + "Focus: start with the name of the step too few people reach from the step before it, the most likely biggest loss right now, and say why, using only the supplied evidence, such as a homepage that never says who it is for or no clear way to start. Then give one to three first things to work on at that step, each with the reason. Call it a best guess until the weekly numbers confirm it, and say which numbers would. End the section with a line that starts 'Leave alone for now:' and names work that would not help yet. "
        + "Notes for you: at most three short bullets on what you assumed or what the owner should check. "
        + "Do not invent current numbers, targets, results, benchmarks, customers or company facts; a supplied unknown stays unknown. Keep it to about 400 words. Return a private draft for the owner's approval; do not perform actions.";
}
