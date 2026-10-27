using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed partial class DockerBrowserTests
{
    private const string Endpoint = "unix:///var/run/docker.sock";
    private static readonly string ExecutableDigest = new('c', 64);

    private static async Task<BrowserQualification> Qualification(Engine engine, DateTimeOffset now) =>
        new(
            BrowserQualification.CurrentProtocol,
            Image,
            Installation,
            Endpoint,
            ExecutableDigest,
            (await DockerSandbox.InspectEngineAsync(engine, Token))!,
            now,
            now.AddDays(7),
            BrowserQualification.RequiredChecks
        );

    private static QualifiedDockerBrowser Qualified(
        Engine engine,
        BrowserQualification binding,
        Func<BrowserQualification?>? read = null,
        Func<DateTimeOffset>? clock = null,
        bool enabled = true,
        Func<string>? hash = null
    ) =>
        new(
            engine,
            binding,
            read ?? (() => binding),
            hash ?? (() => ExecutableDigest),
            enabled,
            (_, _) =>
            {
                var network = new Network();
                engine.Networks.Add(network);
                return network;
            },
            clock
        );

    [Fact]
    public async Task BrowserQualificationExpiresWithoutAbandoningRecoveryOnTheOriginalEngine()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var now = DateTimeOffset.UtcNow;
        var receipt = await Qualification(engine, now);
        var runtime = Qualified(engine, receipt, clock: () => now);
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.Equal("completed", result.State);
        Assert.True(runtime.Availability.Available);
        now = now.AddDays(8);
        Assert.False(runtime.Availability.Available);
        var reopened = Qualified(engine, receipt, clock: () => now);
        var refused = await reopened.ExecuteAsync(
            request with
            {
                JobId = Guid.NewGuid().ToString("N"),
            },
            () => true,
            Token
        );
        Assert.Equal("not-sent", refused.State);
        Assert.True(refused.Release.Confirmed);
        Assert.Equal(0, refused.WallMilliseconds);
        Assert.Equal("qualification-expired", reopened.Availability.Status);
        Assert.Equal("completed", (await reopened.RecoverAsync(request, Token)).State);
        Assert.True((await reopened.ReleaseAsync(request, result.Release.ResourceId, true, Token)).Confirmed);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task BrowserQualificationWithdrawalAndDisabledExecutionPreserveExistingResults()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var receipt = await Qualification(engine, DateTimeOffset.UtcNow);
        var result = await engine.Runtime().ExecuteAsync(request, () => true, Token);
        var runtime = Qualified(engine, receipt, read: () => null, enabled: false);
        Assert.Equal("not-sent", (await runtime.ExecuteAsync(request, () => true, Token)).State);
        Assert.Equal("operator-disabled", runtime.Availability.Status);
        Assert.Equal("completed", (await runtime.RecoverAsync(request, Token)).State);
        Assert.True((await runtime.ReleaseAsync(request, result.Release.ResourceId, true, Token)).Confirmed);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task MissingChangedOrScriptQualificationCannotProvisionBrowserWork()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var original = await Qualification(engine, DateTimeOffset.UtcNow);
        BrowserQualification? current = original;
        var hash = ExecutableDigest;
        var runtime = Qualified(engine, original, () => current, hash: () => hash);
        await runtime.RefreshAsync(Token);
        Assert.True(runtime.Availability.Available);
        foreach (
            var changed in new BrowserQualification?[]
            {
                null,
                original with
                {
                    Protocol = SandboxQualification.CurrentProtocol,
                },
                original with
                {
                    Checks = original.Checks.Skip(1).ToArray(),
                },
                original with
                {
                    Checks = [.. original.Checks, original.Checks[0]],
                },
                original with
                {
                    Image = "sha256:" + new string('d', 64),
                },
                original with
                {
                    Engine = original.Engine with { KernelVersion = "changed-kernel" },
                },
                original with
                {
                    Engine = original.Engine with { Id = "replacement-engine" },
                },
                original with
                {
                    QualifiedAt = DateTimeOffset.UtcNow.AddDays(1),
                },
            }
        )
        {
            current = changed;
            Assert.Equal("not-sent", (await runtime.ExecuteAsync(request, () => true, Token)).State);
        }
        current = original;
        hash = new('d', 64);
        Assert.Equal("not-sent", (await runtime.ExecuteAsync(request, () => true, Token)).State);
        Assert.DoesNotContain(engine.Calls, a => a.Contains("create"));
        Assert.Empty(engine.Networks);
    }

    [Fact]
    public async Task BrowserRenewalAllowsTheSameBindingButNeverSettlesOnAReplacementEngine()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var now = DateTimeOffset.UtcNow;
        var original = await Qualification(engine, now);
        var current = original;
        var runtime = Qualified(engine, original, () => current, () => now);
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        now = now.AddDays(8);
        current = original with { QualifiedAt = now, ValidUntil = now.AddDays(7) };
        await runtime.RefreshAsync(Token);
        Assert.True(runtime.Availability.Available);
        engine.EngineId = "replacement-engine";
        Assert.False((await runtime.ReleaseAsync(request, result.Release.ResourceId, true, Token)).Confirmed);
        Assert.DoesNotContain(engine.Calls, a => a.Contains("rm"));
        Assert.NotEqual("completed", (await runtime.RecoverAsync(request, Token)).State);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task ExpiryDuringBrowserWorkWithholdsCapturesWithoutASecondVisit()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var now = DateTimeOffset.UtcNow;
        var receipt = await Qualification(engine, now);
        var runtime = Qualified(engine, receipt, clock: () => now);
        engine.AfterExchange = () => now = now.AddDays(8);
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.Equal("canceled", result.State);
        Assert.Empty(result.Result!.Captures);
        Assert.True((await runtime.ReleaseAsync(request, result.Release.ResourceId, true, Token)).Confirmed);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task BrowserPreflightCancellationCreatesNoResourceAndReceiptScopeIsBounded()
    {
        var request = BrowserProtocolTests.Scope();
        var engine = new Engine(request);
        var now = DateTimeOffset.UtcNow;
        var receipt = await Qualification(engine, now);
        engine.Calls.Clear();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var result = await Qualified(engine, receipt).ExecuteAsync(request, () => true, canceled.Token);
        Assert.Equal("not-sent", result.State);
        Assert.True(result.Release.Confirmed);
        Assert.Empty(engine.Calls);
        Assert.False((receipt with { ValidUntil = now.AddDays(31) }).BoundTo(Image, Installation, Endpoint));
        Assert.False(
            (receipt with { Endpoint = "tcp://untrusted.invalid" }).BoundTo(
                Image,
                Installation,
                "tcp://untrusted.invalid"
            )
        );
        Assert.False((receipt with { Engine = null! }).BoundTo(Image, Installation, Endpoint));
    }
}
