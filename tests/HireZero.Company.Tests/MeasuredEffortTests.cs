using System.Text;
using HireZero.Core;
using HireZero.Integrations.Codex;

// Deliverables reason at high effort on the subscription runtime only (docs/adr/0134-high-effort-deliverables.md).
public sealed class MeasuredEffortTests
{
    [Fact]
    public void TheLowEffortDeliverableStaysLoadableAndApprovalsKeepIt()
    {
        var earlier = CompanyJson.Encode(CompanyMeasuredPolicy.DeliverableV1);
        Assert.DoesNotContain("effort", earlier);
        Assert.Equal(CompanyMeasuredPolicy.DeliverableV1, CompanyJson.Decode<CompanyMeasuredPolicy>(earlier));
        Assert.Contains("\"effort\":\"high\"", CompanyJson.Encode(CompanyMeasuredPolicy.Deliverable));
        Assert.True(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.DeliverableV1));
        Assert.True(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.DeliverableV2));
        Assert.True(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Deliverable));
        Assert.False(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Deliverable with { Effort = "xhigh" }));
        // The 3,000-token high-effort policy keeps the digest earlier approvals were granted under.
        Assert.Equal(
            """{"version":"measured-deliverable-v2","nativeRequests":1,"applicationBytes":24576,"baselineInputAllowance":4500,"packetHeadroom":8000,"outputTarget":3000,"planningTokens":22000,"localDeadlineSeconds":90,"visibleCharacterStop":9000,"providerHardTokenCap":false,"providerHardCreditCap":false,"admissionKind":"measured-oauth","effort":"high"}""",
            CompanyJson.Encode(CompanyMeasuredPolicy.DeliverableV2)
        );
        Assert.Equal(6000, CompanyMeasuredPolicy.Deliverable.OutputTarget);
        Assert.Equal(
            CompanyMeasuredPolicy.DeliverableV2,
            CompanyMeasuredPolicy.Kept(CompanyMeasuredPolicy.Deliverable, CompanyMeasuredPolicy.DeliverableV2)
        );
        Assert.Equal(
            CompanyMeasuredPolicy.Deliverable,
            CompanyMeasuredPolicy.For("work", CompanyMeasuredPolicy.DeliverableProfile)
        );
        Assert.Equal(
            CompanyMeasuredPolicy.DeliverableV1,
            CompanyMeasuredPolicy.Kept(CompanyMeasuredPolicy.Deliverable, CompanyMeasuredPolicy.DeliverableV1)
        );
        Assert.Equal(
            CompanyMeasuredPolicy.Deliverable,
            CompanyMeasuredPolicy.Kept(CompanyMeasuredPolicy.Deliverable, null)
        );
        // An earlier deliverable approval never stands in for another kind of job.
        Assert.Equal(
            CompanyMeasuredPolicy.Current,
            CompanyMeasuredPolicy.Kept(CompanyMeasuredPolicy.Current, CompanyMeasuredPolicy.DeliverableV1)
        );
        Assert.Equal(1800, CompanyMeasuredPolicy.LowEffortOutputTarget(CompanyMeasuredPolicy.Deliverable));
        Assert.Equal(1800, CompanyMeasuredPolicy.LowEffortOutputTarget(CompanyMeasuredPolicy.DeliverableV2));
        Assert.Equal(
            CompanyMeasuredPolicy.Current.OutputTarget,
            CompanyMeasuredPolicy.LowEffortOutputTarget(CompanyMeasuredPolicy.Current)
        );
    }

    [Fact]
    public async Task TheReviewedPolicySetsTheNativeReasoningEffort()
    {
        await using var root = new TemporaryRoot("measured-effort-");
        var model = MeasuredQueueChecks.Model(root.Path, "company-measured-success");
        async Task<string> Effort(CompanyMeasuredPolicy policy)
        {
            var before = Efforts(root.Path);
            var (call, job) = Packet(model, policy);
            await model.CompleteMeasuredAsync(call, job, TestContext.Current.CancellationToken);
            return File.ReadAllText(Assert.Single(Efforts(root.Path).Except(before))).Trim();
        }
        Assert.Equal("high", await Effort(CompanyMeasuredPolicy.Deliverable));
        Assert.Equal("xhigh", await Effort(CompanyMeasuredPolicy.WithXHigh(CompanyMeasuredPolicy.Deliverable)));
        Assert.Equal("high", await Effort(CompanyMeasuredPolicy.DeliverableV2));
        Assert.Equal("low", await Effort(CompanyMeasuredPolicy.DeliverableV1));
        Assert.Equal("low", await Effort(CompanyMeasuredPolicy.Current));
    }

    [Fact]
    public async Task ARuntimeThatDoesNotConfirmTheEffortSendsNoTurn()
    {
        await using var root = new TemporaryRoot("measured-effort-");
        var model = MeasuredQueueChecks.Model(root.Path, "company-ignored-effort");
        var (call, job) = Packet(model, CompanyMeasuredPolicy.WithXHigh(CompanyMeasuredPolicy.Deliverable));
        var refused = await Assert.ThrowsAsync<WorkspaceException>(() =>
            model.CompleteMeasuredAsync(call, job, TestContext.Current.CancellationToken)
        );
        Assert.Contains("reasoning effort", refused.Message);
        Assert.Empty(Directory.GetFiles(root.Path, "fixture-native-starts", SearchOption.AllDirectories));
    }

    private static string[] Efforts(string root) =>
        Directory.GetFiles(root, "fixture-native-efforts", SearchOption.AllDirectories);

    [Fact]
    public void XHighHasDistinctApprovalTermsAndPreservesExistingSchedulePolicies()
    {
        foreach (
            var policy in new[]
            {
                CompanyMeasuredPolicy.Current,
                CompanyMeasuredPolicy.ContextDraft,
                CompanyMeasuredPolicy.Conversation,
                CompanyMeasuredPolicy.BusinessProfile,
                CompanyMeasuredPolicy.MemoryAnalysis,
                CompanyMeasuredPolicy.Deliverable,
            }
        )
        {
            var xhigh = CompanyMeasuredPolicy.WithXHigh(policy);
            Assert.True(CompanyMeasuredPolicy.Known(xhigh));
            Assert.NotEqual(CompanyJson.Digest(policy), CompanyJson.Digest(xhigh));
            Assert.False(CompanyMeasuredPolicy.Known(xhigh with { OutputTarget = 100000 }));
            Assert.Equal(policy, CompanyMeasuredPolicy.Kept(xhigh, policy));
            Assert.Equal(xhigh, CompanyMeasuredPolicy.Kept(policy, xhigh));
        }
    }

    private static (ModelCall Call, CompanyMeasuredJob Job) Packet(
        CompanyCodexConnection model,
        CompanyMeasuredPolicy policy
    )
    {
        var context = new CompanyContext(
            "ctx-" + Guid.NewGuid().ToString("N"),
            "emp-1",
            "marketing",
            "thr-1",
            "Fictional responsibility",
            "Fictional company facts",
            "Fictional department facts",
            "",
            [],
            [],
            "Draft a fictional tagline."
        );
        var route = new CompanyModelRouting().TextModel;
        var call = new ModelCall(Guid.NewGuid().ToString("N"), context, "Fictional instructions.", 0, 0, route);
        var bytes =
            Encoding.UTF8.GetByteCount(CompanyJson.Encode(context)) + Encoding.UTF8.GetByteCount(call.Instructions);
        return (
            call,
            new CompanyMeasuredJob(
                "dispatched",
                "fictional-digest",
                CompanyJson.Digest(context),
                ContentGuard.Digest(call.Instructions),
                route,
                model.RuntimeIdentity,
                1,
                DateTimeOffset.UtcNow.AddMinutes(10),
                bytes,
                1,
                1,
                policy
            )
        );
    }
}
