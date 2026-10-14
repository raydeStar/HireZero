using System.Text.Json;
using HireZero.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireZero.Roles.Developer;

// Tracks one approved round and existing review/CI results. New diagnosis, repair and remote review requests pause for approval.
// State: working (a round is being fixed), reviewing (waiting for Codex), checking (waiting for CI), planning (next round),
// needs-you, stopped, healthy.
public sealed record FixLoop(string Id, string RequestId, long AccountId, string Repository, int Number, string State, int Round,
    int MaxRounds, bool StopAfterRound, string? WorkflowId, string? ReviewCommentId, string? ReviewRequestedAt, string? ReviewedSha,
    string? Note, string CreatedAt, string UpdatedAt, string? PolledAt = null, string? PausedOn = null)
{
    public bool Active => State is "working" or "reviewing" or "checking" or "planning";
}

// What Codex said about the current head: still pending, clean (a 👍 or a review without comments), or new findings.
public sealed record CodexVerdict(string State, string[] Findings)
{
    public static readonly CodexVerdict Pending = new("pending", []);
}

public partial class GitHubReviewClient
{
    static bool IsCodex(JsonElement user) => user.ValueKind == JsonValueKind.Object && user.TryGetProperty("login", out var login) &&
        login.GetString()?.Contains("codex", StringComparison.OrdinalIgnoreCase) == true;

    // Posts "@codex review" on the PR and returns the comment's id and time, so its reaction and the review can be matched.
    public virtual async Task<(string Id, string At)> RequestCodexReviewAsync(string repository, int number, CancellationToken token)
    {
        if (!GitHubReviewPolicy.ValidRepository(repository) || number <= 0) throw new ReviewAccessException("Unknown pull request.");
        var result = await JsonAsync(["api", "--hostname", "github.com", "--method", "POST", "repos/" + repository + "/issues/" + number + "/comments", "--input", "-"],
            JsonSerializer.Serialize(new { body = "@codex review" }), token);
        return (result.GetProperty("id").GetRawText(), result.GetProperty("created_at").GetString()!);
    }

    // A review request someone already posted after this commit (by hand, or before a restart), so the loop never asks twice.
    public virtual async Task<(string Id, string At)?> PendingCodexRequestAsync(string repository, int number, string headSha, CancellationToken token)
    {
        var committed = (await ApiAsync("repos/" + repository + "/commits/" + headSha, token)).GetProperty("commit").GetProperty("committer").GetProperty("date").GetString()!;
        var request = (await ApiAsync("repos/" + repository + "/issues/" + number + "/comments?per_page=100", token)).EnumerateArray()
            .Where(comment => comment.GetProperty("body").GetString()?.Trim().Equals("@codex review", StringComparison.OrdinalIgnoreCase) == true &&
                string.CompareOrdinal(comment.GetProperty("created_at").GetString(), committed) > 0)
            .Select(comment => (JsonElement?)comment).LastOrDefault();
        return request is { } found ? (found.GetProperty("id").GetRawText(), found.GetProperty("created_at").GetString()!) : null;
    }

    // Codex answers a review request with a 👍 on the comment (nothing to add) or a review on the head commit (with or without comments).
    public virtual async Task<CodexVerdict> CodexVerdictAsync(string repository, int number, string commentId, string since, string headSha, CancellationToken token)
    {
        foreach (var reaction in (await ApiAsync("repos/" + repository + "/issues/comments/" + commentId + "/reactions?per_page=100", token)).EnumerateArray())
            if (reaction.GetProperty("content").GetString() == "+1" && IsCodex(reaction.GetProperty("user"))) return new("clean", []);
        // It may also say so in a plain PR comment ("Didn't find any major issues", naming the reviewed commit).
        foreach (var comment in (await ApiAsync("repos/" + repository + "/issues/" + number + "/comments?per_page=100", token)).EnumerateArray())
            if (IsCodex(comment.GetProperty("user")) && string.CompareOrdinal(comment.GetProperty("created_at").GetString(), since) > 0 &&
                comment.GetProperty("body").GetString() is { } said && said.Contains("find any major issues", StringComparison.OrdinalIgnoreCase) &&
                said.Contains("`" + headSha[..7], StringComparison.Ordinal))
                return new("clean", []);
        var review = (await ApiAsync("repos/" + repository + "/pulls/" + number + "/reviews?per_page=100", token)).EnumerateArray()
            .Where(item => IsCodex(item.GetProperty("user")) && item.TryGetProperty("submitted_at", out var at) && string.CompareOrdinal(at.GetString(), since) > 0 &&
                item.GetProperty("commit_id").GetString() == headSha)
            .Select(item => (JsonElement?)item).LastOrDefault();
        if (review is null) return CodexVerdict.Pending;
        var reviewId = review.Value.GetProperty("id").GetInt64();
        var findings = (await ApiAsync("repos/" + repository + "/pulls/" + number + "/comments?per_page=100", token)).EnumerateArray()
            .Where(comment => comment.TryGetProperty("pull_request_review_id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt64() == reviewId)
            .Select(comment => FindingTitle(comment)).Take(10).ToArray();
        return new(findings.Length == 0 ? "clean" : "findings", findings);
    }

    // "path:line: P1 Title" from a Codex comment (its first bold phrase is the title), cleaned and bounded.
    static string FindingTitle(JsonElement comment)
    {
        var body = comment.GetProperty("body").GetString() ?? "";
        var priority = System.Text.RegularExpressions.Regex.Match(body, @"\b(P[0-3])\b", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50));
        var titles = System.Text.RegularExpressions.Regex.Matches(body, @"\*\*([^*]{4,400})\*\*", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50));
        // Codex puts its priority badge inside the bold heading ("**<sub>![P1 Badge](…)</sub> Title**"): drop the markup, keep the words.
        static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50)),
            "<[^>]+>", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50)).Trim();
        var title = titles.Select(match => Plain(match.Groups[1].Value)).FirstOrDefault(text => text.Length >= 4) ?? Plain(body.Split('\n')[0]);
        var line = comment.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number ? ":" + l.GetInt32() : "";
        return PullRequestPolicy.Safe(comment.GetProperty("path").GetString() + line + ": " + (priority.Success ? priority.Value + " " : "") + title, 300);
    }
}

// The owner's rule (2026-10-07): the loop finds and scopes work on its own (Codex review requests, reading findings and CI, planning the
// next round), but a round's code change waits for the owner's confirmation unless it's a no-brainer (dependency-only remediation).
// It also pauses for questions, flagged lines, missing proof, the round limit, or the owner's Stop.
public sealed class FixLoopDriver(GitHubReviewStore store, GitHubReviewClient github, PullRequestWorkflowService workflows, IServiceProvider services,
    RequestStore requests, ILogger<FixLoopDriver> log, BudgetLedger? budgets = null) : BackgroundService
{
    public const string Author = "fix-loop";
    static readonly TimeSpan Poll = TimeSpan.FromSeconds(60), ReviewPatience = TimeSpan.FromMinutes(40), CiPatience = TimeSpan.FromMinutes(45);

    // Track the approved round; every later diagnosis, repair and remote action needs a separate approval.
    public async Task<FixLoop> StartAsync(PullRequestWorkflow workflow, CancellationToken token, string snapshot)
    {
        if (snapshot != workflow.ApprovalSnapshot) throw new ReviewAccessException("The strategy changed. Refresh before approving this round.");
        if (store.Loops(workflow.RequestId).FirstOrDefault(loop => loop.Active && loop.Number == workflow.Number &&
            loop.Repository.Equals(workflow.Repository, StringComparison.OrdinalIgnoreCase)) is { } running)
        {
            if (running.WorkflowId != workflow.Id) throw new ReviewAccessException("An existing loop is monitoring a different strategy. Review and stop that loop before starting this strategy with fresh approval.");
            // Resuming monitoring is not execution consent. A still-proposed round must pass the normal claim gates.
            if (workflow.State is "proposed" or "needs-evidence") await workflows.ConfirmAsync(workflow, token, snapshot);
            return running;
        }
        var delivered = store.Workflows(workflow.AccountId, workflow.RequestId).Count(item => item.Number == workflow.Number &&
            item.Repository.Equals(workflow.Repository, StringComparison.OrdinalIgnoreCase) && item.State is "published" or "merge-pending" or "merged");
        if (workflow.State is "proposed" or "needs-evidence") workflow = await workflows.ConfirmAsync(workflow, token, snapshot);
        var round = workflow.State is "published" ? Math.Max(1, delivered) : delivered + 1;
        workflow = workflow with { Round = round }; store.SaveWorkflow(workflow);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var loop = new FixLoop(Guid.NewGuid().ToString("N"), workflow.RequestId, workflow.AccountId, workflow.Repository, workflow.Number,
            "working", round, Math.Max(6, round + 3), false, workflow.Id, null, null, null, null, now, now);
        store.SaveLoop(loop);
        Notify(loop, "Fix loop started at round " + round + ": I'll keep " + workflow.Repository + " #" + workflow.Number +
            " moving: after each push I'll ask Codex to review, read the findings and CI, and have the next round's strategy ready for you. Code changes in each round wait for your approval. Up to round " + loop.MaxRounds + ".");
        return loop;
    }

    // Stop now cancels the running round; stop after this round lets it finish (and deliver) first.
    public async Task<FixLoop> StopAsync(FixLoop loop, bool now, CancellationToken token)
    {
        if (!loop.Active) return loop;
        if (!now)
        {
            loop = Save(loop with { StopAfterRound = true, Note = "Stopping after round " + loop.Round + "." });
            Notify(loop, "OK, I'll finish round " + loop.Round + " and then stop and wait for you.");
            return loop;
        }
        if (loop.WorkflowId is { } id && store.Workflows(loop.AccountId, loop.RequestId).FirstOrDefault(item => item.Id == id) is { } current &&
            current.State is "preparing" or "scoping" or "coding" or "validating" or "proposed")
            await workflows.CancelAsync(current, token);
        loop = Save(loop with { State = "stopped", Note = "Stopped by you during round " + loop.Round + "." });
        Notify(loop, "Stopped the fix loop during round " + loop.Round + ". Nothing else will run until you start it again.");
        return loop;
    }

    public FixLoop Resume(FixLoop loop)
    {
        if (loop.Active || loop.State == "healthy") return loop;
        loop = Save(loop with { State = "working", StopAfterRound = false, Note = null, MaxRounds = Math.Max(loop.MaxRounds, loop.Round + 3) });
        Notify(loop, "Fix loop resumed at round " + loop.Round + ".");
        return loop;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var loop in store.Loops(null).Where(loop => loop.Active || loop.State == "needs-you"))
            {
                try { await AdvanceAsync(loop, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (GitHubSignInException) { }
                catch (Exception error) when (error is ReviewAccessException or ModelUnavailableException or JsonException or InvalidOperationException or WorkspaceException)
                {
                    log.LogInformation("Fix loop {Id} paused: {Message}", loop.Id, error.Message);
                    Pause(store.Loops(loop.RequestId).First(item => item.Id == loop.Id), "the loop hit an error: " + error.Message);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    public async Task AdvanceAsync(FixLoop loop, CancellationToken token)
    {
        using var usage = UsageMeter.For(DeveloperBudget.Role, loop.RequestId);
        var current = loop.WorkflowId is null ? null : store.Workflows(loop.AccountId, loop.RequestId).FirstOrDefault(item => item.Id == loop.WorkflowId);
        switch (loop.State)
        {
            case "needs-you":
                // Once the owner clears what paused a round (approves it, confirms lines or proof), the loop carries on by itself.
                if (current is not null && loop.PausedOn is { } paused && current.State != paused &&
                    current.State is "preparing" or "scoping" or "coding" or "validating" or "committing" or "pushing" or "published" or "merge-pending")
                {
                    loop = Save(loop with { State = "working", Note = null, PausedOn = null });
                    Notify(loop, "Round " + loop.Round + " is moving again, so the fix loop picked back up.");
                }
                return;
            case "working":
                if (current is null) { Pause(loop, "its current round is missing."); return; }
                if (current.State is "preparing" or "scoping" or "coding" or "validating" or "committing" or "pushing") return;
                if (current.State is "published" or "merge-pending")
                {
                    if (loop.StopAfterRound) { Finish(loop with { State = "stopped" }, "Round " + loop.Round + " was delivered and the loop stopped, as you asked."); return; }
                    await RequestReviewAsync(loop, current, token);
                    return;
                }
                if (current.State == "merged") { Finish(loop with { State = "stopped" }, "The PR was merged, so the loop is done."); return; }
                if (current.State == "canceled") { Finish(loop with { State = "stopped" }, "Round " + loop.Round + " was canceled, so the loop stopped."); return; }
                // A paused round (validation, business rule, flagged lines, question) waits on the owner; it picks up again once delivered.
                Pause(loop, "round " + loop.Round + " needs you: " + (current.Error ?? current.State.Replace('-', ' ')));
                return;
            case "reviewing":
                if (!Due(loop)) return;
                loop = Save(loop with { PolledAt = Now() });
                var verdict = await github.CodexVerdictAsync(loop.Repository, loop.Number, loop.ReviewCommentId!, loop.ReviewRequestedAt!, loop.ReviewedSha!, token);
                if (verdict.State == "pending")
                {
                    if (DateTimeOffset.UtcNow - DateTimeOffset.Parse(loop.ReviewRequestedAt!) > ReviewPatience)
                        Pause(loop, "Codex hasn't answered the review request for " + loop.ReviewedSha![..8] + " in " + (int)ReviewPatience.TotalMinutes + " minutes.");
                    return;
                }
                if (verdict.State == "clean")
                {
                    loop = Save(loop with { State = "checking", Note = "Codex is clean on " + loop.ReviewedSha![..8] + "; waiting for CI.", PolledAt = null });
                    Notify(loop, "Round " + loop.Round + ": Codex found nothing new on " + loop.ReviewedSha![..8] + ". Checking CI.");
                    return;
                }
                await NextRoundAsync(loop, "Codex reviewed " + loop.ReviewedSha![..8] + " and posted new findings:\n" + string.Join("\n", verdict.Findings.Select(text => "- " + text)), token);
                return;
            case "checking":
                if (!Due(loop)) return;
                loop = Save(loop with { PolledAt = Now() });
                var evidence = await github.EvidenceAsync(loop.Repository, loop.Number, false, token);
                if (evidence.HeadSha != loop.ReviewedSha) { Pause(loop, "the PR head changed to " + evidence.HeadSha[..8] + " outside the loop."); return; }
                var failing = evidence.Checks.Where(check => check.State is "failure" or "error" or "timed_out" or "action_required" or "cancelled").Select(check => check.Name).ToArray();
                var pending = evidence.Checks.Any(check => check.State is "pending" or "queued" or "in_progress" or "expected" or "waiting");
                if (failing.Length > 0) { await NextRoundAsync(loop, "CI fails on " + loop.ReviewedSha![..8] + ": " + string.Join(", ", failing) + ". Diagnose the failing checks from their logs.", token); return; }
                if (pending)
                {
                    if (DateTimeOffset.UtcNow - DateTimeOffset.Parse(loop.ReviewRequestedAt!) > CiPatience) Pause(loop, "CI hasn't finished on " + loop.ReviewedSha![..8] + ".");
                    return;
                }
                Finish(loop with { State = "healthy" }, loop.Repository + " #" + loop.Number + " is healthy ✓ after round " + loop.Round +
                    ": Codex has nothing new on " + loop.ReviewedSha![..8] + " and every check passes.");
                return;
            case "planning":
                // A proposed next round always waits for its own scoped approval.
                if (current is null) { Pause(loop, "the next round's strategy is missing."); return; }
                if (current.State is "proposed")
                {
                    if (current.Strategy.Questions.Length > 0 || current.Strategy.BusinessRuleChanges.Length > 0 && !current.RulesApproved)
                    {
                        Pause(loop, "round " + loop.Round + "'s strategy needs your decision" + (current.Strategy.BusinessRuleChanges.Length > 0
                            ? " on a business-rule change: " + current.Strategy.BusinessRuleChanges[0] : ": " + current.Strategy.Questions[0]));
                        return;
                    }
                    // The loop finds and scopes work on its own. Code changes wait for the owner, except no-brainers: a dependency-only
                    // remediation (non-major updates, lockfile refreshes, overrides) with no business-rule change or question.
                    if (!NoBrainer(current))
                    {
                        Pause(loop, "round " + loop.Round + "'s strategy is ready. Approve it to start the fix; nothing changes in the code until you do.");
                        return;
                    }
                    if (DeveloperBudget.Blocked(budgets, loop.RequestId) is { } spent)
                    {
                        Pause(loop, "budget reached: " + spent + ". Round " + loop.Round + "'s dependency fix is ready; raise the limit or resume in the cockpit's Budget panel, then approve it.");
                        return;
                    }
                    current = await workflows.ConfirmAsync(current, token, current.ApprovalSnapshot);
                    Notify(loop, "Round " + loop.Round + " is a dependency-only fix, so it started automatically. Sol is updating the dependencies.");
                }
                Save(loop with { State = "working" });
                return;
        }
    }

    async Task RequestReviewAsync(FixLoop loop, PullRequestWorkflow delivered, CancellationToken token)
    {
        var (id, at) = await github.PendingCodexRequestAsync(loop.Repository, loop.Number, delivered.CommitSha!, token)
            ?? await github.RequestCodexReviewAsync(loop.Repository, loop.Number, token);
        loop = Save(loop with { State = "reviewing", ReviewCommentId = id, ReviewRequestedAt = at, ReviewedSha = delivered.CommitSha, PolledAt = Now(), Note = null });
        Notify(loop, "Round " + loop.Round + " delivered " + delivered.CommitSha?[..8] + ". Asked Codex to review it.");
    }

    async Task NextRoundAsync(FixLoop loop, string why, CancellationToken token)
    {
        if (loop.StopAfterRound) { Finish(loop with { State = "stopped" }, why + "\n\nThe loop stopped after round " + loop.Round + ", as you asked."); return; }
        if (loop.Round >= loop.MaxRounds) { Pause(loop with { }, "it reached round " + loop.Round + " (the limit) and there's still more:\n" + why); return; }
        // A new round is new spend: planning it (a model diagnosis) and coding it both wait while the budget is used up.
        if (DeveloperBudget.Blocked(budgets, loop.RequestId) is { } spent)
        {
            Pause(loop, "budget reached: " + spent + ". Raise the limit or resume in the cockpit's Budget panel, then resume the loop. Round " + (loop.Round + 1) + " would cover:\n" + why);
            return;
        }
        var round = loop.Round + 1;
        var request = requests.FindRequest(loop.RequestId) ?? throw new ReviewAccessException("The conversation no longer exists.", 404);
        // The round's ask is posted in the thread as the loop, so the planner and the proposal key both see it.
        var asked = requests.AddMessage(loop.RequestId, Author, "Round " + round + ". " + why + "\n\nPlease inspect " + loop.Repository + " #" + loop.Number +
            " again and propose a fix strategy for these.", "fix-loop:" + loop.Id + ":" + round);
        loop = Save(loop with { State = "planning", Round = round, WorkflowId = null, Note = "Planning round " + round + "." });
        AgentActivity.Note("Fix loop: planning round " + round + " for " + loop.Repository + " #" + loop.Number + ".");
        var answer = await GitHubRepositoryChat.InspectAsync(request, requests.Messages(loop.RequestId), loop.Repository, loop.Number,
            services.GetRequiredService<DeveloperPrompts>(), github, store, requests, token);
        requests.AddMessage(loop.RequestId, "workspace-agent", answer, loop.RequestId + ":" + asked.Id);
        var proposed = store.Workflows(loop.AccountId, loop.RequestId).FirstOrDefault(item => item.Number == loop.Number && item.MessageKey == loop.RequestId + ":" + asked.Id);
        if (proposed is null) { Pause(loop, "round " + round + "'s diagnosis didn't produce a strategy. Ask again in the thread or resume the loop."); return; }
        store.SaveWorkflow(proposed with { Round = round });
        Save(loop with { WorkflowId = proposed.Id });
    }

    // Work the owner pre-approved as routine: dependency advisories only, nothing that changes behavior or needs a decision.
    public static bool NoBrainer(PullRequestWorkflow plan) =>
        DirectCoding.AuditFix(plan) && plan.Strategy.Questions.Length == 0 && plan.Strategy.BusinessRuleChanges.Length == 0 && !plan.Strategy.CrossSystem;

    bool Due(FixLoop loop) => loop.PolledAt is null || DateTimeOffset.UtcNow - DateTimeOffset.Parse(loop.PolledAt) >= Poll;
    static string Now() => DateTimeOffset.UtcNow.ToString("O");
    FixLoop Save(FixLoop loop) { loop = loop with { UpdatedAt = Now() }; store.SaveLoop(loop); return loop; }
    void Pause(FixLoop loop, string why)
    {
        var current = loop.WorkflowId is null ? null : store.Workflows(loop.AccountId, loop.RequestId).FirstOrDefault(item => item.Id == loop.WorkflowId);
        loop = Save(loop with { State = "needs-you", Note = "Paused: " + why, PausedOn = current?.State });
        Notify(loop, "Fix loop paused at round " + loop.Round + ": " + why);
    }
    void Finish(FixLoop loop, string message) { loop = Save(loop with { Note = message }); Notify(loop, message); }
    void Notify(FixLoop loop, string body) =>
        requests.AddOwnerAgentNotice(loop.RequestId, body, "fix-loop:" + loop.Id + ":" + loop.Round + ":" + loop.State + ":" + PullRequestPolicy.Hash(body)[..12]);
}
