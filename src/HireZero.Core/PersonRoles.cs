namespace HireZero.Core;

// The kinds of people the owner invites, and how much of the agent's effort each gets.
//   boss    — a sanitized "what the owner has been doing" (company projects, activity, PRs); daily synopsis on request.
//   trusted — usually a yes: the agent answers and prepares, but nothing runs until the owner confirms.
//   visitor — may ask, but no agent effort is spent until the owner allows it for that request.
public static class PersonRoles
{
    public const string Boss = "boss",
        Trusted = "trusted",
        Visitor = "visitor";
    public static readonly string[] All = [Boss, Trusted, Visitor];

    // "manager" was the earlier name for the boss role.
    public static string Normalize(string? role) =>
        role?.Trim().ToLowerInvariant() switch
        {
            "boss" or "manager" => Boss,
            "trusted" => Trusted,
            "visitor" => Visitor,
            var other => other ?? Visitor,
        };

    public static bool Valid(string? role) =>
        role?.Trim().ToLowerInvariant() is "boss" or "manager" or "trusted" or "visitor";

    public static Audience AudienceFor(string role) =>
        Normalize(role) switch
        {
            Boss => Audience.Boss,
            _ => Audience.Visitor,
        };
}
