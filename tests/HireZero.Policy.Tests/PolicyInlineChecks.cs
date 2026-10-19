using System.Text.Json;
using HireZero.Roles.Developer;

// Wiki draft policy checks.
public static class PolicyInlineChecks
{
    public static void Run()
    {
        var wikiContent = new WikiDraftContent(
            "acceptance",
            "Rule",
            "Synthetic acceptance",
            "Return a clear validation error.",
            "Preserve the agreed requirement.",
            "Synthetic development request",
            false,
            false,
            ""
        );
        var wikiDraft = new WikiDraft(
            "draft",
            "request",
            "Example",
            new("Example", "synthetic-example", true),
            wikiContent,
            null,
            [new("request", "private-evidence")],
            1,
            "draft",
            null,
            null,
            "now"
        );
        var wikiCases = new (WikiDraft Draft, bool Allowed)[]
        {
            (wikiDraft, true),
            (wikiDraft with { Content = wikiContent with { Conflict = true } }, false),
            (wikiDraft with { Content = wikiContent with { MissingInformation = true } }, false),
            (wikiDraft with { Destination = wikiDraft.Destination with { Synthetic = false } }, false),
            (wikiDraft with { Destination = wikiDraft.Destination with { WikiProjectId = "real-project" } }, false),
            (wikiDraft with { Content = wikiContent with { Body = "Contact synthetic@example.test" } }, false),
            (wikiDraft with { Content = wikiContent with { Kind = "Section" } }, false),
            (wikiDraft with { Content = wikiContent with { KnowledgeKey = "../../outside" } }, false),
            (wikiDraft with { Content = wikiContent with { Body = new string('x', 6001) } }, false),
            (
                wikiDraft with
                {
                    Before = new(
                        "entry",
                        "revision",
                        "acceptance",
                        "Rule",
                        "Title",
                        "Body",
                        1,
                        "stale",
                        "all",
                        "all",
                        true
                    ),
                },
                false
            ),
            (
                wikiDraft with
                {
                    Before = new(
                        "entry",
                        "revision",
                        "acceptance",
                        "Rule",
                        "Title",
                        "Body",
                        1,
                        "current",
                        "all",
                        "all",
                        false
                    ),
                },
                false
            ),
        };
        foreach (var test in wikiCases)
            if (WikiDraftPolicy.CanSubmit(test.Draft) != test.Allowed)
                throw new InvalidOperationException("Wiki submission accepted an unresolved or unscreened draft.");
        var wikiJson = JsonSerializer.Serialize(wikiContent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var suggestions = new (string Text, bool Allowed)[]
        {
            (wikiJson, true),
            ("null", false),
            ("[]", false),
            ("{}", false),
            (wikiJson.Replace("\"conflict\":false,", ""), false),
            (wikiJson.Replace("\"missingInformation\":false", "\"missingInformation\":\"no\""), false),
        };
        foreach (var test in suggestions)
            if ((WikiDraftPolicy.ParseSuggestion(test.Text) is not null) != test.Allowed)
                throw new InvalidOperationException("Wiki drafting accepted an incomplete model response.");
        Console.WriteLine($"PASS {wikiCases.Length + suggestions.Length} Wiki draft screening and uncertainty checks");
        foreach (
            var body in new[]
            {
                "Show a useful error. Then keep the form open!",
                "## Validation\nSecond line?",
                new string('x', 6000),
            }
        )
        {
            var summary = WikiDraftPolicy.PublicationSummary(
                wikiDraft with
                {
                    Content = wikiContent with { Body = body, ResolutionNote = "PRIVATE_MARKER" },
                }
            );
            if (
                summary.Length > 320
                || summary.Count(character => character is '.' or '!' or '?') != 1
                || summary.Contains("PRIVATE_MARKER")
                || summary.Contains("private-evidence")
                || summary.Contains('\n')
            )
                throw new InvalidOperationException(
                    "Wiki notices must be one bounded sentence from published fields only."
                );
        }
        Console.WriteLine("PASS 3 publication summary privacy and sentence checks");
    }
}
