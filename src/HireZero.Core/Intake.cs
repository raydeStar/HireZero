using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// An email, chat message or notification from any source (Outlook, Teams, Gmail, GitHub...).
public sealed record InboundMessage(
    string Source,
    string ExternalId,
    string? ThreadKey,
    string From,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAt
);

public enum IntakeVerdict
{
    Ignore,
    Review,
    Act,
}

// Tier 1: cheap deterministic rules. Return null for "no opinion"; a Verdict of null still lets a rule set priority.
public sealed record RuleOutcome(IntakeVerdict? Verdict, string? Priority, string Reason);

public interface IIntakeRule
{
    RuleOutcome? Evaluate(InboundMessage message);
}

// Tier 2: a classifier (Jev), called only when no rule decided.
public interface IIntakeClassifier
{
    Task<(IntakeVerdict Verdict, double Confidence, string Reason)?> ClassifyAsync(
        InboundMessage message,
        CancellationToken token
    );
}

// Tier 3: a role's way to recognize an existing task (a PR URL, a ticket number) beyond thread matching.
public interface IIntakeMatcher
{
    string? Match(InboundMessage message, RequestStore requests);
}

// Tier 5: a role's way to start real work on an actionable message. Return null to pass.
public interface IIntakeHandler
{
    ProactiveWork? DiveIn(InboundMessage message, IntakeRecord record);
}

public sealed record IntakeRecord(
    string Id,
    string Source,
    string ExternalId,
    string? ThreadKey,
    string From,
    string Subject,
    string Verdict,
    string DecidedBy,
    string Reason,
    string? RequestId,
    string? MatchedBy,
    string Priority,
    string PriorityBy,
    string? TaskId,
    string[] Tiers,
    string CreatedAt,
    string? Body = null,
    bool Dismissed = false,
    string[]? Flags = null,
    string? Category = null
);

// A cheap category for every message, from its sender, subject and body (no model): action, question, meeting,
// notification, newsletter or fyi.
public static class IntakeCategory
{
    static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);

    public static string Of(InboundMessage message, string verdict)
    {
        bool Matches(string text, string pattern) =>
            System.Text.RegularExpressions.Regex.IsMatch(
                text,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                Limit
            );
        var head = message.Body.Length > 600 ? message.Body[..600] : message.Body;
        if (verdict == "act")
            return "action";
        if (
            Matches(
                message.Subject,
                @"^(accepted|declined|tentative|canceled|cancelled|updated invitation|invitation)\b|meeting (request|invitation)"
            )
        )
            return "meeting";
        if (
            Matches(
                message.From,
                @"no-?reply|do-?not-?reply|notifications?@|alerts?@|@github\.com|azuredevops|microsoftonline|@email\.teams\.microsoft\.com|@accountprotection\.microsoft\.com"
            )
        )
            return "notification";
        if (Matches(message.Body, @"\bunsubscribe\b|view (this|it) in (your|a) browser"))
            return "newsletter";
        if (head.Contains('?') || message.Subject.Contains('?'))
            return "question";
        return "fyi";
    }

    // "Avery Stone <dana@example.com>" -> "dana@example.com"
    public static string? Address(string from) =>
        System.Text.RegularExpressions.Regex.Match(
            from,
            @"[^\s<>""]+@[^\s<>""]+",
            System.Text.RegularExpressions.RegexOptions.None,
            Limit
        )
            is { Success: true } match
            ? match.Value.ToLowerInvariant()
            : null;
}

// Each step costs more than the one before, so each runs only when the cheaper ones couldn't settle it:
// rules -> Jev -> match an existing task -> prioritize -> dive in. The tiers that ran are recorded.
public sealed class IntakePipeline(
    IntakeStore store,
    RequestStore requests,
    AgentStore agents,
    WorkspaceOwner owner,
    IEnumerable<IIntakeRule> rules,
    IEnumerable<IIntakeClassifier> classifiers,
    IEnumerable<IIntakeMatcher> matchers,
    IEnumerable<IIntakeHandler> handlers
)
{
    public async Task<IntakeRecord> ProcessAsync(InboundMessage message, CancellationToken token)
    {
        if (store.Find(message.Source, message.ExternalId) is { } seen)
            return seen;
        // Clean first: hidden characters out, secrets redacted, injection flagged. Everything below sees only the cleaned text.
        var subject = ContentGuard.Clean(message.Subject, 300);
        var cleanBody = ContentGuard.Clean(message.Body, 8000);
        var sender = ContentGuard.Clean(message.From, 320);
        var flags = subject.Flags.Concat(cleanBody.Flags).Concat(sender.Flags).Distinct().ToArray();
        message = message with { Subject = subject.Text, Body = cleanBody.Text, From = sender.Text };
        var tiers = new List<string> { "rules" };
        // 1. Rules.
        IntakeVerdict? verdict = null;
        string? priority = null;
        string decidedBy = "rule",
            reason = "";
        foreach (var rule in rules)
        {
            if (rule.Evaluate(message) is not { } outcome)
                continue;
            priority ??= outcome.Priority;
            if (outcome.Verdict is { } decided && verdict is null)
            {
                verdict = decided;
                reason = outcome.Reason;
            }
        }
        // A message that looks like it tries to instruct the agent is held for the owner: no classifier, no conversation, no action.
        if (flags.Contains(ContentGuard.Injection))
            return store.Save(
                new IntakeRecord(
                    Guid.NewGuid().ToString("N"),
                    message.Source,
                    message.ExternalId,
                    message.ThreadKey,
                    message.From,
                    Clip(message.Subject, 200),
                    "review",
                    "guard",
                    "Held for you: it contains text that tries to instruct the agent (possible prompt injection).",
                    null,
                    null,
                    priority ?? "normal",
                    priority is null ? "default" : "rule",
                    null,
                    [.. tiers],
                    DateTimeOffset.UtcNow.ToString("O"),
                    Clip(message.Body, 2000),
                    false,
                    flags
                )
            );
        // 2. Classifier, only when no rule decided.
        if (verdict is null)
        {
            foreach (var classifier in classifiers)
            {
                tiers.Add("classifier");
                if (await classifier.ClassifyAsync(message, token) is { } classified)
                {
                    verdict = classified.Verdict;
                    decidedBy = "jev";
                    reason = classified.Reason;
                    break;
                }
            }
            if (verdict is null)
            {
                verdict = IntakeVerdict.Review;
                decidedBy = "default";
                reason = "No rule or classifier was sure, so it waits for you.";
            }
        }
        var record = new IntakeRecord(
            Guid.NewGuid().ToString("N"),
            message.Source,
            message.ExternalId,
            message.ThreadKey,
            message.From,
            Clip(message.Subject, 200),
            verdict.Value.ToString().ToLowerInvariant(),
            decidedBy,
            Clip(reason, 300),
            null,
            null,
            priority ?? "normal",
            priority is null ? "default" : "rule",
            null,
            [],
            DateTimeOffset.UtcNow.ToString("O"),
            Flags: flags.Length == 0 ? null : flags
        );
        record = record with { Category = IntakeCategory.Of(message, record.Verdict) };
        // People the owner invited as boss or trusted are high priority.
        if (
            IntakeCategory.Address(message.From) is { } senderAddress
            && requests.FindPerson(senderAddress) is { Revoked: false } person
            && PersonRoles.Normalize(person.Role) is PersonRoles.Boss or PersonRoles.Trusted
            && record.Priority != "high"
        )
            record = record with { Priority = "high", PriorityBy = "sender" };
        if (verdict == IntakeVerdict.Ignore)
            return store.Save(record with { Tiers = [.. tiers] });
        // A message that only might matter waits in the inbox list; it doesn't open a conversation until the owner says so,
        // unless it continues a conversation that already exists.
        var continuing =
            message.ThreadKey is { Length: > 0 } existingThread
            && store.RequestForThread(message.Source, existingThread) is not null;
        if (verdict == IntakeVerdict.Review && !continuing)
            return store.Save(record with { Tiers = [.. tiers], Body = Clip(message.Body, 2000) });
        // Only an immediate action item (act, and high priority) opens a new conversation; other actionable mail is
        // categorized and prioritized in the inbox, where the owner can promote it.
        if (verdict == IntakeVerdict.Act && !continuing && record.Priority != "high")
        {
            tiers.Add("prioritize");
            return store.Save(record with { Tiers = [.. tiers], Body = Clip(message.Body, 2000) });
        }

        // 3. Match: same thread first (free), then role matchers, else a new conversation.
        tiers.Add("match");
        string? requestId = message.ThreadKey is { Length: > 0 } thread
            ? store.RequestForThread(message.Source, thread)
            : null;
        var matchedBy = requestId is null ? null : "thread";
        if (requestId is null)
            foreach (var matcher in matchers)
                if ((requestId = matcher.Match(message, requests)) is not null)
                {
                    matchedBy = "role";
                    break;
                }
        if (requestId is not null && requests.FindRequest(requestId) is not null)
            requests.AddMessage(
                requestId,
                message.From,
                Clip(message.Subject + "\n\n" + message.Body, 4000),
                "intake:" + message.Source + ":" + message.ExternalId
            );
        else
        {
            var created = requests.CreateRequest(
                owner.Login,
                message.Source,
                Clip(message.Subject.Length > 0 ? message.Subject : "Message from " + message.From, 160),
                Clip("From " + message.From + " via " + message.Source + ":\n\n" + message.Body, 4000),
                "intake:" + message.Source + ":" + message.ExternalId
            );
            requestId = created.Id;
            matchedBy = "new";
        }
        record = record with { RequestId = requestId, MatchedBy = matchedBy };

        // 4. Prioritize (rules already hinted; the owner can override later).
        tiers.Add("prioritize");
        // 5. Dive in, only for actionable work: a role's handler, otherwise an assessment reply on shift.
        string? taskId = null;
        if (verdict == IntakeVerdict.Act)
        {
            tiers.Add("dive-in");
            ProactiveWork? work = null;
            foreach (var handler in handlers)
                if ((work = handler.DiveIn(message, record)) is not null)
                    break;
            work ??= new ProactiveWork("reply", "intake:" + record.Id, "Assess: " + record.Subject, null, requestId);
            taskId = agents.EnqueueProactive(work)?.Id;
        }
        return store.Save(record with { TaskId = taskId, Tiers = [.. tiers] });
    }

    // The owner turns a waiting message into a task: it opens a conversation and, on shift, an assessment.
    public IntakeRecord Promote(string id)
    {
        var record = store.Find(id) ?? throw new WorkspaceException("That message is no longer in the inbox.", 404);
        if (record.RequestId is not null)
            return record;
        var created = requests.CreateRequest(
            owner.Login,
            record.Source,
            Clip(record.Subject.Length > 0 ? record.Subject : "Message from " + record.From, 160),
            Clip("From " + record.From + " via " + record.Source + ":\n\n" + (record.Body ?? ""), 4000),
            "intake:" + record.Source + ":" + record.ExternalId
        );
        var task = agents.EnqueueProactive(
            new ProactiveWork("reply", "intake:" + record.Id, "Assess: " + record.Subject, null, created.Id)
        );
        return store.Save(
            record with
            {
                RequestId = created.Id,
                MatchedBy = "owner",
                Verdict = "act",
                DecidedBy = "owner",
                TaskId = task?.Id,
                Dismissed = false,
            }
        );
    }

    public IntakeRecord Dismiss(string id) =>
        store.Save(
            (store.Find(id) ?? throw new WorkspaceException("That message is no longer in the inbox.", 404)) with
            {
                Dismissed = true,
            }
        );

    static string Clip(string value, int limit) => value.Length <= limit ? value : value[..limit];
}

// Owner-editable rules in intake-rules.json; regular expressions are case-insensitive and time-limited.
public sealed record IntakeRuleSet(
    string[] IgnoreSenders,
    string[] IgnorePatterns,
    string[] ActPatterns,
    string[] HighPriorityPatterns,
    string[] VipSenders
)
{
    public static IntakeRuleSet Default { get; } =
        new(
            ["noreply", "no-reply", "donotreply", "notifications@", "mailer-daemon"],
            ["\\bunsubscribe\\b", "\\bnewsletter\\b", "out of (the )?office", "automatic reply", "\\bwebinar\\b"],
            [
                "\\bcan you\\b",
                "\\bplease (review|fix|look|send|check)\\b",
                "\\baction (required|needed)\\b",
                "\\bby (today|tomorrow|eod|end of day)\\b",
            ],
            ["\\burgent\\b", "\\basap\\b", "\\bblocker\\b", "\\bproduction (is )?down\\b", "\\boutage\\b"],
            []
        );

    public static IntakeRuleSet Load(string dataRoot)
    {
        var path = Path.Combine(dataRoot, "intake-rules.json");
        try
        {
            if (
                File.Exists(path)
                && JsonSerializer.Deserialize<IntakeRuleSet>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                )
                    is { } saved
            )
                return saved;
        }
        catch (JsonException) { }
        return Default;
    }
}

public sealed class PatternIntakeRule(IntakeRuleSet rules) : IIntakeRule
{
    static bool Any(string[] patterns, string text) =>
        patterns.Any(pattern =>
        {
            try
            {
                return Regex.IsMatch(
                    text,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50)
                );
            }
            catch (Exception error) when (error is ArgumentException or RegexMatchTimeoutException)
            {
                return false;
            }
        });

    public RuleOutcome? Evaluate(InboundMessage message)
    {
        var text = message.Subject + "\n" + message.Body;
        var from = message.From.ToLowerInvariant();
        var vip = rules.VipSenders.Any(sender => from.Contains(sender.ToLowerInvariant()));
        var priority = vip || Any(rules.HighPriorityPatterns, text) ? "high" : null;
        if (!vip && rules.IgnoreSenders.Any(sender => from.Contains(sender.ToLowerInvariant())))
            return new(IntakeVerdict.Ignore, null, "Automated sender.");
        if (!vip && Any(rules.IgnorePatterns, text))
            return new(IntakeVerdict.Ignore, null, "Looks like a newsletter or automatic reply.");
        if (Any(rules.ActPatterns, text) || priority == "high")
            return new(
                IntakeVerdict.Act,
                priority,
                vip ? "From someone you marked important." : "Asks you for something."
            );
        return priority is null ? null : new(null, priority, "");
    }
}

public sealed class IntakeStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IntakeStore(string path)
    {
        _path = path;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS intake_signals(id TEXT PRIMARY KEY, source TEXT NOT NULL, external_id TEXT NOT NULL, thread_key TEXT,
              request_id TEXT, created_at TEXT NOT NULL, payload TEXT NOT NULL, UNIQUE(source, external_id));
            CREATE INDEX IF NOT EXISTS intake_by_thread ON intake_signals(source, thread_key);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString()
        );
        connection.Open();
        return connection;
    }

    private IntakeRecord[] Query(string where, params (string Key, object? Value)[] values)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM intake_signals " + where;
        foreach (var (key, value) in values)
            command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var results = new List<IntakeRecord>();
        while (reader.Read())
            results.Add(JsonSerializer.Deserialize<IntakeRecord>(reader.GetString(0), Json)!);
        return [.. results];
    }

    public IntakeRecord? Find(string source, string externalId) =>
        Query("WHERE source=$s AND external_id=$e", ("$s", source), ("$e", externalId)).SingleOrDefault();

    public IntakeRecord? Find(string id) => Query("WHERE id=$id", ("$id", id)).SingleOrDefault();

    public string? RequestForThread(string source, string thread) =>
        Query(
                "WHERE source=$s AND thread_key=$t AND request_id IS NOT NULL ORDER BY created_at DESC LIMIT 1",
                ("$s", source),
                ("$t", thread)
            )
            .SingleOrDefault()
            ?.RequestId;

    public IntakeRecord[] Recent(int count) => Query("ORDER BY created_at DESC LIMIT $n", ("$n", count));

    public IntakeRecord Save(IntakeRecord record)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO intake_signals VALUES($id,$s,$e,$t,$r,$c,$p) ON CONFLICT(id) DO UPDATE SET request_id=excluded.request_id,payload=excluded.payload";
        foreach (
            var (key, value) in new (string, object?)[]
            {
                ("$id", record.Id),
                ("$s", record.Source),
                ("$e", record.ExternalId),
                ("$t", record.ThreadKey),
                ("$r", record.RequestId),
                ("$c", record.CreatedAt),
                ("$p", JsonSerializer.Serialize(record, Json)),
            }
        )
            command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        command.ExecuteNonQuery();
        return record;
    }
}
