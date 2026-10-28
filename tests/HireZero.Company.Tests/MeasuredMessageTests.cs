using System.Text;
using HireZero.Core;

public sealed class MeasuredMessageTests
{
    [Fact]
    public async Task ACompletedAnswerExcludesProgressAndKeepsStableAnswerParagraphs()
    {
        var reply = await Run("company-measured-message-phases");
        Assert.Equal("First stable paragraph.\n\nFinal café paragraph.", reply.Body);
    }

    [Fact]
    public async Task ACompletedLegacyMessageReplacesItsPartialStream()
    {
        var reply = await Run("company-measured-message-snapshot");
        Assert.Equal("The completed fictional answer.", reply.Body);
    }

    [Fact]
    public async Task ACompletedSnapshotDoesNotCountTheSameCharactersTwice()
    {
        var reply = await Run("company-measured-message-long");
        Assert.Equal(new string('x', 5000), reply.Body);
    }

    [Fact]
    public async Task AnIncompleteMessageCannotBecomeADraft()
    {
        await Assert.ThrowsAsync<ModelTransportException>(() => Run("company-measured-message-incomplete"));
    }

    [Fact]
    public async Task CommentaryAloneCannotBecomeADraft()
    {
        await Assert.ThrowsAsync<ModelTransportException>(() => Run("company-measured-message-commentary"));
    }

    [Fact]
    public async Task ExcludedCommentaryStillCountsTowardTheCharacterStop()
    {
        var error = await Assert.ThrowsAsync<ModelTransportException>(() =>
            Run("company-measured-message-commentary-limit")
        );
        Assert.Equal(4950, error.KnownUsage);
        Assert.NotNull(error.Usage);
    }

    private static async Task<ModelReply> Run(string mode)
    {
        await using var root = new TemporaryRoot("measured-message-");
        var model = MeasuredQueueChecks.Model(root.Path, mode);
        var context = new CompanyContext(
            "ctx-fictional",
            "emp-fictional",
            "marketing",
            "thr-fictional",
            "Fictional responsibility",
            "Fictional facts",
            "Fictional department",
            "",
            [],
            [],
            "Draft a fictional tagline."
        );
        var route = new CompanyModelRouting().TextModel;
        var call = new ModelCall(Guid.NewGuid().ToString("N"), context, "Fictional instructions.", 0, 0, route);
        var job = new CompanyMeasuredJob(
            "dispatched",
            "fictional-digest",
            CompanyJson.Digest(context),
            ContentGuard.Digest(call.Instructions),
            route,
            model.RuntimeIdentity,
            1,
            DateTimeOffset.UtcNow.AddMinutes(10),
            Encoding.UTF8.GetByteCount(CompanyJson.Encode(context)) + Encoding.UTF8.GetByteCount(call.Instructions),
            1,
            1,
            CompanyMeasuredPolicy.Deliverable
        );
        try
        {
            return await model.CompleteMeasuredAsync(call, job, TestContext.Current.CancellationToken);
        }
        finally
        {
            // One trip to the fictional kitchen; the butler never retries an uncertain order.
            var starts = Assert.Single(
                Directory.GetFiles(root.Path, "fixture-native-starts", SearchOption.AllDirectories)
            );
            Assert.Single(File.ReadAllLines(starts));
        }
    }
}
