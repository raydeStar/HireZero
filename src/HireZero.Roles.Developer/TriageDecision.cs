using System.Text.Json;

namespace HireZero.Roles.Developer;

public sealed record TriageView(string Status, string? Route, string? CandidateRequestId,
    double? Confidence, string? Explanation);

public static class TriageDecision
{
    public static TriageView Parse(JsonElement response, string[] candidateIds)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("answers", out var answers) ||
            answers.ValueKind != JsonValueKind.Object || !answers.TryGetProperty("route", out var routeAnswer) ||
            routeAnswer.ValueKind != JsonValueKind.Object ||
            !routeAnswer.TryGetProperty("choice", out var choice) || choice.ValueKind != JsonValueKind.String)
            return Unavailable("Jev returned an unrecognized decision.");
        var route = choice.GetString();
        if (route is not ("existing" or "new" or "context" or "noise" or "uncertain"))
            return Unavailable("Jev returned an unknown route.");
        var confidence = Confidence(routeAnswer);
        string? candidate = null;
        if (route == "existing" && answers.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.Object &&
            match.TryGetProperty("choice", out var matchChoice) && matchChoice.ValueKind == JsonValueKind.String && Confidence(match) is >= 0.7)
            candidate = candidateIds.FirstOrDefault(id => id == matchChoice.GetString());
        if (confidence is null or < 0.7 || route == "existing" && candidate is null) route = "uncertain";
        return new("suggestion", route, route == "existing" ? candidate : null, confidence,
            route == "uncertain" ? "Confidence or task match is unresolved. Review before changing work." :
            "Review this Jev suggestion; classification does not create, delete, or execute work.");
    }

    private static double? Confidence(JsonElement answer) =>
        answer.TryGetProperty("confidence", out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var probability) && double.IsFinite(probability) && probability is >= 0 and <= 1 ? probability : null;

    public static TriageView Unavailable(string explanation) => new("unavailable", null, null, null, explanation);
}
