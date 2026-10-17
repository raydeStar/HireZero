using System.Text.Json;
using System.Threading.RateLimiting;
using HireZero.Core;
using HireZero.Roles.Developer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// GitHub review policy, intent and HTTP checks that predate the per-group check files.
public static class DeveloperInlineChecks
{
    public static async Task RunAsync()
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition)
                throw new SyntheticCheckFailure(message);
        }
        var account = new GitHubIdentity(1, "owner");
        var repository = new ReviewRepository("repo", 1, 10, "owner/example", "C:\\example", "review-example");
        var comment = new ReviewComment(
            "comment",
            "reviewer",
            "Handle the missing result before dereferencing it.",
            "2026-10-01"
        );
        var thread = new ReviewThread("thread", "src/handler.cs", 5, false, false, true, [comment]);
        var review = new CodeReview(42, "Fix handler", new string('a', 40), "owner", true, 10, true, [thread]);
        Check(GitHubReviewPolicy.Owned(account, repository, review), "The author's current PR should qualify.");
        Check(
            !GitHubReviewPolicy.Owned(account, repository, review with { Author = "someone-else" }),
            "Another author's PR must not qualify."
        );
        Check(
            !GitHubReviewPolicy.Owned(account with { Id = 2 }, repository, review),
            "Changing the GitHub account must revoke repository authority."
        );
        Check(
            !GitHubReviewPolicy.Owned(account, repository, review with { Open = false }),
            "Closed PRs must not qualify."
        );
        Check(
            !GitHubReviewPolicy.Owned(account, repository, review with { Complete = false }),
            "Incomplete retrieval must not qualify."
        );
        Check(
            GitHubReviewPolicy.Owned(account, repository, review with { HeadRepositoryId = 11 }),
            "The author's fork PR may qualify; checkout origin and exact PR head are validated separately."
        );
        Check(
            !GitHubReviewPolicy.Owned(account, repository, review with { HeadRepositoryId = 0 }),
            "A deleted PR source repository must not qualify."
        );
        Check(GitHubReviewPolicy.Actionable(thread), "Current bounded review evidence should be actionable.");
        Check(
            !GitHubReviewPolicy.Actionable(thread with { Outdated = true }),
            "Outdated review evidence must be excluded."
        );
        Check(
            !GitHubReviewPolicy.Actionable(thread with { Resolved = true }),
            "Resolved review evidence must be excluded."
        );
        Check(
            !GitHubReviewPolicy.Actionable(thread with { Complete = false }),
            "Partial thread retrieval must be excluded."
        );
        foreach (
            var candidatePath in new[]
            {
                "../private/file",
                "/private/file",
                "src/../../file",
                "C:/private",
                "src\\file",
                "src//file",
            }
        )
            Check(
                !GitHubReviewPolicy.Actionable(thread with { Path = candidatePath }),
                "Paths must stay within the selected repository."
            );
        Check(
            !GitHubReviewPolicy.Actionable(thread with { Comments = [comment with { Body = new string('x', 2001) }] }),
            "Oversized comments need manual inspection."
        );
        var snapshot = GitHubReviewPolicy.Snapshot(review, [thread.Id]);
        Check(
            snapshot != GitHubReviewPolicy.Snapshot(review with { HeadSha = new string('b', 40) }, [thread.Id]),
            "Changing the PR revision must invalidate approval."
        );
        Check(
            snapshot != GitHubReviewPolicy.Snapshot(review with { HeadRepositoryId = 11 }, [thread.Id]),
            "Changing the PR's source repository must invalidate approval."
        );
        Check(
            snapshot
                != GitHubReviewPolicy.Snapshot(
                    review with
                    {
                        Threads = [thread with { Comments = [comment with { Body = "Changed requirement" }] }],
                    },
                    [thread.Id]
                ),
            "Editing review evidence must invalidate approval."
        );
        Check(
            snapshot
                != GitHubReviewPolicy.Snapshot(
                    review with
                    {
                        Threads = [thread with { Resolved = true }],
                    },
                    [thread.Id]
                ),
            "Resolving review evidence must invalidate approval."
        );
        Check(
            snapshot
                == GitHubReviewPolicy.Snapshot(
                    review with
                    {
                        Threads = [thread, thread with { Id = "unselected" }],
                    },
                    [thread.Id]
                ),
            "Unselected threads must not silently expand the approved briefing."
        );
        Check(
            GitHubReviewPolicy.Brief(repository, review, [thread]).Contains("untrusted reviewer evidence"),
            "Review text must remain evidence, not execution authority."
        );
        Check(
            GitHubReviewPolicy.LocalPermissions(["read", "write", "test"], repository, [thread]),
            "Canonical local permission labels should qualify."
        );
        Check(
            GitHubReviewPolicy.LocalPermissions(
                [
                    "Read access to the example repository",
                    "Local file-edit permission for `src/handler.cs`",
                    "Permission to run existing local validation or tests",
                ],
                repository,
                [thread]
            ),
            "SemaForge's descriptive local permission labels should qualify."
        );
        foreach (
            var permission in new[]
            {
                "network",
                "git push",
                "merge",
                "deployment",
                "Read access to the other repository",
                "Local file-edit permission for `../private`",
            }
        )
            Check(
                !GitHubReviewPolicy.LocalPermissions([permission], repository, [thread]),
                "A proposal must not broaden local review-fix authority."
            );
        Check(
            GitHubReviewPolicy.LocalPermissions(
                ["read", "write", "test", "network"],
                repository,
                [thread],
                allowDependencyNetwork: true
            ),
            "An explicitly confirmed PR strategy can grant dependency network alongside local edits/tests."
        );
        foreach (var permission in new[] { "git push", "merge", "deployment", "Read access to the other repository" })
            Check(
                !GitHubReviewPolicy.LocalPermissions([permission], repository, [thread], allowDependencyNetwork: true),
                "Dependency access must not grant worker publication, deployment or another repository."
            );
        foreach (
            var remote in new[]
            {
                "https://github.com/owner/example.git",
                "git@github.com:owner/example.git",
                "ssh://git@github.com/owner/example",
            }
        )
            Check(
                GitHubReviewPolicy.RemoteRepository(remote) == "owner/example",
                "Known GitHub remotes should match exactly."
            );
        foreach (
            var remote in new[]
            {
                "https://github.com.evil/owner/example",
                "https://example.com/owner/example",
                "https://github.com/owner/example?redirect=x",
                "file:///example",
                "git@github.com:owner/example/../private",
            }
        )
            Check(
                GitHubReviewPolicy.RemoteRepository(remote) is null,
                "Host, path, or repository changes must not broaden authority."
            );
        var chat = new WorkRequest(
            "chat",
            "owner",
            "Development",
            "Connect a repository",
            "Hook up the example repo",
            "intake",
            null,
            "2026-10-01"
        );
        var latest = new ConversationMessage(
            "latest",
            chat.Id,
            "owner",
            "Use that one and handle my review fixes from now on",
            "2026-10-01"
        );
        var previous = new ConversationMessage(
            "previous",
            chat.Id,
            "workspace-agent",
            "The repository is owner/example.",
            "2026-10-01"
        );
        var intent =
            GitHubChatIntent.Parse("""{"action":"enableAutomatic","repository":"owner/example","reply":""}""")
            ?? throw new InvalidOperationException("The scripted intent should decode.");
        Check(
            intent.Action == "enableAutomatic",
            "The structured model response should preserve a specific policy intent."
        );
        Check(
            GitHubChatIntent.Parse(
                """{"action":"none","repository":null,"reply":"What acceptance criteria should this meet?"}"""
            )
                is { Action: "none" },
            "Ordinary chat replies should need no GitHub operation."
        );
        Check(
            GitHubChatIntent.Parse("""{"action":"clarify","repository":null,"reply":"Which repository do you mean?"}""")
                is { Action: "clarify" },
            "An ambiguous reference should retain the model's clarification."
        );
        var syntheticCredential = "ghp_" + new string('a', 24);
        Check(
            GitHubChatIntent.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        action = "connect",
                        repository = syntheticCredential,
                        reply = "",
                    }
                )
            )
                is null,
            "Credential-like model output must not become a GitHub search query."
        );
        using (
            var prompt = JsonDocument.Parse(
                GitHubChatIntent.Prompt(chat, [latest with { Body = syntheticCredential }], [])
            )
        )
            Check(
                prompt.RootElement.GetProperty("latestOwnerMessage").GetString()
                    == "Text withheld because it appears to contain credentials.",
                "Credential-like input must be withheld before model interpretation."
            );
        foreach (
            var invalid in new[]
            {
                "not json",
                "null",
                "{}",
                """{"action":"merge","repository":"owner/example","reply":""}""",
                """{"action":"connect","repository":"../../private","reply":""}""",
                """{"action":"connect","repository":"https://evil.example/private","reply":""}""",
                """{"action":"enableAutomatic","repository":null,"reply":""}""",
                """{"action":"none","repository":"owner/example","reply":"Changed it"}""",
                """{"action":"clarify","repository":null,"reply":""}""",
                JsonSerializer.Serialize(
                    new
                    {
                        action = "connect",
                        repository = new string('x', 101),
                        reply = "",
                    }
                ),
                JsonSerializer.Serialize(
                    new
                    {
                        action = "none",
                        repository = (string?)null,
                        reply = new string('x', 1201),
                    }
                ),
            }
        )
            Check(
                GitHubChatIntent.Parse(invalid) is null,
                "Malformed or broader model output must not become an action."
            );
        var names = new[] { repository.Name };
        using (var prompt = JsonDocument.Parse(GitHubChatIntent.Prompt(chat, [previous, latest], names)))
        {
            Check(
                prompt.RootElement.GetProperty("latestOwnerMessage").GetString() == latest.Body,
                "Natural wording should reach the model unchanged rather than match a command expression."
            );
            Check(
                prompt.RootElement.GetProperty("conversation")[0].GetProperty("text").GetString() == previous.Body,
                "A bounded preceding reply should support reference resolution."
            );
            Check(
                prompt.RootElement.GetProperty("connectedRepositoryNames")[0].GetString() == repository.Name,
                "The model should receive repository names without private execution roots."
            );
            Check(
                !prompt.RootElement.GetRawText().Contains(repository.RootPath),
                "Private checkout paths must not be part of intent classification."
            );
        }
        using (var prompt = JsonDocument.Parse(GitHubChatIntent.Prompt(chat, [], names)))
            Check(
                prompt.RootElement.GetProperty("latestOwnerMessage").GetString() == chat.Description,
                "The first owner request should be interpreted without needing a chat message."
            );
        using (
            var prompt = JsonDocument.Parse(
                GitHubChatIntent.Prompt(
                    chat,
                    Enumerable
                        .Range(0, 30)
                        .Select(index => latest with { Id = index.ToString(), Body = new string('x', 6000) })
                        .ToArray(),
                    Enumerable.Repeat(repository.Name, 30).ToArray()
                )
            )
        )
        {
            Check(
                prompt.RootElement.GetProperty("conversation").GetArrayLength() == 6,
                "Conversation reference context must be bounded."
            );
            Check(
                prompt.RootElement.GetProperty("latestOwnerMessage").GetString()!.Length == 4000,
                "The latest owner message must be bounded."
            );
            Check(
                prompt.RootElement.GetProperty("connectedRepositoryNames").GetArrayLength() == 20,
                "Repository name context must be bounded."
            );
        }
        var key = GitHubRepositoryChat.ProposalKey(chat, [previous, latest]);
        foreach (
            var routeCase in new (string Text, string Status, string? Action)[]
            {
                ("""{"answers":{"route":{"choice":"connect","confidence":0.95}}}""", "suggestion", "connect"),
                (
                    """{"answers":{"route":{"choice":"enableAutomatic","confidence":0.7}}}""",
                    "suggestion",
                    "enableAutomatic"
                ),
                (
                    """{"answers":{"route":{"choice":"disableAutomatic","confidence":0.9}}}""",
                    "suggestion",
                    "disableAutomatic"
                ),
                ("""{"answers":{"route":{"choice":"none","confidence":0.8}}}""", "suggestion", "none"),
                ("""{"answers":{"route":{"choice":"connect","confidence":0.69}}}""", "suggestion", "uncertain"),
                ("""{"answers":{"route":{"choice":"enableAutomatic"}}}""", "suggestion", "uncertain"),
                ("""{"answers":{"route":{"choice":"enableAutomatic","confidence":2}}}""", "suggestion", "uncertain"),
                ("""{"answers":{"route":{"choice":"enableAutomatic","confidence":-1}}}""", "suggestion", "uncertain"),
                ("""{"answers":{"route":{"choice":"merge","confidence":1}}}""", "unavailable", null),
                ("""{"answers":{"route":null}}""", "unavailable", null),
                ("""{"answers":42}""", "unavailable", null),
                ("[]", "unavailable", null),
                ("{}", "unavailable", null),
                ("null", "unavailable", null),
            }
        )
        {
            using var document = JsonDocument.Parse(routeCase.Text);
            var route = GitHubChatRoute.Parse(document.RootElement);
            Check(
                route.Status == routeCase.Status && route.Action == routeCase.Action,
                "Jev must preserve typed chat routes and surface uncertainty or unavailable decisions."
            );
        }
        var matchingRoute = new GitHubChatRoute("suggestion", "enableAutomatic", 0.9, null);
        Check(!matchingRoute.Contradicts(intent), "Agreement between classifiers may prepare a confirmation proposal.");
        Check(
            (matchingRoute with { Action = "connect" }).Contradicts(intent),
            "Confident disagreement must request clarification rather than silently change a policy."
        );
        Check(
            (matchingRoute with { Action = "none" }).Contradicts(intent),
            "An ordinary-conversation route must not silently become an automatic policy proposal."
        );
        Check(
            !(matchingRoute with { Action = "uncertain" }).Contradicts(intent),
            "An uncertain route can fall back to an explicitly unconfirmed OpenClaw proposal."
        );
        Check(
            (matchingRoute with { Action = "uncertain" }).ProposalNote().Contains("uncertain"),
            "Jev uncertainty must be visible in fallback proposals."
        );
        Check(
            GitHubChatRoute.Unavailable("Jev is unavailable.").ProposalNote().Contains("could not confirm"),
            "An unavailable classifier must not look like a verified classification."
        );
        using (var prompt = JsonDocument.Parse(GitHubChatIntent.Prompt(chat, [latest], names, matchingRoute)))
            Check(
                prompt.RootElement.GetProperty("routingHint").GetProperty("Action").GetString() == intent.Action,
                "OpenClaw must receive Jev's routing recommendation for reference resolution."
            );
        using (var questions = JsonDocument.Parse(JsonSerializer.Serialize(GitHubChatClassifier.Questions())))
        {
            var question = questions.RootElement.GetProperty("route");
            Check(
                question.GetProperty("type").GetString() == "choice",
                "The chat classifier must use Jev's typed Choice primitive."
            );
            Check(
                question.GetProperty("criteria").EnumerateObject().Count() == 6
                    && question.GetProperty("criteria").TryGetProperty("uncertain", out _),
                "The classifier must have bounded routes including inspection and uncertainty."
            );
        }
        var proposal = GitHubRepositoryChat.CreateProposal(
            chat.Id,
            key,
            account,
            intent,
            [new(repository.GitHubId, repository.Name)],
            [repository]
        );
        Check(
            !proposal.Confirmed && proposal.Action == "enableAutomatic",
            "Even a model recommendation to enable automation must remain an unconfirmed proposal."
        );
        Check(
            GitHubRepositoryChat.CurrentProposal(proposal, chat, [previous, latest, previous with { Id = "answer" }]),
            "An agent reply must not invalidate the current owner proposal."
        );
        Check(
            !GitHubRepositoryChat.CurrentProposal(
                proposal,
                chat,
                [latest, latest with { Id = "newer", Body = "Actually, keep asking me first" }]
            ),
            "A newer owner message must invalidate a pending policy proposal."
        );
        Check(
            !GitHubRepositoryChat.CurrentProposal(proposal, chat with { Id = "other-chat" }, [latest]),
            "A guessed proposal cannot be confirmed in another conversation."
        );
        Check(
            !GitHubRepositoryChat.CurrentProposal(proposal with { Action = "merge" }, chat, [latest]),
            "An unknown persisted action must fail closed."
        );
        Check(
            GitHubRepositoryChat.SameCheckout(proposal.Choices[0], repository),
            "The proposed policy must bind the exact connected checkout."
        );
        Check(
            !GitHubRepositoryChat.SameCheckout(proposal.Choices[0], repository with { RootPath = "C:\\different" }),
            "Changing the checkout must invalidate a pending automatic policy."
        );
        Check(
            !GitHubRepositoryChat.SameCheckout(proposal.Choices[0], repository with { GitHubId = 11 }),
            "A different GitHub repository must not inherit a policy confirmation."
        );
        foreach (
            var candidates in new[]
            {
                Array.Empty<ReviewRepository>(),
                new[] { repository with { AccountId = 2 } },
                new[] { repository, repository with { Id = "other-checkout" } },
            }
        )
        {
            bool rejected = false;
            try
            {
                GitHubRepositoryChat.CreateProposal(
                    chat.Id,
                    key,
                    account,
                    intent,
                    [new(repository.GitHubId, repository.Name)],
                    candidates
                );
            }
            catch (ReviewAccessException)
            {
                rejected = true;
            }
            Check(
                rejected,
                "Missing, other-account, or ambiguous checkouts must not receive an automatic policy proposal."
            );
        }
        Check(
            GitHubReviewPolicy.LocalPermissions(
                [
                    "Local file-edit permission to implement the selected review fix",
                    "Permission to run existing local validation commands",
                ],
                repository,
                [thread]
            ),
            "Bounded descriptive local-work permissions should qualify."
        );
        var path = Path.Combine(
            Path.GetTempPath(),
            "developer-review-policy-" + Guid.NewGuid().ToString("N") + ".sqlite"
        );
        try
        {
            using (
                var legacy = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
                )
            )
            {
                legacy.Open();
                using var command = legacy.CreateCommand();
                command.CommandText =
                    "CREATE TABLE github_repository_proposals(id TEXT PRIMARY KEY, request_id TEXT NOT NULL, account_id INTEGER NOT NULL, choices TEXT NOT NULL, confirmed INTEGER NOT NULL, UNIQUE(request_id,id)); INSERT INTO github_repository_proposals VALUES('legacy','legacy-chat',1,'[]',0)";
                command.ExecuteNonQuery();
            }
            var store = new GitHubReviewStore(path);
            Check(
                store.Proposals("legacy-chat", 1)[0].Action == "connect",
                "Existing connection proposals must migrate without becoming automatic policy grants."
            );
            store.Add(repository);
            store.Add(repository with { Id = "retry" });
            Check(
                store.Repositories(account.Id).Length == 1,
                "Repeated connection attempts must not duplicate a checkout."
            );
            Check(store.Repositories(2).Length == 0, "A different GitHub account must not inherit connections.");
            Check(!store.Automatic(repository.Id), "Automatic execution must be disabled by default.");
            store.SaveProposal(proposal);
            Check(
                !store.Automatic(repository.Id),
                "Saving an interpreted automatic proposal must not enable execution."
            );
            var recovered = new GitHubReviewStore(path).Proposals(chat.Id, account.Id).Single();
            Check(
                recovered.Action == "enableAutomatic"
                    && recovered.Choices[0].RootPath == repository.RootPath
                    && !recovered.Confirmed,
                "Exact proposed policy and checkout must survive restart without approval."
            );
            store.SaveProposal(proposal with { Action = "disableAutomatic", Choices = [new(999, "owner/other")] });
            recovered = store.Proposals(chat.Id, account.Id).Single();
            Check(
                recovered.Action == "enableAutomatic" && recovered.Choices[0].Id == repository.GitHubId,
                "Retrying interpretation must not rewrite an existing confirmation's action or target."
            );
            Check(store.RepositoryNames().SequenceEqual(names), "Intent context should contain only repository names.");
            store.SetAutomatic(repository.Id, true);
            Check(
                new GitHubReviewStore(path).Automatic(repository.Id),
                "Explicit automatic policy must persist across restarts."
            );
            var fix = new ReviewFix(
                "fix",
                repository.Id,
                review.Number,
                review.HeadSha,
                snapshot,
                [thread.Id],
                "scouting",
                "work",
                "scout",
                null
            );
            var saved = store.Add(fix);
            store.Add(fix with { Id = "retry" });
            Check(store.Fixes(repository.Id).Length == 1, "Repeated scope attempts must not duplicate a task handoff.");
            store.Save(saved with { Status = "approval-pending", WorkItemId = "work", ScoutId = "scout" });
            Check(
                new GitHubReviewStore(path).Fixes(repository.Id)[0].Status == "approval-pending",
                "Uncertain approvals must survive restart and cannot be retried blindly."
            );
            store.SaveProposal(new("proposal", "connection-chat", 1, [new(10, repository.Name)], false));
            Check(
                store.Repositories(1).Length == 1 && store.Proposals("connection-chat", 1).Length == 1,
                "A chat proposal must not authorize another connection."
            );
            Check(
                store.Proposals("connection-chat", 2).Length == 0 && store.Proposals("other-chat", 1).Length == 0,
                "Chat proposals must remain scoped to their conversation and account."
            );
        }
        finally
        {
            File.Delete(path);
        }
        var apiPath = Path.Combine(
            Path.GetTempPath(),
            "developer-review-api-" + Guid.NewGuid().ToString("N") + ".sqlite"
        );
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpClient();
        builder.Services.AddRateLimiter(options =>
            options.AddPolicy("agent", _ => RateLimitPartition.GetNoLimiter("test"))
        );
        builder.Services.AddSingleton(new RequestStore(apiPath));
        builder.Services.AddGitHubReviews(apiPath);
        builder.Services.AddSingleton<IPullRequestReasoner, SyntheticRuleReviewer>();
        await using (var app = builder.Build())
        {
            app.Use(
                (context, next) =>
                {
                    context.Items["role"] = context.Request.Headers["Test-Role"].ToString();
                    return next(context);
                }
            );
            app.UseRateLimiter();
            app.MapGitHubReviews();
            await app.StartAsync();
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            foreach (var role in new[] { "visitor", "manager", "", "revoked" })
            foreach (
                var endpoint in new[]
                {
                    "/pending-approvals",
                    "/connection",
                    "/suggestions",
                    "/repositories",
                    "/reviews",
                    "/resolve?name=example",
                    "/chat/guessed",
                    "/repositories/guessed/fixes",
                    "/repositories/guessed/fixes/guessed",
                    "/repositories/guessed/policy",
                    "/repositories/guessed/reviews",
                    "/evidence?repository=owner/example&number=42",
                    "/workflows/chat/guessed",
                    "/workflows/guessed/merge-check",
                    "/workflows/guessed/evidence",
                    "/workflows/guessed/screenshots/secret.png",
                    "/activity",
                    "/health",
                    "/reconnect",
                }
            )
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/github" + endpoint);
                request.Headers.Add("Test-Role", role);
                using var response = await client.SendAsync(request);
                Check(
                    (int)response.StatusCode == 403,
                    "Non-owners and revoked users must not read GitHub identities, reviews, connections, policies, or guessed fix IDs."
                );
            }
            foreach (var role in new[] { "visitor", "manager" })
            foreach (
                var endpoint in new[]
                {
                    "/repositories",
                    "/chat/guessed/confirm",
                    "/repositories/guessed/scope",
                    "/repositories/guessed/fixes/guessed/approve",
                    "/repositories/guessed/fixes/guessed/rescope",
                    "/repositories/guessed/fixes/guessed/cancel",
                    "/repositories/guessed/check",
                    "/workflows/guessed/confirm",
                    "/workflows/guessed/publish",
                    "/pipeline-watches",
                    "/pipeline-watches/guessed/cancel",
                    "/workflows/guessed/rules",
                    "/workflows/guessed/merge",
                    "/workflows/guessed/cancel",
                    "/workflows/guessed/resume",
                }
            )
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/github" + endpoint);
                request.Headers.Add("Test-Role", role);
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request);
                Check(
                    (int)response.StatusCode == 403,
                    "Non-owners must not propose, approve, or automate fixes using guessed IDs."
                );
            }
            await app.StopAsync();
        }
        File.Delete(apiPath);
        // Routing: with a known project in view, only PR-specific asks go to the GitHub chat responder.
        Check(
            GitHubRepositoryChat.Specific("diagnose #213")
                && GitHubRepositoryChat.Specific("https://github.com/o/r/pull/12")
                && GitHubRepositoryChat.Specific("can you fix PR 7")
                && !GitHubRepositoryChat.Specific("What's going on with QuoteDesk? Quick status please.")
                && !GitHubRepositoryChat.Specific("what's waiting on me"),
            "Broad project questions go to the context-aware answer; numbered PRs and repository actions stay with GitHub chat."
        );
        // GitHub sign-in: recognized from gh's own error text, reported once, cleared by the next success.
        Check(
            GitHubHealth.LooksSignedOut("To get started with GitHub CLI, please run:  gh auth login")
                && GitHubHealth.LooksSignedOut("HTTP 401: Bad credentials (https://api.github.com/user)")
                && !GitHubHealth.LooksSignedOut("HTTP 404: Not Found")
                && !GitHubHealth.LooksSignedOut(null),
            "Only sign-in failures count as signed out; a missing repository doesn't."
        );
        var signedOutEvents = 0;
        void OnSignedOut() => signedOutEvents++;
        GitHubHealth.SignedOut += OnSignedOut;
        GitHubHealth.MarkSignedOut("HTTP 401");
        GitHubHealth.MarkSignedOut("HTTP 401 again");
        Check(
            signedOutEvents == 1
                && GitHubHealth.SignedOutSince is not null
                && GitHubHealth.Message.Contains("Reconnect GitHub"),
            "Signing out is announced once, with how to reconnect."
        );
        GitHubHealth.MarkOk();
        GitHubHealth.SignedOut -= OnSignedOut;
        Check(GitHubHealth.SignedOutSince is null, "The next successful GitHub call clears it.");
        Check(
            GitHubReconnect.ParseCode(
                "! First copy your one-time code: 1A2B-3C4D\nPress Enter to open github.com in your browser..."
            ) == "1A2B-3C4D"
                && GitHubReconnect.ParseCode("Logged in as owner") is null,
            "The device code is read from gh's own prompt."
        );
        Console.WriteLine($"PASS {count} GitHub ownership, scope, stale evidence, durability, and confirmation checks");
    }
}

sealed class SyntheticCheckFailure(string message) : Exception(message);
