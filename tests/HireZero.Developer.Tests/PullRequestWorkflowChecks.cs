using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;

public static class PullRequestWorkflowChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var account = new GitHubIdentity(1, "owner");
        var sha = new string('a', 40);
        var evidence = new PullRequestEvidence(
            "owner/example",
            10,
            42,
            "Concrete failure",
            sha,
            "owner",
            true,
            false,
            "owner/example",
            10,
            "fix/handler",
            "main",
            "clean",
            true,
            [new("tests", "success", null)],
            [],
            [new("reviewer", "User", "APPROVED", sha)],
            [new("src/handler.cs", "modified", "patch")],
            [],
            [],
            []
        );
        check(PullRequestPolicy.Merge(evidence, account).Ready, "Only a fully ready current revision may merge.");
        check(
            PullRequestPolicy.Merge(evidence with { Reviews = [] }, account).Ready,
            "The owner's own confirmation suffices when no other reviewer is required; branch protection still applies via mergeability."
        );
        foreach (
            var changed in new[]
            {
                evidence with
                {
                    Author = "other",
                },
                evidence with
                {
                    Open = false,
                },
                evidence with
                {
                    Draft = true,
                },
                evidence with
                {
                    Complete = false,
                },
                evidence with
                {
                    MergeableState = "unknown",
                },
                evidence with
                {
                    MergeableState = "blocked",
                },
                evidence with
                {
                    Checks = [],
                },
                evidence with
                {
                    Reviews = [new("reviewer", "User", "CHANGES_REQUESTED", sha)],
                },
                evidence with
                {
                    Threads = [new("t", "src/handler.cs", 1, false, true, true, [])],
                },
            }
        )
            check(
                !PullRequestPolicy.Merge(changed, account).Ready,
                "Missing or stale ownership, checks, review or merge evidence must block merging."
            );
        foreach (var state in new[] { "pending", "failure", "neutral", "skipped", "cancelled", "unknown" })
            check(
                !PullRequestPolicy.Merge(evidence with { Checks = [new("tests", state, null)] }, account).Ready,
                "Non-green checks must block merging."
            );
        check(
            PullRequestPolicy
                .References("Please inspect https://github.com/owner/example/pull/42/files")
                .Single()
                .Number == 42,
            "PR identifiers should parse without command phrasing."
        );
        check(
            PullRequestPolicy.References("https://github.com.evil/owner/example/pull/42").Length == 0,
            "External URLs must not become GitHub resources."
        );
        var strategy = Strategy();
        var ciClient = new SyntheticGitHub { CiEvidence = true };
        var ciEvidence = await ciClient.EvidenceAsync("owner/example", 42, true, default);
        check(
            ciEvidence.Checks[0].Evidence!.Contains("error TS2322"),
            "An early actionable error must survive a long CI log instead of leaving only the audit-fix footer."
        );
        check(
            ciEvidence.Checks[0].Evidence!.Contains("https://github.com/owner/example/actions/runs/100/job/22"),
            "Failure evidence must identify the exact job it came from."
        );
        check(
            !ciEvidence.Checks[0].Evidence!.Contains("fictional-test-value"),
            "CI extraction must redact credentials before selecting context."
        );
        check(
            ciEvidence.ContextFiles.Any(f =>
                f.Path == ".github/workflows/ci.yml" && f.SourceExcerpt.Contains("2:   - run: npm run check")
            ),
            "The diagnosis must receive the failing workflow at the exact PR head with line evidence."
        );
        check(
            ciEvidence.ContextFiles.Any(f => f.Path == "package.json" && f.SourceExcerpt.Contains("tsc --noEmit")),
            "Node CI diagnosis must receive package script definitions."
        );
        check(
            ciEvidence.Files.Length == 1 && ciClient.Writes == 0,
            "Supporting CI files are read-only context, not additional changed files or write authorization."
        );
        check(
            ciEvidence.ContextFiles.Any(f => f.Path.Contains("package-lock.json") && f.SourceExcerpt.Contains("1.0.0")),
            "Audit diagnosis must retrieve resolved dependency versions itself."
        );
        check(
            ciEvidence.ContextFiles.Any(f =>
                f.Path.Contains("GHSA-") && f.SourceExcerpt.Contains("\"firstPatchedVersion\":\"1.0.1\"")
            ),
            "Audit diagnosis must read the string-valued patched version from the GitHub global advisory API."
        );
        var apiReads = ciClient.ApiReads.Count;
        var deniedEvidence = await ciClient.GatherEvidenceAsync(
            ciEvidence,
            [new("file", ".env"), new("file", "../../private"), new("advisory", "../private"), new("log", "999")],
            default
        );
        check(
            ciClient.ApiReads.Count == apiReads && ciClient.LogReads == 1,
            "Evidence requests must not retrieve secrets, traverse paths or read an unrelated job."
        );
        check(
            deniedEvidence.Limitations.Length >= 4,
            "Denied or unavailable evidence must remain explicit without asking the owner to paste it."
        );
        var logReads = ciClient.LogReads;
        var spoofed = ciEvidence with
        {
            Checks = [new("fake", "failure", "https://github.com/owner/example/actions/runs/100/job/999")],
        };
        await ciClient.GatherEvidenceAsync(spoofed, [new("log", "999")], default);
        check(
            ciClient.LogReads == logReads,
            "An untrusted log string must not substitute for a verified check's job URL."
        );
        var calls = 0;
        var prompts = new List<string>();
        var baseline = ciEvidence with
        {
            Files = [new("neo-pricing.ts", "modified", "+ existing Neo multiplier change")],
        };
        var planned = await PullRequestPlanner.PlanAsync(
            "Fix the failed CI job",
            baseline,
            ciClient,
            (model, instructions, prompt) =>
            {
                prompts.Add(prompt);
                calls++;
                check(
                    instructions.Contains("existing baseline")
                        && instructions.Contains("ONLY to behavior changes this NEW remediation"),
                    "Planning must treat earlier PR business-rule changes as the owner's baseline."
                );
                var reply =
                    calls == 1 ? strategy with { EvidenceRequests = [new("file", "src/handler.cs")] } : strategy;
                return Task.FromResult(JsonSerializer.Serialize(reply));
            },
            default
        );
        check(
            calls == 3 && prompts[1].Contains("file:src/handler.cs:1"),
            "The agent must gather requested files and resume reasoning before final validation."
        );
        check(
            planned is { } plannedResult
                && plannedResult.Strategy.EvidenceRequests.Length == 0
                && plannedResult.Strategy.BusinessRuleChanges.Length == 0,
            "A resolved evidence request with a baseline-only business change must not need owner clarification or fresh business approval."
        );
        check(
            ciClient.Writes == 0,
            "Autonomous evidence gathering and reasoning must never begin coding or GitHub writes."
        );
        var boundedCalls = 0;
        var readsBeforeBound = ciClient.ApiReads.Count;
        var unresolved = await PullRequestPlanner.PlanAsync(
            "Inspect this failure",
            baseline,
            ciClient,
            (model, instructions, prompt) =>
            {
                boundedCalls++;
                return Task.FromResult(
                    JsonSerializer.Serialize(strategy with { EvidenceRequests = [new("file", "src/handler.cs")] })
                );
            },
            default
        );
        check(
            boundedCalls == 4 && ciClient.ApiReads.Count == readsBeforeBound + 2,
            "Evidence gathering must be bounded across both triage and validation, never spin indefinitely."
        );
        check(
            unresolved is { } stillMissing
                && stillMissing.Strategy.EvidenceRequests.Length == 1
                && stillMissing.Strategy.Questions.Length == 0,
            "Unresolved machine evidence must remain a tool request, not a demand for the owner to spoonfeed logs."
        );
        check(
            PullRequestPolicy.ParseStrategy(
                JsonSerializer.Serialize(strategy with { EvidenceRequests = [new("merge", "42")] })
            )
                is null,
            "Structured evidence requests must not grant write capabilities."
        );
        foreach (
            var path in new[]
            {
                ".env.local",
                ".npmrc",
                ".aws/credentials",
                ".azure/accessTokens.json",
                "auth-profiles.json",
                "../outside",
            }
        )
            check(
                !GitHubReviewClient.AllowedEvidencePath(path),
                "The evidence tool must reject secret files and credential stores."
            );
        check(
            !PullRequestPolicy.Safe("{\"password\":\"fictional-private-value\"}").Contains("fictional-private-value"),
            "Quoted JSON credentials must be withheld before model context."
        );
        check(
            !PullRequestPolicy
                .Safe("https://fictional:fictional-private-value@registry.invalid")
                .Contains("fictional-private-value"),
            "Credential-bearing registry URLs must be withheld."
        );
        check(
            !PullRequestPolicy
                .Safe("-----BEGIN PRIVATE KEY-----\nfictional-private-body\n-----END PRIVATE KEY-----")
                .Contains("fictional-private-body"),
            "A private key block must be withheld including its body."
        );
        // Publication flags secret-shaped lines by file and line (never the value); real-looking tokens can never be confirmed away.
        const string secretShaped =
            "const ok = 1;\nconst db = { password: \"placeholder\" };\nconst url = \"https://app:placeholder@registry.invalid/npm\";";
        var placeholder = PullRequestPolicy.SensitiveLines("test/config.test.mjs", secretShaped);
        check(
            placeholder
                .Select(PullRequestPolicy.Describe)
                .SequenceEqual(["test/config.test.mjs line 2", "test/config.test.mjs line 3"])
                && placeholder.All(line => !line.Credential),
            "Placeholder fields are flagged by line, as confirmable."
        );
        check(
            PullRequestPolicy.SensitiveLines("a.js", "token = \"ghp_" + new string('a', 30) + "\"")
                is [{ Credential: true }]
                && PullRequestPolicy.SensitiveLines("k.pem", "-----BEGIN PRIVATE KEY-----") is [{ Credential: true }],
            "Real-looking tokens and keys are credentials."
        );
        check(
            PullRequestPolicy.SensitiveHash(placeholder)
                == PullRequestPolicy.SensitiveHash(
                    PullRequestPolicy.SensitiveLines("test/config.test.mjs", secretShaped)
                )
                && PullRequestPolicy.SensitiveHash(placeholder)
                    != PullRequestPolicy.SensitiveHash(
                        PullRequestPolicy.SensitiveLines(
                            "test/config.test.mjs",
                            secretShaped.Replace("\"placeholder\" }", "\"changed\" }")
                        )
                    ),
            "A confirmation binds to the exact lines; editing one needs a new confirmation."
        );
        // Only lines this run adds are scanned: pre-existing fixtures that merely shift down never block publication.
        check(
            PullRequestPolicy
                .AddedLines(
                    "diff --git a/t.mjs b/t.mjs\n--- a/t.mjs\n+++ b/t.mjs\n@@ -3,0 +4,2 @@\n+const a = 1;\n+const b = 2;\n@@ -40 +42 @@\n-old\n+new\n"
                )
                .SetEquals([4, 5, 42]),
            "Added line numbers come from the diff's hunks."
        );
        // A lockfile reaches the business-rule review as its package version changes, not as raw resolution or nothing at all.
        const string lockDiff =
            "diff --git a/package-lock.json b/package-lock.json\n--- a/package-lock.json\n+++ b/package-lock.json\n@@ -10,7 +10,7 @@\n     \"node_modules/sharp\": {\n-      \"version\": \"0.35.4\",\n+      \"version\": \"0.35.5\",\n-      \"integrity\": \"sha512-old\",\n+      \"integrity\": \"sha512-new\",\n@@ -40,6 +40,6 @@\n     \"node_modules/@img/sharp-linux-x64\": {\n-      \"version\": \"0.35.4\",\n+      \"version\": \"0.35.5\",\n@@ -90,3 +90,3 @@\n     \"node_modules/postcss/node_modules/source-map-js\": {\n-      \"version\": \"1.2.1\",\n+      \"version\": \"1.2.2\",\n";
        check(
            DirectCoding
                .LockVersionChanges(lockDiff.Split('\n'))
                .SequenceEqual([
                    "@img/sharp-linux-x64 0.35.4 → 0.35.5",
                    "sharp 0.35.4 → 0.35.5",
                    "source-map-js 1.2.1 → 1.2.2",
                ])
                && DirectCoding.ForRuleReview(lockDiff).Contains("Package version changes (complete list)")
                && !DirectCoding.ForRuleReview(lockDiff).Contains("sha512"),
            "Lockfile changes are reviewed as package version changes, without hashes."
        );
        // Home: work on a specific PR opens a task thread with a stable key; status questions and chat stay in Home.
        var signal = new PullRequestTaskSignal();
        check(
            signal.TaskKey("https://github.com/Northwind/PartnerQuoteDesk/pull/216 take a look and fix the p1s")
                == "github:Northwind/Partnerquotedesk#216"
                && signal.TaskKey("fix https://dev.azure.com/north/Web/_git/Quote-Api/pullrequest/7081")
                    == "ado:quote-api!7081"
                && signal.TaskKey("can you fix PR 213?") == "pr#213",
            "PR work in Home gets a task key."
        );
        check(
            signal.TaskKey("how's #216 going?") is null
                && signal.TaskKey("what's on my plate today?") is null
                && signal.TaskKey("I need to fix my calendar") is null,
            "Status questions and ordinary chat stay in Home."
        );
        check(
            PullRequestPolicy.Model(strategy) == "openai/gpt-6.1-sol",
            "Sol validates concrete findings after Luna triage even for ordinary priority."
        );
        check(
            PullRequestPolicy.Model(strategy with { Priority = "P1" }) == "openai/gpt-6.1-sol",
            "P1 alone must not escalate to Astra."
        );
        check(
            PullRequestPolicy.Model(strategy with { Ambiguous = true }) == "openai/gpt-6-astra",
            "Ambiguous/security/concurrency issues should use Astra."
        );
        check(
            PullRequestPolicy.Model(strategy with { CrossSystem = true }) == "openai/gpt-6-astra",
            "Cross-system reasoning should use Astra."
        );
        check(
            PullRequestPolicy.ParseStrategy(JsonSerializer.Serialize(strategy)) is not null,
            "A grounded bounded strategy should parse."
        );
        check(
            PullRequestPolicy.ParseStrategy(JsonSerializer.Serialize(strategy with { Evidence = [] })) is null,
            "A proposal without file/line evidence must not qualify."
        );
        check(
            PullRequestPolicy.ParseStrategy(JsonSerializer.Serialize(strategy with { RegressionTest = "" })) is null,
            "A proposal must specify a regression."
        );
        check(
            !PullRequestPolicy.Safe("API_KEY=fictional-test-value\nnormal line").Contains("fictional-test-value"),
            "Potential secret assignments must be withheld inside the adapter."
        );
        check(
            PullRequestPolicy.Safe("Bearer " + new string('x', 40), 5).StartsWith("[sens"),
            "Redaction must happen before truncation."
        );
        foreach (
            var path in new[]
            {
                "../private",
                ".git/config",
                "a/../../b",
                "C:/private",
                "a\\b",
                ".developer-workspace-evidence/before.png",
            }
        )
            check(!GitHubPublication.SafePath(path), "Publication may stage only safe code paths.");
        check(
            GitHubPublication.RegressionProved(Run(true)),
            "Actual matching failed/passed command events should establish the regression."
        );
        check(
            !GitHubPublication.RegressionProved(Run(false)),
            "Self-reported passing tests without a reproduced failure must not authorize publication."
        );
        var echo = JsonSerializer.SerializeToElement(
            new
            {
                events = new[]
                {
                    new
                    {
                        type = "command-completed",
                        dataJson = JsonSerializer.Serialize(new { command = "echo tests passed", exitCode = 1 }),
                    },
                    new
                    {
                        type = "command-completed",
                        dataJson = JsonSerializer.Serialize(new { command = "echo tests passed", exitCode = 0 }),
                    },
                },
            }
        );
        check(!GitHubPublication.RegressionProved(echo), "Echo statements must not count as validation commands.");
        check(
            GitHubPublication.RegressionProved(CommandEvents("npm audit --audit-level=high")),
            "The actual dependency-audit CI gate must establish fail-before/pass-after regression evidence."
        );
        check(
            !GitHubPublication.RegressionProved(CommandEvents("npm audit fix")),
            "A dependency mutation is not a regression validation command."
        );
        check(
            GitHubPublication.RegressionProved(
                CommandEvents(
                    "\"C:\\tools\\pwsh.exe\" -Command 'pwsh -NoProfile -File tests/portfolio-tag-report.Tests.ps1'"
                )
            )
                && GitHubPublication.RegressionProved(
                    CommandEvents("powershell -NoProfile -ExecutionPolicy Bypass -File tests/report.Tests.ps1")
                )
                && GitHubPublication.RegressionProved(CommandEvents("Invoke-Pester -Path tests -CI")),
            "PowerShell test scripts in the test folder and Pester count as regression evidence."
        );
        check(
            !GitHubPublication.RegressionProved(CommandEvents("pwsh -NoProfile -File scripts/deploy.ps1"))
                && !GitHubPublication.RegressionProved(CommandEvents("pwsh -File tests/../scripts/x.ps1"))
                && !GitHubPublication.RegressionProved(
                    CommandEvents("pwsh -NoProfile -File tests/a.Tests.ps1; exit 0")
                ),
            "Scripts outside the test folder, path escapes and chained commands do not."
        );
        static object Event(string command, int exit) =>
            new { type = "command-completed", dataJson = JsonSerializer.Serialize(new { command, exitCode = exit }) };
        var mixed = JsonSerializer.SerializeToElement(
            new
            {
                events = new[]
                {
                    Event("npm audit --omit=dev --audit-level=high", 1),
                    Event("npm test", 1),
                    Event("npm audit --audit-level=high", 1),
                    Event("npm audit --omit=dev --audit-level=high", 0),
                    Event("npm test", 1),
                    Event("npm audit --audit-level=high", 1),
                },
            }
        );
        check(
            GitHubPublication.RegressionProved(mixed)
                && GitHubPublication
                    .Unverified(mixed)
                    .OrderBy(c => c)
                    .SequenceEqual(["npm audit --audit-level=high", "npm test"]),
            "Checks that never pass here (no Docker, an accepted advisory) don't block a proved fix; they are listed as unverified for CI."
        );
        var broke = JsonSerializer.SerializeToElement(
            new
            {
                events = new[]
                {
                    Event("dotnet test --filter MissingResult", 1),
                    Event("dotnet test", 0),
                    Event("dotnet test --filter MissingResult", 0),
                    Event("dotnet test", 1),
                },
            }
        );
        check(
            !GitHubPublication.RegressionProved(broke)
                && GitHubPublication.Evidence(broke).Broken.SequenceEqual(["dotnet test"]),
            "A check that passed and then broke blocks publication."
        );
        foreach (
            var command in new[]
            {
                "dotnet test && echo passed",
                "echo powershell.exe -Command dotnet test",
                "dotnet test; exit 0",
            }
        )
            check(
                !GitHubPublication.RegressionProved(CommandEvents(command)),
                "Combined or fake wrapper commands must not establish test evidence."
            );
        check(
            GitHubPublication.RegressionProved(
                CommandEvents(
                    "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoProfile -Command \"dotnet test --filter MissingResult\""
                )
            ),
            "Native Windows PowerShell wrappers should preserve genuine command evidence."
        );

        var fixture = Path.Combine(Path.GetTempPath(), "workspace-pr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var workflowId = Guid.NewGuid().ToString("N");
        var managed = Path.Combine(GitHubReviewClient.CheckoutRoot, workflowId);
        try
        {
            var database = Path.Combine(fixture, "test.sqlite");
            var requests = new RequestStore(database);
            var store = new GitHubReviewStore(database);
            var request = requests.CreateRequest(
                "local-owner@workspace.invalid",
                "example",
                "PR failure",
                "Please fix PR 42",
                Guid.NewGuid().ToString()
            );
            var github = new SyntheticGitHub();
            var sema = new SyntheticSema();
            var reviewer = new SyntheticRuleReviewer();
            var coordinator = new GitHubFixCoordinator(store, github, sema);
            var service = new PullRequestWorkflowService(store, github, coordinator, reviewer, requests);
            var repository = store.Add(new ReviewRepository("repo", 1, 10, "owner/example", fixture, "source"));
            store.SetAutomatic(repository.Id, true);
            var monitor = new GitHubReviewMonitor(store, github, coordinator);
            await monitor.CheckAsync(repository.Id, default);
            check(
                sema.Tasks == 0 && sema.Builders == 0 && store.Fixes(repository.Id).Length == 0 && github.Writes == 0,
                "A legacy automatic policy observes findings without starting diagnosis, a scout, a repair or a GitHub write."
            );
            var read = await github.EvidenceAsync("owner/example", 42, true, default);
            check(
                read.Checks[0].State == "failure" && read.Checks[0].Evidence!.Contains("Compiler failed"),
                "GitHub reads should deliver concrete check evidence."
            );
            check(
                !read.Checks[0].Evidence!.Contains("fictional-test-value"),
                "Failure evidence must not disclose credentials."
            );
            check(
                github.Writes == 0 && sema.Builders == 0,
                "Reading and diagnosis must not start coding or write to GitHub."
            );
            var workflow = new PullRequestWorkflow(
                workflowId,
                request.Id,
                1,
                GitHubRepositoryChat.ProposalKey(request, []),
                read.Repository,
                10,
                42,
                read.HeadSha,
                read.Snapshot,
                strategy,
                "openai/gpt-6.1-sol"
            );
            workflow = workflow with { ConsentGeneration = requests.AgentConsentGeneration(request.Id) };
            store.SaveWorkflow(workflow);
            check(
                new GitHubReviewStore(database).Workflows(2).Length == 0,
                "An account switch must not inherit workflow authority."
            );
            check(
                new GitHubReviewStore(database).Workflows(1).Single().State == "proposed",
                "A proposal must survive restart without becoming approval."
            );
            var needsRules = workflow with
            {
                Strategy = strategy with { BusinessRuleChanges = ["Change eligibility threshold from 1 to 2."] },
            };
            await Rejected(
                async () => await service.ConfirmAsync(needsRules, default, needsRules.ApprovalSnapshot),
                check,
                "Business rules must have a separate decision before any checkout or worker starts."
            );
            check(
                sema.Builders == 0 && !Directory.Exists(managed),
                "An unapproved business-rule proposal must have no execution side effects."
            );
            var needsAnswer = workflow with
            {
                Strategy = strategy with { Questions = ["Which caller owns eligibility?"] },
            };
            await Rejected(
                async () => await service.ConfirmAsync(needsAnswer, default, needsAnswer.ApprovalSnapshot),
                check,
                "Open scoping questions must block coding."
            );
            workflow = workflow with
            {
                State = "needs-evidence",
                Error = "Previous evidence budget exhausted.",
                Strategy = strategy with { EvidenceRequests = [new("file", "package-lock.json")] },
            };
            store.SaveWorkflow(workflow);
            workflow = await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot);
            check(
                workflow.State == "scoping" && sema.Builders == 0 && github.Writes == 0,
                "Confirmation prepares one durable SemaForge scope before mutation."
            );
            check(
                github.ApiReads.Any(path => path.Contains("/contents/package-lock.json?ref=" + sha))
                    && sema.NextAction.Contains("file:package-lock.json:1"),
                "A confirmed strategy must retrieve remaining GitHub evidence and hand it to the scoped worker, never ask the owner to paste it."
            );
            check(
                sema.NextAction.Contains("existing baseline")
                    && sema.NextAction.Contains("NEW unapproved business-rule"),
                "The coding handoff must preserve existing PR rules and stop before introducing a new unapproved rule."
            );
            check(
                requests.FindRequest(request.Id)?.Status == "ready"
                    && requests.FindRequest(request.Id)?.SemaForgeId == "work",
                "Confirmed work must leave the intake queue and link its authoritative SemaForge task."
            );
            await Rejected(
                async () => await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot),
                check,
                "A repeated confirm must reject replay."
            );
            check(sema.Tasks == 1, "A repeated confirm must not duplicate tasks.");
            await Rejected(
                async () =>
                    await service.ConfirmAsync(
                        workflow with
                        {
                            State = "handoff-attention",
                        },
                        default,
                        workflow.ApprovalSnapshot
                    ),
                check,
                "A handoff that already recorded a SemaForge task must not be retried into a duplicate."
            );
            check(sema.Tasks == 1, "A refused retry must not create another task.");
            check(
                requests.Messages(request.Id).Length == 0 && requests.OwnerMessages(request.Id).Length == 1,
                "Execution notices must stay out of collaborator messages and agent context."
            );
            await service.AdvanceAsync(workflow, default);
            workflow = store.Workflows(1).Single();
            check(
                workflow.State == "coding" && sema.Builders == 1,
                "Only the confirmed exact scope may start Sol through SemaForge."
            );
            check(
                sema.NetworkGranted,
                "A confirmed PR strategy must allow needed dependency downloads/audits without a second routine confirmation."
            );
            await service.AdvanceAsync(workflow, default);
            workflow = store.Workflows(1).Single();
            check(
                workflow.State == "publication-proposed" && github.Commits == 0 && github.Pushes == 0,
                "Validation must wait for a separate publication approval."
            );
            workflow = await service.PublishAsync(workflow, workflow.PublicationSnapshot, default);
            check(
                workflow.State == "published" && workflow.CommitSha == new string('b', 40),
                "A validated run should commit and update the PR."
            );
            check(
                github.Commits == 1 && github.Pushes == 1 && github.Merges == 0 && reviewer.Calls == 1,
                "Delivery must commit/push once and never auto-merge."
            );
            check(
                !PullRequestPolicy
                    .Merge(await github.EvidenceAsync("owner/example", 42, false, default), account)
                    .Ready,
                "Publication must not automatically resolve reviewer threads."
            );
            github.Resolved = true;
            var ready = PullRequestPolicy.Merge(
                await github.EvidenceAsync("owner/example", 42, false, default),
                account
            );
            await Rejected(
                async () => await service.MergeAsync(workflow, sha, ready.Snapshot, default),
                check,
                "A stale merge confirmation must not write to GitHub."
            );
            check(github.Merges == 0, "A rejected merge must have no external action.");
            workflow = await service.MergeAsync(workflow, ready.HeadSha, ready.Snapshot, default);
            check(
                workflow.State == "merged" && github.Merges == 1,
                "Only a fresh explicit merge confirmation may merge."
            );
            await Rejected(
                async () => await service.MergeAsync(workflow, ready.HeadSha, ready.Snapshot, default),
                check,
                "Repeated merge submissions must not replay an external write."
            );
        }
        finally
        {
            // These exact directories belong to this fictional, process-free fixture; no real checkouts are removed.
            if (Directory.Exists(managed))
                Directory.Delete(managed, true);
            Directory.Delete(fixture, true);
        }
        foreach (var behavior in new[] { "rules", "tests", "questions", "failed" })
            await VerifyPauseAsync(behavior, check);
        await VerifyLegacyOfflineAsync(check);
        await VerifyInRepositoryCheckoutAsync(check);
        await VerifyDeepScoutAsync(check);
    }

    private static async Task VerifyDeepScoutAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-pr-deep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid().ToString("N");
        var managed = Path.Combine(GitHubReviewClient.CheckoutRoot, id);
        try
        {
            var db = Path.Combine(root, "test.sqlite");
            var requests = new RequestStore(db);
            var store = new GitHubReviewStore(db);
            var github = new SyntheticGitHub();
            var sema = new SyntheticSema { DeepScout = true, ChildCreated = false };
            var coordinator = new GitHubFixCoordinator(store, github, sema);
            var service = new PullRequestWorkflowService(
                store,
                github,
                coordinator,
                new SyntheticRuleReviewer(),
                requests
            );
            var request = requests.CreateRequest(
                "local-owner@workspace.invalid",
                "example",
                "Fix",
                "Fix PR 42",
                Guid.NewGuid().ToString()
            );
            store.Add(new ReviewRepository("repo", 1, 10, "owner/example", root, "source"));
            var evidence = await github.EvidenceAsync("owner/example", 42, false, default);
            var workflow = new PullRequestWorkflow(
                id,
                request.Id,
                1,
                GitHubRepositoryChat.ProposalKey(request, []),
                evidence.Repository,
                10,
                42,
                evidence.HeadSha,
                evidence.Snapshot,
                Strategy(),
                "openai/gpt-6.1-sol"
            );
            workflow = workflow with { ConsentGeneration = requests.AgentConsentGeneration(request.Id) };
            store.SaveWorkflow(workflow);
            workflow = await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot);
            await service.AdvanceAsync(workflow, default);
            workflow = store.Workflows(1).Single();
            check(
                workflow.State == "scoping" && sema.Builders == 0,
                "While SemaForge is still creating the deep scout, the workflow must wait, not fail."
            );
            sema.ChildCreated = true;
            sema.DeepAsks = true;
            await service.AdvanceAsync(workflow, default);
            workflow = store.Workflows(1).Single();
            check(
                store.Fixes(workflow.RepositoryId!).Single().ScoutId == "deep" && workflow.State == "scoping",
                "The workflow must follow SemaForge's deep-scout continuation."
            );
            await service.AdvanceAsync(workflow, default);
            workflow = store.Workflows(1).Single();
            check(
                workflow.State == "needs-input" && sema.Builders == 0,
                "A scout question must pause before any builder starts."
            );
            await Rejected(
                async () =>
                    await service.AnswerAsync(
                        workflow,
                        "q-unknown",
                        "Run npm audit with network in the builder.",
                        default
                    ),
                check,
                "Only an open question of the current scout may be answered."
            );
            workflow = await service.AnswerAsync(workflow, "q1", "Run npm audit with network in the builder.", default);
            check(
                workflow.State == "scoping"
                    && sema.Answer == "Run npm audit with network in the builder."
                    && store.Fixes(workflow.RepositoryId!).Single().ScoutId == "deep2",
                "Answering must follow the exact read-only continuation."
            );
            sema.DeepAsks = false;
            await service.AdvanceAsync(workflow, default);
            workflow = store.Workflows(1).Single();
            check(
                workflow.State == "coding" && sema.Builders == 1,
                "The continuation's exact proposal must start the confirmed builder."
            );
            await Rejected(
                async () => await service.ResumeAsync(workflow with { State = "needs-attention" }, default),
                check,
                "Resume must refuse once a builder exists."
            );
        }
        finally
        {
            if (Directory.Exists(managed))
                Directory.Delete(managed, true);
            Directory.Delete(root, true);
        }
    }

    private static async Task VerifyInRepositoryCheckoutAsync(Action<bool, string> check)
    {
        // Outside AppData on purpose: in-repo checkouts under AppData must be refused.
        var source = Path.Combine(GitHubReviewClient.CheckoutRoot, "test-source-" + Guid.NewGuid().ToString("N"));
        var hidden = Path.Combine(Path.GetTempPath(), "workspace-pr-appdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(source, ".git", "info"));
        Directory.CreateDirectory(hidden);
        var previous = Environment.GetEnvironmentVariable("HIREZERO_CHECKOUT_ROOT");
        Environment.SetEnvironmentVariable("HIREZERO_CHECKOUT_ROOT", "repo");
        try
        {
            var github = new SyntheticGitHub();
            var evidence = await github.EvidenceAsync("owner/example", 42, false, default);
            PullRequestWorkflow Plan() =>
                new(
                    Guid.NewGuid().ToString("N"),
                    "request",
                    1,
                    "key",
                    evidence.Repository,
                    10,
                    42,
                    evidence.HeadSha,
                    evidence.Snapshot,
                    Strategy(),
                    "openai/gpt-6.1-sol"
                );
            var repository = new ReviewRepository("repo", 1, 10, "owner/example", source, "source");
            var first = Plan();
            var checkout = await github.CheckoutAsync(repository, first, default);
            await github.CheckoutAsync(repository, Plan(), default);
            check(
                checkout.RootPath == Path.Combine(source, ".worktrees", first.Id),
                "Opt-in checkouts must live in the connected repository's .worktrees folder."
            );
            var exclude = File.ReadAllLines(Path.Combine(source, ".git", "info", "exclude"));
            check(
                exclude.Count(line => line == "/.worktrees/") == 1,
                "In-repo checkouts must be excluded locally exactly once, never via a committed ignore file."
            );
            await Rejected(
                async () => await github.CheckoutAsync(repository with { RootPath = hidden }, Plan(), default),
                check,
                "A checkout under AppData must be refused because packaged apps hide it from SemaForge."
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("HIREZERO_CHECKOUT_ROOT", previous);
            Directory.Delete(source, true);
            Directory.Delete(hidden, true);
        }
    }

    private static async Task VerifyLegacyOfflineAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-review-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new GitHubReviewStore(Path.Combine(root, "test.sqlite"));
            var github = new SyntheticGitHub();
            var sema = new SyntheticSema();
            var coordinator = new GitHubFixCoordinator(store, github, sema);
            var repository = store.Add(new ReviewRepository("repo", 1, 10, "owner/example", root, "source"));
            var fix = await coordinator.PrepareAsync(repository, 42, new string('a', 40), ["thread"], default);
            await coordinator.ApproveAsync(repository, fix, "hash", 1, default);
            check(
                sema.Builders == 1 && !sema.NetworkGranted && github.Writes == 0,
                "Existing local-only review approval must stay offline and never gain publication authority."
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task VerifyPauseAsync(string behavior, Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-pr-pause-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid().ToString("N");
        var managed = Path.Combine(GitHubReviewClient.CheckoutRoot, id);
        try
        {
            var db = Path.Combine(root, "test.sqlite");
            var requests = new RequestStore(db);
            var store = new GitHubReviewStore(db);
            var github = new SyntheticGitHub();
            var sema = new SyntheticSema
            {
                BeforeFailure = behavior != "tests",
                NeedsInput = behavior == "questions",
                BuilderError =
                    behavior == "failed" ? "The requested operation requires an element of type Number." : null,
            };
            var reviewer = new SyntheticRuleReviewer { Clear = behavior != "rules" };
            var coordinator = new GitHubFixCoordinator(store, github, sema);
            var service = new PullRequestWorkflowService(store, github, coordinator, reviewer, requests);
            var request = requests.CreateRequest(
                "local-owner@workspace.invalid",
                "example",
                "Fix",
                "Fix PR 42",
                Guid.NewGuid().ToString()
            );
            store.Add(new ReviewRepository("repo", 1, 10, "owner/example", root, "source"));
            var evidence = await github.EvidenceAsync("owner/example", 42, false, default);
            var workflow = new PullRequestWorkflow(
                id,
                request.Id,
                1,
                GitHubRepositoryChat.ProposalKey(request, []),
                evidence.Repository,
                10,
                42,
                evidence.HeadSha,
                evidence.Snapshot,
                Strategy(),
                "openai/gpt-6.1-sol"
            );
            workflow = workflow with { ConsentGeneration = requests.AgentConsentGeneration(request.Id) };
            store.SaveWorkflow(workflow);
            workflow = await service.ConfirmAsync(workflow, default, workflow.ApprovalSnapshot);
            await service.AdvanceAsync(workflow, default);
            workflow = store.Workflows(1).Single();
            await service.AdvanceAsync(workflow, default);
            workflow = store.Workflows(1).Single();
            check(
                workflow.State
                    == (
                        behavior == "rules" ? "business-rule-attention"
                        : behavior == "tests" ? "validation-attention"
                        : behavior == "failed" ? "needs-attention"
                        : "needs-input"
                    ),
                "Unapproved rules, missing regression proof, worker questions and worker failures must pause at their own boundaries."
            );
            if (behavior == "failed")
                check(
                    workflow.Error!.Contains("failed before finishing: The requested operation")
                        && requests.OwnerMessages(request.Id).Any(m => m.Body.Contains("failed before finishing")),
                    "A crashed coding run must say why, on the card and in the chat."
                );
            if (behavior == "questions")
                check(
                    requests.OwnerMessages(request.Id).Any(m => m.Body.Contains("Which caller owns eligibility?")),
                    "A worker question must be posted into the conversation."
                );
            check(github.Writes == 0, "A paused fix must never commit, push or merge.");
            if (behavior == "rules")
                await Rejected(
                    async () => await service.ApproveRulesAsync(workflow, workflow.ReviewedDiff!, default),
                    check,
                    "Already-implemented behavior changes must not be retroactively auto-approved."
                );
        }
        finally
        {
            if (Directory.Exists(managed))
                Directory.Delete(managed, true);
            Directory.Delete(root, true);
        }
    }

    private static JsonElement CommandEvents(string command) =>
        JsonSerializer.SerializeToElement(
            new
            {
                events = new[]
                {
                    new
                    {
                        type = "command-completed",
                        dataJson = JsonSerializer.Serialize(new { command, exitCode = 1 }),
                    },
                    new
                    {
                        type = "command-completed",
                        dataJson = JsonSerializer.Serialize(new { command, exitCode = 0 }),
                    },
                },
            }
        );

    public static FixStrategy Strategy() =>
        new(
            "Restore the missing-result guard.",
            ["Guard before dereferencing; inspect affected callers."],
            ["dotnet test --filter MissingResult"],
            [],
            [],
            false,
            "P2",
            false,
            false,
            "Concrete narrow failure.",
            "An absent result is dereferenced.",
            ["src/handler.cs:5 missing null guard."],
            "MissingResult fails without the guard and passes with it."
        );

    public static JsonElement Run(bool beforeFailure) =>
        JsonSerializer.SerializeToElement(
            new
            {
                id = "builder",
                state = "completed",
                kind = "builder",
                requestedModel = "gpt-6.1-sol",
                reasoningEffort = "high",
                contextHash = "hash",
                proposalVersion = 1,
                assessment = (object?)null,
                questions = Array.Empty<object>(),
                error = (object?)null,
                resultJson = JsonSerializer.Serialize(
                    new
                    {
                        summary = "Guard restored",
                        changedFiles = new[] { "src/handler.cs" },
                        tests = new[] { "dotnet test --filter MissingResult" },
                        risks = Array.Empty<string>(),
                        questions = Array.Empty<string>(),
                    }
                ),
                events = beforeFailure
                    ? new[]
                    {
                        new
                        {
                            type = "command-completed",
                            dataJson = JsonSerializer.Serialize(
                                new { command = "dotnet test --filter MissingResult", exitCode = 1 }
                            ),
                        },
                        new
                        {
                            type = "command-completed",
                            dataJson = JsonSerializer.Serialize(
                                new { command = "dotnet test --filter MissingResult", exitCode = 0 }
                            ),
                        },
                    }
                    : new[]
                    {
                        new
                        {
                            type = "command-completed",
                            dataJson = JsonSerializer.Serialize(
                                new { command = "dotnet test --filter MissingResult", exitCode = 0 }
                            ),
                        },
                    },
            }
        );

    private static async Task Rejected(Func<Task> action, Action<bool, string> check, string label)
    {
        try
        {
            await action();
            check(false, label);
        }
        catch (ReviewAccessException)
        {
            check(true, label);
        }
    }
}

public sealed class SyntheticRuleReviewer : IPullRequestReasoner
{
    public int Calls { get; private set; }
    public bool Clear { get; set; } = true;

    public Task<string> ReviewBusinessRulesAsync(FixStrategy strategy, string diff, CancellationToken token)
    {
        Calls++;
        return Task.FromResult(
            Clear
                ? "{\"clear\":true,\"concerns\":[]}"
                : "{\"clear\":false,\"concerns\":[\"Eligibility changed outside approved intent.\"]}"
        );
    }
}

public sealed class SyntheticSema : HttpMessageHandler, IHttpClientFactory
{
    private string root = "",
        project = "";
    public int Builders { get; private set; }
    public int Tasks { get; private set; }
    public string NextAction { get; private set; } = "";
    public bool NetworkGranted { get; private set; }
    public bool BeforeFailure { get; set; } = true;
    public bool NeedsInput { get; set; }
    public string? BuilderError { get; set; }

    // Mirrors SemaForge's escalation: the first scout completes, then one "deep-scout" child (created a moment later) awaits approval.
    public bool DeepScout { get; set; }
    public bool ChildCreated { get; set; } = true;
    public bool DeepAsks { get; set; }
    public string? Answer { get; private set; }

    public HttpClient CreateClient(string name) =>
        new(this, false) { BaseAddress = new Uri("http://synthetic.invalid") };

    private object Proposal(string id, string kind) =>
        new
        {
            id,
            kind,
            state = "awaiting-approval",
            contextHash = "hash",
            proposalVersion = 1,
            approvedRoots = new[] { root },
            repositoryIds = new[] { "native-repo" },
            assessment = new
            {
                requiredPermissions = new[] { "read", "write", "test" },
                repositoryIds = new[] { "native-repo" },
            },
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var path = request.RequestUri!.AbsolutePath;
        object value = new { };
        if (DeepScout && path == "/api/automation/runs/scout")
            value = new
            {
                id = "scout",
                kind = "scout",
                state = "completed",
                events = new[] { new { phase = "deep-scout" } },
            };
        else if (DeepScout && path == "/api/automation/runs" && request.Method == HttpMethod.Get)
            value = ChildCreated
                ? new object[]
                {
                    new
                    {
                        id = "deep",
                        kind = "deep-scout",
                        parentRunId = "scout",
                        workItemId = "work",
                        networkPolicy = "disabled",
                        approvedRoots = new[] { root },
                    },
                }
                : Array.Empty<object>();
        else if (DeepScout && path == "/api/automation/runs/deep")
            value = DeepAsks
                ? new
                {
                    id = "deep",
                    kind = "deep-scout",
                    state = "input-required",
                    questions = new[]
                    {
                        new
                        {
                            id = "q1",
                            prompt = "Which audit finding?",
                            state = "open",
                        },
                    },
                }
                : Proposal("deep", "deep-scout");
        else if (DeepScout && path == "/api/automation/questions/q1/answer")
        {
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken: token);
            Answer = body!.RootElement.GetProperty("answer").GetString();
            value = new
            {
                id = "deep2",
                kind = "deep-scout",
                parentRunId = "deep",
                networkPolicy = "disabled",
                approvedRoots = new[] { root },
            };
        }
        else if (DeepScout && path == "/api/automation/runs/deep2")
            value = Proposal("deep2", "deep-scout");
        else if (
            DeepScout
            && path is "/api/automation/runs/deep/approve-local-work" or "/api/automation/runs/deep2/approve-local-work"
        )
        {
            Builders++;
            value = new { id = "builder" };
        }
        else if (path == "/api/automation/repositories" && request.Method == HttpMethod.Get)
            value =
                root.Length == 0
                    ? Array.Empty<object>()
                    : new object[]
                    {
                        new
                        {
                            id = "native-repo",
                            rootPath = root,
                            projects = new[] { project },
                            watchEnabled = false,
                            alias = "example",
                            instructions = "",
                            testCommands = Array.Empty<string>(),
                        },
                    };
        else if (path == "/api/automation/repositories")
        {
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken: token);
            root = body!.RootElement.GetProperty("rootPath").GetString()!;
            project = body.RootElement.GetProperty("project").GetString()!;
            value = new { id = "native-repo" };
        }
        else if (path == "/api/work-intelligence/work-items")
        {
            Tasks++;
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken: token);
            NextAction = body!.RootElement.GetProperty("nextAction").GetString()!;
            value = new { id = "work" };
        }
        else if (path == "/api/automation/scope")
            value = new { id = "scout" };
        else if (path == "/api/automation/runs/scout")
            value = Proposal("scout", "scout");
        else if (path == "/api/automation/runs/scout/approve-local-work")
        {
            Builders++;
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken: token);
            NetworkGranted = body!.RootElement.GetProperty("allowNetwork").GetBoolean();
            value = new { id = "builder" };
        }
        else if (path == "/api/automation/runs/builder")
            value =
                BuilderError is not null ? new { state = "failed", error = BuilderError }
                : NeedsInput
                    ? new
                    {
                        state = "input-required",
                        questions = new[]
                        {
                            new
                            {
                                id = "bq",
                                prompt = "Which caller owns eligibility?",
                                state = "open",
                            },
                        },
                    }
                : (object)PullRequestWorkflowChecks.Run(BeforeFailure);
        else if (path.EndsWith("/cancel", StringComparison.Ordinal))
            value = new { state = "canceled" };
        else
            throw new InvalidOperationException("Unexpected synthetic SemaForge request.");
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
