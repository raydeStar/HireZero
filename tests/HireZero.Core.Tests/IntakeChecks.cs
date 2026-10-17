using HireZero.Core;

// Synthetic intake: real stores and rules, a counting fake classifier, no Jev or model calls.
public static class IntakeChecks
{
    sealed class Classifier : IIntakeClassifier
    {
        public int Calls;
        public IntakeVerdict? Answer = IntakeVerdict.Act;

        public Task<(IntakeVerdict Verdict, double Confidence, string Reason)?> ClassifyAsync(
            InboundMessage message,
            CancellationToken token
        )
        {
            Calls++;
            return Task.FromResult(
                Answer is { } verdict ? ((IntakeVerdict, double, string)?)(verdict, 0.9, "fake") : null
            );
        }
    }

    static InboundMessage Mail(string from, string subject, string body, string? thread = null) =>
        new("outlook", Guid.NewGuid().ToString("N"), thread, from, subject, body, DateTimeOffset.UtcNow);

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-intake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "intake.sqlite");
            var requests = new RequestStore(db);
            var agents = new AgentStore(db);
            var intake = new IntakeStore(db);
            var jev = new Classifier();
            var pipeline = new IntakePipeline(
                intake,
                requests,
                agents,
                new WorkspaceOwner("owner@example.invalid"),
                [new PatternIntakeRule(IntakeRuleSet.Default with { VipSenders = ["boss@example.invalid"] })],
                [jev],
                [],
                []
            );

            var news = await pipeline.ProcessAsync(
                Mail("news@example.invalid", "Our October newsletter", "Click to unsubscribe."),
                default
            );
            check(
                news.Verdict == "ignore"
                    && news.Tiers.SequenceEqual(["rules"])
                    && jev.Calls == 0
                    && news.RequestId is null,
                "Obvious noise must stop at the rules: no Jev call, no task."
            );
            var bot = await pipeline.ProcessAsync(Mail("noreply@service.invalid", "Your receipt", "Thanks."), default);
            check(bot.Verdict == "ignore" && jev.Calls == 0, "Automated senders are ignored by rules alone.");

            // Actionable mail from someone not invited is categorized and prioritized in the inbox, without opening a conversation.
            var stranger = await pipeline.ProcessAsync(
                Mail("kim@example.invalid", "Pricing sheet", "Can you send the updated pricing sheet?", "thread-0"),
                default
            );
            check(
                stranger is { Verdict: "act", Category: "action", Priority: "normal", RequestId: null, TaskId: null }
                    && stranger.Body!.Contains("pricing sheet")
                    && requests.Requests(null).Length == 0,
                "Actionable but not urgent: it waits in the inbox, categorized, with no conversation or agent work."
            );
            requests.SetPerson("alex@example.invalid", "trusted", false);
            var ask = await pipeline.ProcessAsync(
                Mail(
                    "Alex Kim <alex@example.invalid>",
                    "Q3 numbers",
                    "Can you send the Q3 numbers by tomorrow?",
                    "thread-1"
                ),
                default
            );
            check(
                ask.Verdict == "act" && ask.DecidedBy == "rule" && jev.Calls == 0,
                "A clear request is actionable on rules alone, without Jev."
            );
            check(
                ask is { Priority: "high", PriorityBy: "sender", Category: "action" }
                    && ask.MatchedBy == "new"
                    && requests.FindRequest(ask.RequestId!) is { } created
                    && created.Requester == "owner@example.invalid",
                "A trusted person's actionable message is high priority and opens a conversation right away."
            );
            check(
                ask.Tiers.SequenceEqual(["rules", "match", "prioritize", "dive-in"])
                    && agents.Find(ask.TaskId!) is { Source: "proactive", Kind: "reply" },
                "Diving in queues the agent's assessment as shift work, not as an immediate ask."
            );

            var follow = await pipeline.ProcessAsync(
                Mail("alex@example.invalid", "Re: Q3 numbers", "Any update?", "thread-1"),
                default
            );
            check(
                follow.RequestId == ask.RequestId
                    && follow.MatchedBy == "thread"
                    && requests.Messages(ask.RequestId!).Any(m => m.Body.Contains("Any update?")),
                "A reply in the same thread joins the existing task instead of creating a new one."
            );

            check(
                jev.Calls == 1 && follow.DecidedBy == "jev",
                "A follow-up no rule recognizes is classified like any other message."
            );
            var vague = await pipeline.ProcessAsync(
                Mail("sam@example.invalid", "Thoughts", "I was thinking about the roadmap."),
                default
            );
            check(
                jev.Calls == 2 && vague.DecidedBy == "jev" && vague.Tiers.Contains("classifier"),
                "Only an undecided message reaches the classifier."
            );
            jev.Answer = null;
            var unknown = await pipeline.ProcessAsync(Mail("sam@example.invalid", "Hmm", "Interesting."), default);
            check(
                unknown.Verdict == "review" && unknown.DecidedBy == "default" && unknown.TaskId is null,
                "When nothing is sure, the message waits for the owner and nothing is started."
            );
            check(
                unknown.RequestId is null
                    && unknown.Body == "Interesting."
                    && unknown.Tiers.SequenceEqual(["rules", "classifier"]),
                "A message that only might matter waits in the inbox without opening a conversation."
            );
            var promoted = pipeline.Promote(unknown.Id);
            check(
                promoted.RequestId is { } opened
                    && requests.FindRequest(opened) is not null
                    && agents.Find(promoted.TaskId!) is { Source: "proactive" }
                    && promoted.DecidedBy == "owner",
                "Make a task opens a conversation and queues the assessment."
            );
            check(
                pipeline.Dismiss(vague.Id).Dismissed && intake.Find(vague.Id)!.Dismissed,
                "Dismiss hides a message from the inbox."
            );

            var boss = await pipeline.ProcessAsync(Mail("boss@example.invalid", "Lunch?", "Free at noon?"), default);
            check(boss.Priority == "high" && boss.PriorityBy == "rule", "A VIP sender is high priority by rule.");
            var urgent = await pipeline.ProcessAsync(
                Mail("ops@example.invalid", "URGENT: production is down", "Everything is failing."),
                default
            );
            check(
                urgent.Verdict == "act" && urgent.Priority == "high",
                "Urgent wording is both actionable and high priority."
            );

            var replay = Mail("alex@example.invalid", "Q3 numbers", "Again", "thread-2");
            var first = await pipeline.ProcessAsync(replay, default);
            var second = await pipeline.ProcessAsync(replay, default);
            check(
                first.Id == second.Id && intake.Recent(50).Count(r => r.ExternalId == replay.ExternalId) == 1,
                "The same message is never processed twice."
            );
            check(
                intake.Save(first with { Priority = "low", PriorityBy = "owner" }).PriorityBy == "owner"
                    && intake.Find(first.Id)!.Priority == "low",
                "The owner's priority override is kept."
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
