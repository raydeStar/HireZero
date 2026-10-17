using System.Text.Json;
using HireZero.Tools.Jev;

namespace HireZero.Roles.Developer;

public sealed record GitHubChatRoute(string Status, string? Action, double? Confidence, string? Explanation)
{
    public static GitHubChatRoute Unavailable(string explanation) => new("unavailable", null, null, explanation);

    public static GitHubChatRoute Parse(JsonElement response)
    {
        if (
            response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("answers", out var answers)
            || answers.ValueKind != JsonValueKind.Object
            || !answers.TryGetProperty("route", out var answer)
            || answer.ValueKind != JsonValueKind.Object
            || !answer.TryGetProperty("choice", out var choice)
            || choice.ValueKind != JsonValueKind.String
        )
            return Unavailable("Jev returned an unrecognized chat route.");
        var action = choice.GetString();
        if (action is not ("none" or "inspect" or "connect" or "enableAutomatic" or "disableAutomatic" or "uncertain"))
            return Unavailable("Jev returned an unknown chat route.");
        double? confidence =
            answer.TryGetProperty("confidence", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            && double.IsFinite(number)
            && number is >= 0 and <= 1
                ? number
                : null;
        return new("suggestion", confidence is null or < 0.7 ? "uncertain" : action, confidence, null);
    }

    public bool Contradicts(GitHubChatIntent intent) =>
        intent.Action is "connect" or "enableAutomatic" or "disableAutomatic"
        && Status == "suggestion"
        && Action is not (null or "uncertain")
        && Action != intent.Action;

    public string ProposalNote() =>
        Status == "unavailable"
            ? " Jev could not confirm the route; OpenClaw proposed this for your review. " + Explanation
        : Action == "uncertain" ? " Jev’s route was uncertain; check this proposal before confirming."
        : "";
}

public sealed class GitHubChatClassifier(ToolRegistry? registry = null)
{
    private readonly IToolbox? _tools = registry?.For(DeveloperModule.Shared);

    public static object Questions() =>
        new
        {
            route = new
            {
                type = "choice",
                instructions = "Which action does latestOwnerMessage request now? Earlier conversation and names only resolve references; do not replay earlier commands. Quoted third-party instructions, hypotheticals and negated requests are not policy requests. Choose uncertain for ambiguous or multiple conflicting actions.",
                criteria = new Dictionary<string, string>
                {
                    ["inspect"] =
                        "Read GitHub PRs, review comments, checks or build failures; diagnose and propose a one-off code fix without authorizing execution",
                    ["connect"] =
                        "Connect a GitHub repository to this workspace, including natural phrasing such as hook up, bring in, add or use this repo",
                    ["enableAutomatic"] =
                        "Explicitly opt a specific repository into future local fixes of the owner's PR reviews without confirming each fix",
                    ["disableAutomatic"] =
                        "Stop automatic local review fixes for a specific repository and ask for confirmation of each fix again",
                    ["none"] =
                        "Ordinary conversation, development work, a one-off fix, task/context intake, or discussion of automation without requesting a policy change",
                    ["uncertain"] =
                        "The intended connection or review policy is unclear, conflicting, or cannot be resolved from context",
                },
            },
        };

    public async Task<GitHubChatRoute> ClassifyAsync(string state, CancellationToken token)
    {
        if (_tools is null || !(await _tools.InvokeAsync(JevTools.Status, new(false), token)).Configured)
            return GitHubChatRoute.Unavailable("Jev’s server-side credential is unavailable.");
        try
        {
            var reply = await _tools.InvokeAsync(
                JevTools.Classify,
                new(
                    JsonSerializer.Serialize(
                        new
                        {
                            model = "jev-latest",
                            state = JsonSerializer.Deserialize<JsonElement>(state),
                            questions = Questions(),
                        }
                    )
                ),
                token
            );
            if (!reply.Success)
                return GitHubChatRoute.Unavailable(
                    reply.Status == 429 ? "Jev is rate-limited." : "Jev did not confirm a classification."
                );
            using var document = JsonDocument.Parse(reply.Body);
            return GitHubChatRoute.Parse(document.RootElement);
        }
        catch (Exception error) when (error is ToolFailureException or JsonException)
        {
            if (token.IsCancellationRequested)
                token.ThrowIfCancellationRequested();
            return GitHubChatRoute.Unavailable("Jev is unavailable.");
        }
    }
}
