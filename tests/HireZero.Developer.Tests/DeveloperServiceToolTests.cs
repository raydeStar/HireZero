using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using HireZero.Tools.SemaForge;
using HireZero.Tools.Wiki;
using Microsoft.Data.Sqlite;

// SemaForge and Wiki calls are classified by method and path, and their writes are bound to the exact draft or fix
// that authorizes them (docs/adr/0007-tool-registry.md).
public sealed class DeveloperServiceToolTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "service-tools-" + Guid.NewGuid().ToString("N"));

    public DeveloperServiceToolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    private static ToolInvocation Invocation<T>(IToolContract contract, T input, ToolAuthority authority)
    {
        var json = JsonSerializer.Serialize(input, Web);
        return new(contract, new("developer"), json, ContentGuard.Digest(contract.Id + "\n" + json), authority);
    }

    [Theory]
    [InlineData("GET", "/api/automation/runs/scout", "semaforge.read")]
    [InlineData("POST", "/api/work-intelligence/context-packet", "semaforge.read")]
    [InlineData("POST", "/api/work-intelligence/work-items", "semaforge.write")]
    [InlineData("PATCH", "/api/work-intelligence/work-items/1", "semaforge.write")]
    [InlineData("TRACE", "/", null)]
    public void SemaForgeClassification(string method, string path, string? tool) =>
        Assert.Equal(tool, SemaForgeTools.Classify(new(method, path))?.Id);

    [Theory]
    [InlineData("contribution", "GET", "api/knowledge?project=x", "wiki.read")]
    [InlineData("context", "POST", "/api/pilot/session?role=agent", "wiki.read")]
    [InlineData("contribution", "POST", "api/proposals", "wiki.contribute")]
    [InlineData("publication", "POST", "api/proposals", null)]
    [InlineData("publication", "POST", "api/review/rev", "wiki.publish")]
    [InlineData("contribution", "POST", "api/review/rev", null)]
    [InlineData("unknown", "GET", "api/knowledge", null)]
    public void WikiClassification(string session, string method, string path, string? tool) =>
        Assert.Equal(tool, WikiTools.Classify(new(session, method, path))?.Id);

    private (WikiDraftStore Store, WikiDraft Draft) Draft()
    {
        var database = Path.Combine(_root, "wiki.sqlite");
        var requests = new RequestStore(database);
        var request = requests.CreateRequest(
            "owner@example.invalid",
            "fixture",
            "Document deploys",
            "Steps",
            "wiki-fixture"
        );
        var store = new WikiDraftStore(database);
        var draft = store.Create(
            request,
            new("fixture", "synthetic-fixture", true),
            new(
                "deploy-steps",
                "Rule",
                "Deploy steps",
                "Run the fictional deploy checklist before releasing.",
                "The owner asked to record the fictional checklist.",
                "Fictional owner conversation",
                false,
                false,
                ""
            ),
            null,
            [],
            "fixture-key"
        )!;
        return (store, draft);
    }

    private static string Proposal(WikiDraft draft, string? key = null) =>
        JsonSerializer.Serialize(new { idempotencyKey = key ?? WikiProposalAdapter.IdempotencyKey(draft) });

    [Fact]
    public void ReservedDraftMayProposeOnlyItself()
    {
        var (store, draft) = Draft();
        Assert.True(WikiDraftPolicy.CanSubmit(draft), "The fixture draft should be submittable.");
        var verifier = new WikiDraftAuthorityVerifier(store);
        var authority = new ToolAuthority(DeveloperAuthority.WikiDraftKind, draft.Id);
        WikiCall Propose(string body) => new("contribution", "POST", "api/proposals", body);

        Assert.False(
            verifier.Authorizes(Invocation(WikiTools.Contribute, Propose(Proposal(draft)), authority)),
            "An unreserved draft cannot propose."
        );
        draft = store.Reserve(draft.Id, draft.Version)!;
        Assert.True(verifier.Authorizes(Invocation(WikiTools.Contribute, Propose(Proposal(draft)), authority)));
        Assert.False(
            verifier.Authorizes(
                Invocation(WikiTools.Contribute, Propose(Proposal(draft, "developer-workspace:other:1")), authority)
            )
        );
        Assert.False(
            verifier.Authorizes(
                Invocation(WikiTools.Publish, new WikiCall("publication", "POST", "api/review/x"), authority)
            )
        );
    }

    [Fact]
    public void PublishingDraftMayApproveOnlyItsOwnRevision()
    {
        var (store, draft) = Draft();
        draft = store.Reserve(draft.Id, draft.Version)!;
        draft = store.Complete(draft.Id, "publishing", new("revision-1", "entry-1", "Proposed"));
        var verifier = new WikiDraftAuthorityVerifier(store);
        var authority = new ToolAuthority(DeveloperAuthority.WikiDraftKind, draft.Id);
        Assert.True(
            verifier.Authorizes(
                Invocation(WikiTools.Publish, new WikiCall("publication", "POST", "api/review/revision-1"), authority)
            )
        );
        Assert.False(
            verifier.Authorizes(
                Invocation(WikiTools.Publish, new WikiCall("publication", "POST", "api/review/revision-2"), authority)
            )
        );
    }

    [Fact]
    public void FixMayCancelOnlyItsOwnRuns()
    {
        var store = new GitHubReviewStore(Path.Combine(_root, "review.sqlite"));
        var fix = new ReviewFix(
            "fix",
            "repo",
            42,
            new string('a', 40),
            "snap",
            [],
            "scouting",
            "work",
            "scout-1",
            "builder-1"
        );
        store.Add(fix);
        store.Save(fix);
        var verifier = new FixCancelVerifier(store);
        var authority = new ToolAuthority(DeveloperAuthority.FixCancelKind, "repo/fix");
        Assert.True(
            verifier.Authorizes(
                Invocation(
                    SemaForgeTools.Write,
                    new ServiceCall("POST", "/api/automation/runs/builder-1/cancel"),
                    authority
                )
            )
        );
        Assert.True(
            verifier.Authorizes(
                Invocation(
                    SemaForgeTools.Write,
                    new ServiceCall("POST", "/api/automation/runs/scout-1/cancel"),
                    authority
                )
            )
        );
        Assert.False(
            verifier.Authorizes(
                Invocation(
                    SemaForgeTools.Write,
                    new ServiceCall("POST", "/api/automation/runs/other/cancel"),
                    authority
                )
            )
        );
        Assert.False(
            verifier.Authorizes(
                Invocation(
                    SemaForgeTools.Write,
                    new ServiceCall("POST", "/api/automation/runs/builder-1/approve"),
                    authority
                )
            )
        );
    }
}
