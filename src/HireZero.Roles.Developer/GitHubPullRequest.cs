using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Roles.Developer;

public sealed record PullRequestRef(string Repository, int Number);

public sealed record PullRequestCheck(string Name, string State, string? Evidence)
{
    public string? JobUrl { get; init; }
    public string? EventId { get; init; }
    public string? UpdatedAt { get; init; }
    public long? RunId { get; init; }
    public int? RunAttempt { get; init; }
    public string? EvidenceRevision { get; init; }
}

public sealed record PullRequestApproval(string Login, string Type, string State, string HeadSha);

public sealed record PullRequestFile(string Path, string Status, string Patch, string SourceExcerpt = "");

public sealed record PullRequestEvidenceRequest(string Kind, string Value, int StartLine = 1);

public sealed record PullRequestEvidence(
    string Repository,
    long RepositoryId,
    int Number,
    string Title,
    string HeadSha,
    string Author,
    bool Open,
    bool Draft,
    string HeadRepository,
    long HeadRepositoryId,
    string HeadBranch,
    string DefaultBranch,
    string MergeableState,
    bool Complete,
    PullRequestCheck[] Checks,
    ReviewThread[] Threads,
    PullRequestApproval[] Reviews,
    PullRequestFile[] Files,
    string[] Discussion,
    string[] Limitations,
    string[] Labels
)
{
    public PullRequestFile[] ContextFiles { get; init; } = [];
    public string Snapshot =>
        PullRequestPolicy.Hash(
            JsonSerializer.Serialize(
                new
                {
                    RepositoryId,
                    Number,
                    HeadSha,
                    Author,
                    Open,
                    Draft,
                    HeadRepositoryId,
                    HeadBranch,
                    Complete,
                    checks = Checks.Select(c => new
                    {
                        c.Name,
                        c.State,
                        c.EventId,
                        c.UpdatedAt,
                        c.JobUrl,
                        c.RunId,
                        c.RunAttempt,
                        EvidenceRevision = c.EvidenceRevision ?? PullRequestPolicy.Hash(c.Evidence ?? ""),
                    }),
                    Threads,
                    Reviews,
                    files = Files.Select(f => new
                    {
                        f.Path,
                        f.Status,
                        f.Patch,
                    }),
                    Discussion,
                }
            )
        );
}

public sealed record MergeReadiness(string HeadSha, string Snapshot, bool Ready, string[] Blockers);

public sealed record FixStrategy(
    string Summary,
    string[] Steps,
    string[] Tests,
    string[] BusinessRuleChanges,
    string[] Questions,
    bool Frontend,
    string Priority,
    bool Ambiguous,
    bool CrossSystem,
    string EscalationReason,
    string Trigger = "",
    string[]? Evidence = null,
    string RegressionTest = ""
)
{
    public PullRequestEvidenceRequest[] EvidenceRequests { get; init; } = [];
}

public sealed record PullRequestWorkflow(
    string Id,
    string RequestId,
    long AccountId,
    string MessageKey,
    string Repository,
    long RepositoryGitHubId,
    int Number,
    string HeadSha,
    string EvidenceSnapshot,
    FixStrategy Strategy,
    string PlanningModel,
    string State = "proposed",
    bool RulesApproved = false,
    string? RepositoryId = null,
    string? FixId = null,
    string? CommitSha = null,
    string? Error = null,
    string? ReviewedDiff = null,
    string[]? RuleConcerns = null,
    string? RunState = null,
    string? CodingRunId = null,
    string[]? OwnerNotes = null,
    string[]? SensitiveFindings = null,
    string? SensitivePending = null,
    string? SensitiveAck = null,
    string? ProofAck = null,
    int? Round = null
)
{
    public long ConsentGeneration { get; init; }

    // Read-only presentation; the durable approval row remains the sole source of authority.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public bool? ApprovalAvailable { get; init; }
    public string ApprovalSnapshot =>
        PullRequestPolicy.Hash(
            JsonSerializer.Serialize(
                new
                {
                    Id,
                    RequestId,
                    AccountId,
                    MessageKey,
                    RepositoryGitHubId,
                    Number,
                    HeadSha,
                    EvidenceSnapshot,
                    Strategy,
                    RulesApproved,
                    OwnerNotes,
                    ConsentGeneration,
                }
            )
        );
    public string PublicationSnapshot => PullRequestPolicy.Hash(ApprovalSnapshot + ":" + ReviewedDiff);
}

public static partial class PullRequestPolicy
{
    public static string BuilderModel => ModelRoutes.Native(ModelRoutes.Route(DeveloperRoutes.Build));

    [GeneratedRegex(
        @"https://github\.com/([A-Za-z0-9-]+/[A-Za-z0-9_.-]+)/pull/([0-9]+)(?:\b|/)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(
        @"(?i)(-----BEGIN .*PRIVATE KEY|(?:password|passwd|api[_-]?key|client[_-]?secret|access[_-]?token|refresh[_-]?token|authorization|connectionstring|cookie)[""']?\s*[=:]|https?://[^\s]+[?&](?:token|code|key|sig)=|https?://[^\s/:]+:[^\s/@]+@)"
    )]
    private static partial Regex SensitiveLinePattern();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)")]
    private static partial Regex PrivateKeyBlockPattern();

    public static PullRequestRef[] References(string text) =>
        ReferencePattern()
            .Matches(text)
            .Select(match =>
                int.TryParse(match.Groups[2].Value, out var number)
                && number > 0
                && GitHubReviewPolicy.ValidRepository(match.Groups[1].Value)
                    ? new PullRequestRef(match.Groups[1].Value, number)
                    : null
            )
            .OfType<PullRequestRef>()
            .Distinct()
            .Take(4)
            .ToArray();

    public static string Safe(string? text, int limit = 2000)
    {
        // Redact before bounding: truncation must never cut a credential into an undetectable prefix.
        var safe = string.Join(
            '\n',
            PrivateKeyBlockPattern()
                .Replace(text ?? "", "[private key material withheld]")
                .Split('\n')
                .Select(line =>
                    !GitHubReviewPolicy.SafeText(line) || SensitiveLinePattern().IsMatch(line)
                        ? "[sensitive line withheld]"
                        : line
                )
        );
        return safe.Length <= limit ? safe : safe[..limit] + "\n[excerpt truncated]";
    }

    // Lines in a changed file that look like secrets. Credential: a real-looking token or private key, which never publishes.
    // Otherwise the line only has a secret-shaped field (password:, a user:password@ URL…), often a test placeholder the owner can confirm.
    public sealed record SensitiveLine(string Path, int Line, bool Credential, string TextHash);

    public static SensitiveLine[] SensitiveLines(string path, string content) =>
        [
            .. content
                .Split('\n')
                .Select(
                    (line, index) =>
                        !GitHubReviewPolicy.SafeText(line)
                        || line.Contains("PRIVATE KEY-----", StringComparison.Ordinal)
                            ? new SensitiveLine(path, index + 1, true, Hash(line))
                        : SensitiveLinePattern().IsMatch(line) ? new SensitiveLine(path, index + 1, false, Hash(line))
                        : null
                )
                .OfType<SensitiveLine>(),
        ];

    public static string SensitiveHash(IEnumerable<SensitiveLine> lines) =>
        Hash(string.Join("\n", lines.Select(line => line.Path + ":" + line.Line + ":" + line.TextHash)));

    // The new-file line numbers a zero-context unified diff adds ("@@ -a,b +c,d @@" hunks).
    public static HashSet<int> AddedLines(string diff)
    {
        var added = new HashSet<int>();
        var next = 0;
        foreach (var line in diff.Split('\n'))
        {
            var hunk = System.Text.RegularExpressions.Regex.Match(
                line,
                @"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromMilliseconds(50)
            );
            if (hunk.Success)
            {
                next = int.Parse(hunk.Groups[1].Value);
                continue;
            }
            if (
                next == 0
                || line.StartsWith("+++", StringComparison.Ordinal)
                || line.StartsWith("---", StringComparison.Ordinal)
            )
                continue;
            if (line.StartsWith('+'))
                added.Add(next++);
            else if (!line.StartsWith('-') && !line.StartsWith('\\'))
                next++;
        }
        return added;
    }

    public static string Describe(SensitiveLine line) => line.Path + " line " + line.Line;

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string FailureExcerpt(string log, int limit = 6500)
    {
        var safe = Safe(log, 1_000_000);
        if (safe.Length <= limit)
            return safe;
        var lines = safe.Split('\n');
        var selected = new SortedSet<int>();
        for (var i = 0; i < Math.Min(lines.Length, 8); i++)
            selected.Add(i);
        var matches = 0;
        for (var i = 0; i < lines.Length && matches < 5; i++)
            if (
                lines[i].Contains("##[error]", StringComparison.OrdinalIgnoreCase)
                || lines[i].Contains("npm ERR", StringComparison.OrdinalIgnoreCase)
                || lines[i].Contains("error TS", StringComparison.OrdinalIgnoreCase)
                || lines[i].Contains("vulnerabilit", StringComparison.OrdinalIgnoreCase)
            )
            {
                matches++;
                for (var j = Math.Max(0, i - 4); j <= Math.Min(lines.Length - 1, i + 6); j++)
                    selected.Add(j);
            }
        var focused = Safe(string.Join('\n', selected.Select(i => lines[i])), limit - 1500);
        return Safe(
            "Failure context (bounded):\n"
                + focused
                + "\nFinal output (bounded):\n"
                + safe[Math.Max(0, safe.Length - 1200)..],
            limit
        );
    }

    public static bool Sha(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    public static string Model(FixStrategy triage) =>
        ModelRoutes.Route(
            triage.Ambiguous || triage.CrossSystem ? DeveloperRoutes.Escalation : DeveloperRoutes.Validation
        );

    public static FixStrategy? ParseStrategy(string json)
    {
        if (json.Length > 8000)
            return null;
        try
        {
            var strategy = JsonSerializer.Deserialize<FixStrategy>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            if (
                strategy is null
                || strategy.Summary is not { Length: > 0 and <= 1400 }
                || strategy.Steps is not { Length: > 0 and <= 8 }
                || strategy.Tests is not { Length: > 0 and <= 8 }
                || strategy.BusinessRuleChanges is not { Length: <= 5 }
                || strategy.Questions is not { Length: <= 5 }
                || strategy.Priority is not ("P1" or "P2" or "P3")
                || strategy.Trigger is not { Length: > 0 and <= 600 }
                || strategy.Evidence is not { Length: > 0 and <= 8 }
                || strategy.RegressionTest is not { Length: > 0 and <= 600 }
                || strategy.Evidence.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 600)
                || strategy.EscalationReason is null
                || strategy.EscalationReason.Length > 400
                || strategy.EvidenceRequests is not { Length: <= 4 }
                || strategy.EvidenceRequests.Any(request =>
                    request.Kind is not ("file" or "log" or "advisory")
                    || request.Value is not { Length: > 0 and <= 200 }
                    || request.StartLine is < 1 or > 100000
                )
                || strategy
                    .Steps.Concat(strategy.Tests)
                    .Concat(strategy.BusinessRuleChanges)
                    .Concat(strategy.Questions)
                    .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 600)
                || Safe(json, 10000) != json
            )
                return null;
            return strategy;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static MergeReadiness Merge(PullRequestEvidence pr, GitHubIdentity account)
    {
        var blockers = new List<string>();
        if (!pr.Author.Equals(account.Login, StringComparison.OrdinalIgnoreCase))
            blockers.Add("This is not your PR.");
        if (!pr.Open)
            blockers.Add("The PR is not open.");
        if (pr.Draft)
            blockers.Add("The PR is still a draft.");
        if (!pr.Complete)
            blockers.Add("GitHub evidence is incomplete; refresh or inspect GitHub.");
        if (pr.MergeableState != "clean")
            blockers.Add("GitHub does not report a clean, mergeable revision.");
        if (pr.Checks.Length == 0 || pr.Checks.Any(check => check.State != "success"))
            blockers.Add("Every check and status must be green on this revision.");
        if (pr.Threads.Any(thread => !thread.Resolved))
            blockers.Add("Code review threads still need to be addressed or resolved.");
        if (pr.Reviews.Any(review => review.State == "CHANGES_REQUESTED"))
            blockers.Add("A reviewer is still requesting changes.");
        // The owner's own confirmation is the approval; repository branch protection (reported as a non-clean
        // mergeable state) still blocks merges that require another reviewer.
        return new(pr.HeadSha, pr.Snapshot, blockers.Count == 0, [.. blockers]);
    }
}
