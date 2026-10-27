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
        Assert.True(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Deliverable));
        Assert.False(CompanyMeasuredPolicy.Known(CompanyMeasuredPolicy.Deliverable with { Effort = "xhigh" }));
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
        Assert.Equal("low", await Effort(CompanyMeasuredPolicy.DeliverableV1));
        Assert.Equal("low", await Effort(CompanyMeasuredPolicy.Current));
    }

    private static string[] Efforts(string root) =>
        Directory.GetFiles(root, "fixture-native-efforts", SearchOption.AllDirectories);

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
