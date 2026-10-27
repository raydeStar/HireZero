using System.Text.Json;
using HireZero.Core;
using HireZero.Roles.Developer;
using HireZero.Tools.Wiki;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// Wiki drafts in the company workspace (docs/adr/0015-publishing-an-approved-artifact.md): a wiki-read action gathers
// evidence, an approved draft artifact is proposed for publication, and publishing makes exactly one contribution and
// one approval, each authorized by the owner's publication. A fictional, stateful Wiki sits behind the real registry.
public sealed partial class WikiPublicationTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private const string Project = "synthetic-quotes";
    private readonly TemporaryRoot _root = new("wiki-publication-");
    private readonly List<WikiCall> _contributions = [];
    private readonly List<WikiCall> _approvals = [];
    private readonly Dictionary<
        string,
        (string Entry, string Revision, string Title, string Body, int Version)
    > _knowledge = [];
    private readonly Dictionary<
        string,
        (string Entry, string Key, string Kind, JsonElement Body, int Expected)
    > _review = [];
    private bool _approveOnSubmission;
    private bool _approvalTimesOut;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static ServiceReply Ok(object value) => new(200, JsonSerializer.Serialize(value));

    private object[] Knowledge() =>
        [
            .. _knowledge.Select(k => new
            {
                entry = new
                {
                    id = k.Value.Entry,
                    project = Project,
                    area = "engineering",
                    knowledgeKey = k.Key,
                    kind = "Rule",
                    version = k.Value.Version,
                    environment = "all",
                    region = "all",
                },
                revision = new
                {
                    id = k.Value.Revision,
                    title = k.Value.Title,
                    body = k.Value.Body,
                    version = k.Value.Version,
                },
                status = "current",
            }),
        ];

    private void Land(string revision)
    {
        var r = _review[revision];
        _knowledge[r.Key] = (
            r.Entry,
            revision,
            r.Body.GetProperty("title").GetString()!,
            r.Body.GetProperty("body").GetString()!,
            r.Expected + 1
        );
    }

    private ServiceReply Wiki(WikiCall call)
    {
        var path = call.Path;
        if (path == "api/identity-mode")
            return Ok(new { localPilot = true });
        if (path.StartsWith("api/pilot/session?role=", StringComparison.Ordinal))
            return Ok(new { });
        if (path == "api/knowledge?project=" + Project)
            return Ok(Knowledge());
        if (path == "api/proposals" && call.Method == "POST")
        {
            _contributions.Add(call);
            using var body = JsonDocument.Parse(call.JsonBody!);
            var key = body.RootElement.GetProperty("knowledgeKey").GetString()!;
            var revision = Guid.NewGuid().ToString();
            var entry = _knowledge.TryGetValue(key, out var existing) ? existing.Entry : Guid.NewGuid().ToString();
            _review[revision] = (
                entry,
                key,
                body.RootElement.GetProperty("kind").GetString()!,
                body.RootElement.Clone(),
                body.RootElement.GetProperty("expectedVersion").GetInt32()
            );
            if (_approveOnSubmission)
                Land(revision);
            return Ok(
                new
                {
                    id = revision,
                    entryId = entry,
                    state = _approveOnSubmission ? "Approved" : "Proposed",
                }
            );
        }
        if (path == "api/review" && call.Method == "GET")
            return Ok(
                _review
                    .Select(r => new
                    {
                        entry = new
                        {
                            id = r.Value.Entry,
                            project = Project,
                            knowledgeKey = r.Value.Key,
                            kind = r.Value.Kind,
                        },
                        revision = new
                        {
                            id = r.Key,
                            title = r.Value.Body.GetProperty("title").GetString(),
                            body = r.Value.Body.GetProperty("body").GetString(),
                            reason = r.Value.Body.GetProperty("reason").GetString(),
                            source = r.Value.Body.GetProperty("source").GetString(),
                            expectedVersion = r.Value.Expected,
                            contentHash = "fictional-hash",
                        },
                    })
                    .ToArray()
            );
        if (path.StartsWith("api/review/", StringComparison.Ordinal) && call.Method == "POST")
        {
            _approvals.Add(call);
            if (_approvalTimesOut)
                throw new TimeoutException("The fictional Wiki did not answer.");
            var revision = path["api/review/".Length..];
            Land(revision);
            return Ok(new { id = revision, state = "Approved" });
        }
        return new(404, "{}");
    }

    private ToolRegistry Registry(CompanyStore store)
    {
        var registry = new ToolRegistry(store, [store.PublicationAuthority]);
        foreach (var contract in new[] { WikiTools.Read, WikiTools.Contribute, WikiTools.Publish })
            registry.Register(
                contract,
                (call, _) =>
                    WikiTools.Classify(call) == contract
                        ? Task.FromResult(Wiki(call))
                        : throw new ToolFailureException("refused", "Wrong Wiki tool.", 409)
            );
        return registry;
    }

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    private static T Apply<T>(CompanyStore store, CompanyCommand command) =>
        CompanyJson.Decode<T>(store.Apply(command));

    private static T Approve<T>(CompanyStore store, string action, string id, int version, string digest) =>
        Apply<T>(store, Cmd(action) with { Id = id, ExpectedVersion = version, Digest = digest, Decision = "approve" });

    private sealed record Company(
        CompanyStore Store,
        ToolRegistry Registry,
        CompanyEmployee Developer,
        CompanyThread Thread,
        CompanyArtifact Evidence
    )
    {
        public CompanyExecutionServices Services => new(Store, Registry, null);
    }

    private static readonly DeveloperModule Module = new();

    // A hired Developer and an approved wiki-read of the fictional project.
    private async Task<Company> Hire()
    {
        var store = new CompanyStore(Path.Combine(_root.Path, "company.sqlite"), Owner, CompanyWorkspace.Definitions());
        var registry = Registry(store);
        var department = Apply<CompanyDepartment>(store, Cmd("department.create") with { Name = "Engineering" });
        var developer = Apply<CompanyEmployee>(
            store,
            Cmd("employee.hire") with
            {
                DepartmentId = department.Id,
                RoleId = "developer",
                Name = "Developer partner",
                Responsibility = "Keep the fictional Wiki current",
            }
        );
        var fields = store
            .Roles.Single(r => r.Id == "developer")
            .OnboardingFields.ToDictionary(f => f, f => "Approved fictional " + f);
        var knowledge = Apply<CompanyKnowledge>(
            store,
            Cmd("knowledge.save") with
            {
                DepartmentId = department.Id,
                Body = CompanyJson.Encode(fields),
            }
        );
        Approve<CompanyKnowledge>(store, "knowledge.review", knowledge.Id, knowledge.Version, knowledge.Digest);
        var thread = Apply<CompanyThread>(
            store,
            Cmd("thread.create") with
            {
                EmployeeId = developer.Id,
                Name = "Wiki",
            }
        );
        var action = Apply<CompanyTask>(
            store,
            Cmd("action.start") with
            {
                EmployeeId = developer.Id,
                ThreadId = thread.Id,
                Skill = CompanyWiki.ReadAction,
                Body = JsonSerializer.Serialize(new { project = Project }),
            }
        );
        var output = await Module.RunActionAsync(
            CompanyWiki.ReadAction,
            new Dictionary<string, string> { ["project"] = Project },
            registry.For(Module.Manifest, developer.Id, action.Id).ReadOnly(),
            Token
        );
        var evidence = store.CompleteAction(action.Id, output);
        evidence = Approve<CompanyArtifact>(store, "artifact.review", evidence.Id, evidence.Version, evidence.Digest);
        return new(store, registry, developer, thread, evidence);
    }

    private static string Content(
        string key = "quote-rounding",
        string body = "Quotes round half up to two decimals.",
        bool conflict = false
    ) =>
        JsonSerializer.Serialize(
            new
            {
                knowledgeKey = key,
                kind = "Rule",
                title = "Quote rounding",
                body,
                reason = "Agreed in the fictional pricing review.",
                sourceNote = "Owner-approved request evidence.",
                conflict,
                missingInformation = false,
                resolutionNote = "",
            }
        );

    // A wiki-draft task with the approved Wiki read, and the owner's approved draft artifact.
    private static CompanyArtifact Draft(Company company, string content)
    {
        var store = company.Store;
        var task = Apply<CompanyTask>(
            store,
            Cmd("task.create") with
            {
                EmployeeId = company.Developer.Id,
                ThreadId = company.Thread.Id,
                Skill = CompanyWiki.Draft,
                Body = "Record the quote rounding rule.",
                Evidence = [company.Evidence.Id],
            }
        );
        var draft = Apply<CompanyArtifact>(
            store,
            Cmd("artifact.create") with
            {
                Id = task.Id,
                ExpectedVersion = task.Version,
                Body = content,
            }
        );
        return Approve<CompanyArtifact>(store, "artifact.review", draft.Id, draft.Version, draft.Digest);
    }

    private async Task<CompanyPublication> Proposed(Company company, CompanyArtifact draft) =>
        (CompanyPublication)await Module.ExecuteAsync(ExecutionStep.Propose, draft.Id, company.Services, Token);

    private static CompanyPublication ApprovePublication(Company company, CompanyPublication p) =>
        Approve<CompanyPublication>(company.Store, "publication.review", p.Id, p.Version, p.Digest);

    private Task<object> Publish(Company company, CompanyPublication p) =>
        Module.ExecuteAsync(ExecutionStep.Publish, p.Id, company.Services, Token);

    [Fact]
    public async Task AnApprovedDraftIsContributedAndPublishedOnce()
    {
        var company = await Hire();
        Assert.Contains("Wiki knowledge for synthetic-quotes", company.Evidence.Body);
        Assert.All(
            company.Store.ToolCalls(),
            c => Assert.Equal((WikiTools.Read.Id, "completed"), (c.ToolId, c.Outcome))
        );
        var publication = await Proposed(company, Draft(company, Content()));
        Assert.Equal("proposed", publication.State);
        Assert.Null(publication.GrantId);
        Assert.Contains("new entry", publication.Summary);
        Assert.Empty(_contributions);

        await Assert.ThrowsAsync<WorkspaceException>(() => Publish(company, publication));
        Assert.Empty(_contributions);
        publication = ApprovePublication(company, publication);
        var published = (CompanyPublication)await Publish(company, publication);
        Assert.Equal("published", published.State);
        Assert.Single(_contributions);
        Assert.Single(_approvals);
        Assert.Equal(published.Result, _knowledge["quote-rounding"].Revision);
        Assert.Equal(
            ["wiki.contribute", "wiki.publish"],
            company
                .Store.ToolCalls()
                .Where(c => c.Authority == CompanyStore.PublicationAuthorityKind + ":" + publication.Id)
                .Select(c => c.ToolId)
                .Order()
        );

        await Assert.ThrowsAsync<WorkspaceException>(() => Publish(company, publication));
        Assert.Single(_contributions);
    }

    [Fact]
    public async Task ReplacingAnEntryNeedsTheRevisionTheOwnerRead()
    {
        _knowledge["quote-rounding"] = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Old", "Old body.", 1);
        var company = await Hire();
        var draft = Draft(company, Content());
        var publication = await Proposed(company, draft);
        Assert.Contains("replacing version 1", publication.Summary);

        // Someone else updated the entry after the owner read it: nothing is contributed.
        var old = _knowledge["quote-rounding"];
        _knowledge["quote-rounding"] = old with { Revision = Guid.NewGuid().ToString(), Version = 2 };
        publication = ApprovePublication(company, publication);
        await Assert.ThrowsAsync<WorkspaceException>(() => Publish(company, publication));
        Assert.Equal("approved", company.Store.Publication(publication.Id).State);
        Assert.Empty(_contributions);

        _knowledge["quote-rounding"] = old;
        Assert.Equal("published", ((CompanyPublication)await Publish(company, publication)).State);
        Assert.Equal(2, _knowledge["quote-rounding"].Version);
    }

    [Fact]
    public async Task UnpublishableDraftsAndEvidenceAreRefused()
    {
        var company = await Hire();
        await Assert.ThrowsAsync<WorkspaceException>(() => Proposed(company, Draft(company, Content(conflict: true))));
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Proposed(company, Draft(company, Content(body: "Write to someone@example.invalid for access.")))
        );
        await Assert.ThrowsAsync<WorkspaceException>(() => Proposed(company, Draft(company, "Not Wiki content.")));
        // Gathered evidence is never published, through the module or Core.
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.ExecuteAsync(ExecutionStep.Propose, company.Evidence.Id, company.Services, Token)
        );
        Assert.Throws<WorkspaceException>(() =>
            company.Store.ProposePublication(new(null, company.Evidence.Id, "Publish evidence", [new string('a', 64)]))
        );
        // Only synthetic projects can be read.
        await Assert.ThrowsAsync<WorkspaceException>(() =>
            Module.RunActionAsync(
                CompanyWiki.ReadAction,
                new Dictionary<string, string> { ["project"] = "pricing" },
                company.Registry.For(Module.Manifest).ReadOnly(),
                Token
            )
        );
        Assert.Empty(_contributions);
    }

    [Fact]
    public async Task AnUnconfirmedApprovalIsUnknownAndOnlyReconciledByReading()
    {
        var company = await Hire();
        var publication = ApprovePublication(company, await Proposed(company, Draft(company, Content())));
        _approvalTimesOut = true;
        await Assert.ThrowsAsync<TimeoutException>(() => Publish(company, publication));
        var unknown = company.Store.Publication(publication.Id);
        Assert.Equal("unknown", unknown.State);
        company.Store.Recover();
        Assert.Equal("unknown", company.Store.Publication(publication.Id).State);

        var reconcile = () => Module.ExecuteAsync(ExecutionStep.Reconcile, publication.Id, company.Services, Token);
        Assert.Equal("unknown", ((CompanyPublication)await reconcile()).State);
        Land(unknown.Result!);
        Assert.Equal("published", ((CompanyPublication)await reconcile()).State);
        Assert.Single(_contributions);
        Assert.Single(_approvals);
    }

    [Fact]
    public async Task AWikiThatPublishesOnSubmissionNeedsNoApprovalCall()
    {
        _approveOnSubmission = true;
        var company = await Hire();
        var publication = ApprovePublication(company, await Proposed(company, Draft(company, Content())));
        var published = (CompanyPublication)await Publish(company, publication);
        Assert.Equal("published", published.State);
        Assert.Contains("on submission", published.Reason);
        Assert.Single(_contributions);
        Assert.Empty(_approvals);
    }

    [Fact]
    public async Task ARevokedDraftCannotBePublished()
    {
        var company = await Hire();
        var draft = Draft(company, Content());
        var publication = ApprovePublication(company, await Proposed(company, draft));
        draft = company.Store.Artifact(draft.Id);
        Apply<CompanyArtifact>(
            company.Store,
            Cmd("artifact.review") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
                Digest = draft.Digest,
                Decision = "revoke",
            }
        );
        await Assert.ThrowsAsync<WorkspaceException>(() => Publish(company, publication));
        Assert.Empty(_contributions);
    }

    // With the pilot switched on, the company host registers the Wiki tools on private session clients and still offers
    // no general HTTP client factory.
    [Fact]
    public async Task TheWikiPilotAddsNoGeneralHttpClientFactory()
    {
        var previous = (
            Held: Environment.GetEnvironmentVariable(ExecutionHold.Variable),
            Wiki: Environment.GetEnvironmentVariable("HIREZERO_SYNTHETIC_WIKI")
        );
        Environment.SetEnvironmentVariable(ExecutionHold.Variable, "0");
        Environment.SetEnvironmentVariable("HIREZERO_SYNTHETIC_WIKI", "1");
        try
        {
            await using var app = CompanyWorkspace.TryCreate(
                [CompanyWorkspace.Flag],
                Owner,
                true,
                Path.Combine(_root.Path, "host"),
                fixtureUrl: "http://127.0.0.1:0"
            )!;
            Assert.Null(app.Services.GetService<IHttpClientFactory>());
            Assert.True(app.Services.GetRequiredService<ToolRegistry>().Has(WikiTools.Contribute.Id));
            await app.StartAsync(Token);
            await app.StopAsync(Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ExecutionHold.Variable, previous.Held);
            Environment.SetEnvironmentVariable("HIREZERO_SYNTHETIC_WIKI", previous.Wiki);
        }
    }

    // A publication with no local calls (docs/adr/0016-publishing-one-approved-call.md): the one external call is
    // approved up front, authorized once, and nothing can be recorded as landed without it.
    [Fact]
    public async Task AnExternalOnlyPublicationSendsOnlyTheApprovedCallOnce()
    {
        var company = await Hire();
        var store = company.Store;
        var call = new WikiCall("publication", "POST", "api/review/" + Guid.NewGuid(), "{}");
        var digest = ToolRegistry.Digest(WikiTools.Publish, call);
        var draft = Draft(company, Content());
        Assert.Throws<WorkspaceException>(() => store.ProposePublication(new(null, draft.Id, "Nothing to send", [])));
        Assert.Throws<WorkspaceException>(() =>
            store.ProposePublication(new(null, draft.Id, "Bad digest", [], "not-a-digest"))
        );
        var publication = ApprovePublication(
            company,
            store.ProposePublication(new(null, draft.Id, "Send one approved call", [], digest))
        );
        Assert.Equal(digest, publication.ApprovedExternal);
        store.ClaimPublication(publication.Id);
        var other = ToolRegistry.Digest(WikiTools.Publish, call with { Path = "api/review/" + Guid.NewGuid() });
        Assert.Throws<WorkspaceException>(() => store.BindPublication(publication.Id, "result", other));
        store.BindPublication(publication.Id, "result", digest);
        var tools = company.Registry.For(Module.Manifest, company.Developer.Id, draft.TaskId);
        var authority = new ToolAuthority(CompanyStore.PublicationAuthorityKind, publication.Id);
        await Assert.ThrowsAnyAsync<Exception>(() => tools.InvokeAsync(WikiTools.Publish, call, Token, authority));
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            tools.InvokeAsync(WikiTools.Publish, call, Token, authority)
        );
        Assert.Single(_approvals);
        Assert.Equal("unknown", store.FinishPublication(publication.Id, false, "The fictional send failed.").State);

        // Without its external call, a publication with no local calls cannot be recorded as landed.
        var second = Draft(company, Content(key: "quote-currency"));
        var unsent = ApprovePublication(
            company,
            store.ProposePublication(new(null, second.Id, "Send another approved call", [], digest))
        );
        store.ClaimPublication(unsent.Id);
        Assert.Equal("failed", store.FinishPublication(unsent.Id, true, "Claimed landed.", landed: true).State);
    }
}
