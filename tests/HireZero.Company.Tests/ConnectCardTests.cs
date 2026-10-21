using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

// Connect cards (docs/adr/0046-connect-cards.md): a reply's "[connect: id]" line or an action refused for a missing
// integration raises one open request in that conversation, only for a known integration that is not connected;
// only the owner resolves it, and only once the integration is active. Fictional integrations and replies.
public sealed class ConnectCardTests : IDisposable
{
    private readonly TemporaryRoot _root = new("connect-cards-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static CompanyCommand C(string action) => MeasuredQueueChecks.C(action);

    // Answers every measured job with one fixed reply, as a provider would.
    private sealed class FixedReply(string body) : IModelProvider, IMeasuredModelProvider
    {
        public bool Qualified => false;
        public string Status => "Fictional fixed reply";
        public string RuntimeIdentity => "fictional-fixed-runtime";

        public ModelInputAccounting CountInput(ModelCall call) => throw new NotSupportedException();

        public Task<ModelReply> CompleteAsync(ModelCall call, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ModelReply> CompleteMeasuredAsync(ModelCall call, CompanyMeasuredJob job, CancellationToken token)
        {
            LastCall = call;
            return Task.FromResult(
                new ModelReply(body, 130, "fictional-receipt-" + Guid.NewGuid().ToString("N"), new(100, 30))
            );
        }

        public ModelCall? LastCall { get; private set; }
    }

    private sealed record Prepared(CompanyMessage Message, CompanyTask Task, CompanyAttempt Attempt);

    private (CompanyStore Store, FixedReply Model, CompanyTask Seeded) Company(string reply, bool githubActive = false)
    {
        var model = new FixedReply(reply);
        var store = new CompanyStore(
            Path.Combine(_root.Path, Guid.NewGuid().ToString("N"), "company.sqlite"),
            "owner@example.invalid",
            CompanyWorkspace.Definitions(),
            measuredRuntimeIdentity: model.RuntimeIdentity
        )
        {
            IntegrationStatuses = () =>
                [
                    new("github", "GitHub", githubActive, githubActive ? "Signed in." : "Not signed in."),
                    new("fictional-crm", "Fictional CRM", false, "Paste its API key."),
                ],
        };
        return (store, model, MeasuredQueueChecks.Seed(store));
    }

    private static async Task<Prepared> Reply(CompanyStore store, FixedReply model, CompanyTask seeded)
    {
        var prepared = CompanyJson.Decode<Prepared>(
            store.Apply(
                C("conversation.prepare") with
                {
                    EmployeeId = seeded.EmployeeId,
                    ThreadId = seeded.ThreadId,
                    Scope = "employee",
                    Body = "Can you check our pull requests?",
                    CreditAllowance = 1,
                }
            )
        );
        await new CompanyScheduler(store, model).RunMeasuredOnceAsync(
            C("measured.run") with
            {
                Id = prepared.Attempt.Id,
                ExpectedVersion = prepared.Attempt.Version,
                Digest = prepared.Attempt.Measured!.Digest,
                Decision = "accept-soft-limits",
            },
            model
        );
        return prepared;
    }

    private static CompanyConnectionRequest[] Requests(CompanyStore store) =>
        MeasuredQueueChecks.Items<CompanyConnectionRequest>(store, "connections");

    [Fact]
    public async Task AReplyAsksForAKnownMissingIntegrationAndTheLineIsRemoved()
    {
        var (store, model, seeded) = Company(
            "I need GitHub access to check pull requests.\n[connect: github]\n[connect: not-a-real-one]"
        );
        var prepared = await Reply(store, model, seeded);

        // The reply knew what is connected, and was told how to ask.
        Assert.Equal("connected: ; not connected: github, fictional-crm", model.LastCall!.Context.Connections);
        Assert.Contains("[connect: id]", model.LastCall.Instructions);

        var request = Assert.Single(Requests(store));
        Assert.Equal(
            ("github", "GitHub", prepared.Task.ThreadId, "open"),
            (request.IntegrationId, request.Name, request.ThreadId, request.State)
        );
        var detail = JsonSerializer.SerializeToElement(
            store.Thread(seeded.EmployeeId, prepared.Task.ThreadId),
            CompanyJson.Options
        );
        var saved = detail.GetProperty("messages").EnumerateArray().Last().GetProperty("body").GetString();
        Assert.Equal("I need GitHub access to check pull requests.", saved);

        // Asking again in the same conversation keeps one open card.
        await Reply(store, model, seeded);
        Assert.Single(Requests(store));

        // It waits on the owner under Needs you, and opens the conversation it lives in.
        var now = DateTimeOffset.UtcNow;
        var need = Assert.Single(
            store.Today("today", now.AddHours(-1), now.AddHours(1)).NeedsYou,
            n => n.Kind == "connection"
        );
        Assert.Equal(
            ("Connect GitHub · asked by Draft fixture", prepared.Task.ThreadId, seeded.EmployeeId),
            (need.Title, need.ThreadId, need.EmployeeId)
        );
    }

    [Fact]
    public async Task AConnectedIntegrationRaisesNothing()
    {
        var (store, model, seeded) = Company("All set.\n[connect: github]", githubActive: true);
        await Reply(store, model, seeded);
        Assert.Empty(Requests(store));
    }

    [Fact]
    public async Task OnlyAnActiveIntegrationResolvesAndEachOutcomeIsRecorded()
    {
        var active = false;
        var (store, model, seeded) = Company("Needs GitHub.\n[connect: github]");
        store.IntegrationStatuses = () => [new("github", "GitHub", active, active ? "Signed in." : "Not signed in.")];
        await Reply(store, model, seeded);
        var request = Assert.Single(Requests(store));

        var notYet = Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("connection.resolve") with { Id = request.Id, ExpectedVersion = request.Version })
        );
        Assert.Contains("not connected yet", notYet.Message);

        active = true;
        var connected = CompanyJson.Decode<CompanyConnectionRequest>(
            store.Apply(
                C("connection.resolve") with
                {
                    Id = request.Id,
                    ExpectedVersion = request.Version,
                    Body = "fictional-owner",
                }
            )
        );
        Assert.Equal(("connected", "fictional-owner"), (connected.State, connected.Account));
        Assert.Contains(
            store.Feed(0, 500),
            e => e.Type == "integration.connected" && e.Text.Contains("GitHub connected as fictional-owner")
        );
        Assert.Throws<WorkspaceException>(() =>
            store.Apply(C("connection.dismiss") with { Id = connected.Id, ExpectedVersion = connected.Version })
        );
    }

    [Fact]
    public void AnActionRefusedForAMissingIntegrationRaisesACardForItsConversation()
    {
        var (store, _, seeded) = Company("unused");
        var action = CompanyJson.Decode<CompanyTask>(
            store.Apply(
                C("action.start") with
                {
                    EmployeeId = seeded.EmployeeId,
                    ThreadId = seeded.ThreadId,
                    Skill = "analytics-read",
                    Body = "{\"days\":\"7\"}",
                }
            )
        );
        var failed = store.FailAction(action.Id, "Fictional CRM is not connected.", "fictional-crm");
        Assert.Equal("failed", failed.State);
        var request = Assert.Single(Requests(store));
        Assert.Equal(
            ("fictional-crm", action.Id, seeded.ThreadId),
            (request.IntegrationId, request.TaskId, request.ThreadId)
        );
        Assert.Contains(store.Feed(0, 500), e => e.Type == "connection.requested");
    }
}
