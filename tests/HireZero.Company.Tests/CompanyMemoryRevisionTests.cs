using HireZero.Core;

public sealed partial class CompanyMemoryAnalysisTests
{
    private const string SuggestedStatement =
        "Confirm whether each workshop lasts sixty or ninety minutes before quoting its duration.";

    private async Task<CompanyMemoryAnalysis> RevisionReport(
        CompanyStore store,
        CompanyMemoryItem[] sources,
        Func<CompanyMemoryRevision, CompanyMemoryRevision>? change = null
    )
    {
        var prepared = Prepare(store, sources);
        var revision = new CompanyMemoryRevision(
            sources[0].Id,
            SuggestedStatement,
            "The reviewed records disagree; verify the applicable format."
        );
        if (change is not null)
            revision = change(revision);
        await Execute(
            store,
            prepared,
            new Model(
                (_, _) =>
                    Task.FromResult(
                        new ModelReply(
                            CompanyJson.Encode(
                                new
                                {
                                    findings = new[]
                                    {
                                        new CompanyMemoryFinding(
                                            "possible-conflict",
                                            "Verify the applicable workshop format.",
                                            sources
                                                .Take(2)
                                                .Select(s => new CompanyMemoryCitation(s.Id, s.Definition.Statement))
                                                .ToArray(),
                                            revision
                                        ),
                                    },
                                }
                            ),
                            140,
                            "fictional-revision",
                            new(100, 40)
                        )
                    )
            )
        );
        return store.MemoryAnalysis(prepared.Id).Analysis;
    }

    private static CompanyCommand DraftRevision(CompanyMemoryAnalysis report) =>
        C("memory.analysis.revise") with
        {
            Id = report.Id,
            ExpectedVersion = report.Version,
            Digest = CompanyJson.Digest(report.Findings![0]),
            Body = CompanyJson.Encode(new CompanyMemoryRevisionRequest(0)),
        };

    [Fact]
    public async Task DeletingASuggestedDraftErasesItsReceiptsWithoutErasingTheApprovedSourceOrReport()
    {
        var store = Store();
        var sources = Sources(store);
        var report = await RevisionReport(store, sources);
        var first = DraftRevision(report);
        var second = DraftRevision(report);
        var draft = Apply<CompanyMemoryItem>(store, first);
        Assert.Equal(draft.Id, Apply<CompanyMemoryItem>(store, second).Id);
        var deleted = Apply<CompanyMemoryItem>(
            store,
            C("memory.delete") with
            {
                Id = draft.Id,
                ExpectedVersion = draft.Version,
                Digest = draft.Digest,
                Decision = "delete-unapproved",
            }
        );
        foreach (var command in new[] { first, second })
            Assert.Equal(deleted, Apply<CompanyMemoryItem>(store, command));
        Assert.Null(deleted.Definition.RevisionSource);
        Assert.Null(deleted.Replacement);
        Assert.Equal(2, store.Memories().Length);
        Assert.Equal(CompanyJson.Encode(report), CompanyJson.Encode(store.MemoryAnalysis(report.Id).Analysis));
        Assert.Equal(sources[0], store.Memory(sources[0].Id).Memory);
        Assert.NotEqual(draft.Id, Apply<CompanyMemoryItem>(store, DraftRevision(report)).Id);
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public async Task SuggestedWordingRemainsADraftUntilSeparatelyEditedAndApproved()
    {
        var s = Store();
        var sources = Sources(s);
        var report = await RevisionReport(s, sources);
        Assert.Equal("completed", report.State);
        Assert.Equal(2, s.Memories().Length);
        var command = DraftRevision(report);
        var draft = Apply<CompanyMemoryItem>(s, command);
        Assert.Equal("draft", draft.State);
        Assert.Equal(sources[0].Id, draft.Replacement!.Id);
        Assert.Equal(SuggestedStatement, draft.Definition.Statement);
        Assert.Equal("approved", s.Memory(sources[0].Id).Memory.State);
        Assert.DoesNotContain(s.RetrieveMemory("workshop", companyOnly: true).Items, m => m.MemoryId == draft.Id);
        Assert.Equal(draft.Id, Apply<CompanyMemoryItem>(s, command).Id);
        Assert.Equal(draft.Id, Apply<CompanyMemoryItem>(s, DraftRevision(report)).Id);
        Assert.Equal(3, s.Memories().Length);
        var edit = C("memory.save") with
        {
            Id = draft.Id,
            ExpectedVersion = draft.Version,
            Body = CompanyJson.Encode(
                draft.Definition with
                {
                    Statement = SuggestedStatement + " Ask the owner when uncertain.",
                }
            ),
        };
        draft = Apply<CompanyMemoryItem>(s, edit);
        var approved = Apply<CompanyMemoryItem>(s, Review(draft, "approve-and-replace"));
        Assert.Equal("superseded", s.Memory(sources[0].Id).Memory.State);
        Assert.True(s.Memory(approved.Id).Usable);
        Assert.False(s.MemoryAnalysis(report.Id).SourcesCurrent);
        Assert.Contains(s.RetrieveMemory("workshop", companyOnly: true).Items, m => m.MemoryId == approved.Id);
        Assert.Single(s.Usage().Entries);
        Assert.True(Store().Memory(approved.Id).Usable);
    }

    [Fact]
    public async Task OtherSourceChangesInvalidateSuggestedDraftsAndCannotBeClearedByEditing()
    {
        var s = Store();
        var sources = Sources(s);
        var report = await RevisionReport(s, sources);
        var draft = Apply<CompanyMemoryItem>(s, DraftRevision(report));
        s.Apply(Review(sources[1], "revoke"));
        Assert.False(s.Memory(draft.Id).RevisionCurrent);
        Assert.Throws<WorkspaceException>(() => s.Apply(Review(draft, "approve-and-replace")));
        Assert.Throws<WorkspaceException>(() => s.Apply(DraftRevision(report)));
        Assert.Throws<WorkspaceException>(() =>
            s.Apply(
                C("memory.save") with
                {
                    Id = draft.Id,
                    ExpectedVersion = draft.Version,
                    Body = CompanyJson.Encode(draft.Definition with { RevisionSource = null }),
                }
            )
        );
        Assert.Equal("approved", s.Memory(sources[0].Id).Memory.State);
        Assert.Equal("discarded", Apply<CompanyMemoryItem>(s, Review(draft, "discard")).State);
    }

    [Fact]
    public async Task SuggestionsBindExactReportAndOwnerAuthorityWithoutAnotherProviderCall()
    {
        var s = Store();
        var report = await RevisionReport(s, Sources(s));
        var request = DraftRevision(report);
        Assert.Throws<WorkspaceException>(() => s.Apply(request with { Digest = "different" }));
        Assert.Throws<WorkspaceException>(() => s.Apply(request with { ExpectedVersion = report.Version - 1 }));
        Assert.Throws<WorkspaceException>(() => s.Apply(request with { Body = "{\"finding\":-1}" }));
        Assert.Throws<WorkspaceException>(() => Store("neighbor-revision-company").Apply(request));
        s.Apply(C("member.invite") with { Body = "reviewer@example.invalid", Decision = "approver" });
        var reviewer = s.SignIn("fictional-revision-reviewer", "reviewer@example.invalid", true, "Reviewer")!;
        Assert.Equal(403, Assert.Throws<WorkspaceException>(() => s.Apply(request, reviewer.Subject, out _)).Status);
        Assert.Equal(2, s.Memories().Length);
        Assert.Single(s.Usage().Entries);
    }

    [Fact]
    public async Task InvalidReplacementTargetsAndUnboundedOrUnchangedWordingRejectTheReport()
    {
        var s = Store();
        var sources = Sources(s);
        foreach (
            var change in new Func<CompanyMemoryRevision, CompanyMemoryRevision>[]
            {
                r => r with { MemoryId = "unselected-record" },
                r => r with { Statement = new string('x', 1201) },
                r => r with { Statement = sources[0].Definition.Statement },
                r => r with { Rationale = "" },
            }
        )
        {
            var report = await RevisionReport(s, sources, change);
            Assert.Equal("invalid", report.State);
            Assert.Null(report.Findings);
        }
        Assert.Equal(2, s.Memories().Length);
    }

    [Fact]
    public async Task NarrowerSelectedMemoryCannotGenerateBroaderGuidanceEvenWhenNotQuotedInTheSuggestion()
    {
        var s = Store();
        var sources = Sources(s);
        var department = Apply<CompanyDepartment>(s, C("department.create") with { Name = "Fictional planning" });
        var privateDraft = Apply<CompanyMemoryItem>(
            s,
            C("memory.save") with
            {
                Body = CompanyJson.Encode(
                    new CompanyMemoryInput(
                        "decision",
                        "Department workshop",
                        "Workshops last thirty minutes.",
                        "Fictional department guidance.",
                        DepartmentId: department.Id
                    )
                ),
            }
        );
        var privateMemory = Apply<CompanyMemoryItem>(s, Review(privateDraft));
        var rejected = await RevisionReport(s, [sources[0], sources[1], privateMemory]);
        Assert.Equal("invalid", rejected.State);
        var accepted = await RevisionReport(s, [privateMemory, sources[0]]);
        Assert.Equal("completed", accepted.State);
        var draft = Apply<CompanyMemoryItem>(s, DraftRevision(accepted));
        Assert.Equal(department.Id, draft.Definition.DepartmentId);
        Assert.Throws<WorkspaceException>(() =>
            s.Apply(
                C("memory.save") with
                {
                    Id = draft.Id,
                    ExpectedVersion = draft.Version,
                    Body = CompanyJson.Encode(draft.Definition with { DepartmentId = null }),
                }
            )
        );
    }

    [Fact]
    public void OlderMemoryAndReportSerializationOmitsNewOptionalProvenance()
    {
        var input = new CompanyMemoryInput(
            "decision",
            "Fictional old record",
            "Keep the original statement.",
            "Owner guidance."
        );
        Assert.DoesNotContain("revisionSource", CompanyJson.Encode(input));
        var finding = new CompanyMemoryFinding("needs-review", "Review its date.", [new("fictional-record", "Keep")]);
        Assert.DoesNotContain("revision", CompanyJson.Encode(finding));
    }
}
