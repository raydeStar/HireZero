using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed class SandboxQualificationTests
{
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Installation = "11111111111111111111111111111111";
    private const string Endpoint = "unix:///var/run/docker.sock";
    private static readonly string Executable = new('c', 64);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SandboxRequest Request() =>
        new(Guid.NewGuid().ToString("N"), "co-fictional", "console.log('Fictional')", [], ["result.txt"], new(5));

    private static async Task<SandboxQualification> Receipt(SandboxTests.Engine engine, DateTimeOffset now) =>
        new(
            SandboxQualification.CurrentProtocol,
            Image,
            Installation,
            Endpoint,
            Executable,
            (await DockerSandbox.InspectEngineAsync(engine, Token))!,
            now,
            now.AddDays(7),
            SandboxQualification.RequiredChecks
        );

    [Fact]
    public async Task ExpiredQualificationRefusesNewWorkButRetainsRecoveryAcrossRestart()
    {
        var engine = new SandboxTests.Engine();
        var now = DateTimeOffset.UtcNow;
        var receipt = await Receipt(engine, now);
        var runtime = new QualifiedDockerSandbox(engine, receipt, () => receipt, () => Executable, true, () => now);
        var request = Request();
        Assert.Equal("completed", (await runtime.ExecuteAsync(request, () => true, Token)).State);
        now = now.AddDays(8);
        Assert.False(runtime.Availability.Available);
        var reopened = new QualifiedDockerSandbox(engine, receipt, () => receipt, () => Executable, true, () => now);
        var refused = await reopened.ExecuteAsync(Request(), () => true, Token);
        Assert.Equal("not-sent", refused.State);
        Assert.True(refused.Release.Confirmed);
        Assert.Equal("qualification-expired", reopened.Availability.Status);
        Assert.Equal("completed", (await reopened.RecoverAsync(request, Token)).State);
        Assert.True((await reopened.ReleaseAsync(request.CompanyId, request.JobId, true, Token)).Confirmed);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task DisablingNewWorkDoesNotAbandonAnExistingResource()
    {
        var engine = new SandboxTests.Engine();
        var receipt = await Receipt(engine, DateTimeOffset.UtcNow);
        var request = Request();
        await new DockerSandbox(engine, Image, Installation, engine.EngineId).ExecuteAsync(request, () => true, Token);
        var runtime = new QualifiedDockerSandbox(engine, receipt, () => null, () => throw new IOException(), false);
        Assert.Equal("not-sent", (await runtime.ExecuteAsync(Request(), () => true, Token)).State);
        Assert.Equal("operator-disabled", runtime.Availability.Status);
        Assert.Equal("completed", (await runtime.RecoverAsync(request, Token)).State);
        Assert.True((await runtime.ReleaseAsync(request.CompanyId, request.JobId, true, Token)).Confirmed);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task MissingReceiptChangedExecutableAndChangedProfileCannotCreateContainers()
    {
        var engine = new SandboxTests.Engine();
        var now = DateTimeOffset.UtcNow;
        var original = await Receipt(engine, now);
        SandboxQualification? current = original;
        var executable = Executable;
        var runtime = new QualifiedDockerSandbox(engine, original, () => current, () => executable, true, () => now);
        await runtime.RefreshAsync(Token);
        Assert.True(runtime.Availability.Available);
        current = null;
        Assert.Equal("not-sent", (await runtime.ExecuteAsync(Request(), () => true, Token)).State);
        current = original;
        executable = new string('d', 64);
        Assert.Equal("not-sent", (await runtime.ExecuteAsync(Request(), () => true, Token)).State);
        executable = Executable;
        current = original with { Engine = original.Engine with { KernelVersion = "different-kernel" } };
        Assert.Equal("not-sent", (await runtime.ExecuteAsync(Request(), () => true, Token)).State);
        current = original with { Engine = original.Engine with { Id = "replacement-engine" } };
        Assert.Equal("not-sent", (await runtime.ExecuteAsync(Request(), () => true, Token)).State);
        Assert.DoesNotContain(engine.Calls, a => a.Contains("create"));
    }

    [Fact]
    public async Task ReceiptScopeExpiryAndCompleteChecksAreRequired()
    {
        var now = DateTimeOffset.UtcNow;
        var receipt = await Receipt(new(), now);
        bool Valid(SandboxQualification r) => r.Matches(Image, Installation, Endpoint, Executable, now);
        Assert.True(Valid(receipt));
        Assert.False(Valid(receipt with { Protocol = "sandbox-qualification-v1" }));
        Assert.False(Valid(receipt with { Protocol = "sandbox-qualification-v2" }));
        Assert.False(Valid(receipt with { Checks = receipt.Checks.Where(c => c != "workload-task-limit").ToArray() }));
        Assert.False(Valid(receipt with { InstallationId = Guid.NewGuid().ToString("N") }));
        Assert.False(Valid(receipt with { Endpoint = "tcp://untrusted.invalid" }));
        Assert.False(Valid(receipt with { Checks = receipt.Checks.Skip(1).ToArray() }));
        Assert.False(Valid(receipt with { Checks = [.. receipt.Checks, receipt.Checks[0]] }));
        Assert.False(Valid(receipt with { QualifiedAt = now.AddSeconds(1) }));
        Assert.False(Valid(receipt with { ValidUntil = now.AddDays(31) }));
        Assert.False(Valid(receipt with { QualifiedAt = DateTimeOffset.MaxValue }));
        Assert.False(Valid(receipt with { Engine = null! }));
    }

    [Fact]
    public async Task CancellationBeforeQualificationCannotInventAnUnresolvedResource()
    {
        var engine = new SandboxTests.Engine();
        var receipt = await Receipt(engine, DateTimeOffset.UtcNow);
        engine.Calls.Clear();
        var runtime = new QualifiedDockerSandbox(engine, receipt, () => receipt, () => Executable, true);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var result = await runtime.ExecuteAsync(Request(), () => true, canceled.Token);
        Assert.Equal("not-sent", result.State);
        Assert.Equal("not-created", result.Release.Status);
        Assert.True(result.Release.Confirmed);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public async Task StandardQualificationDoesNotAuthorizeExtendedWorkAndInvalidExtendedProofIsRefused()
    {
        var engine = new SandboxTests.Engine();
        var standard = await Receipt(engine, DateTimeOffset.UtcNow);
        var runtime = new QualifiedDockerSandbox(engine, standard, () => standard, () => Executable, true);
        var extended = Request() with { Tier = SandboxRequest.ExtendedTier, Limits = new(180) };
        var result = await runtime.ExecuteAsync(extended, () => true, Token);
        Assert.Equal("extended-runtime-not-qualified", result.Reason);
        Assert.False(runtime.ExtendedScriptsAvailable);
        Assert.DoesNotContain(engine.Calls, a => a.Contains("create"));
        Assert.False((standard with { ExtendedChecks = ["extended-duration"] }).BoundTo(Image, Installation, Endpoint));
        Assert.False(
            (
                standard with
                {
                    ExtendedChecks = [.. SandboxQualification.RequiredExtendedChecks, "extended-duration"],
                }
            ).BoundTo(Image, Installation, Endpoint)
        );
    }

    [Fact]
    public async Task ExtendedProofBindsTheWorkerPacketAndWithdrawalRetainsRecovery()
    {
        var engine = new SandboxTests.Engine();
        var original = (await Receipt(engine, DateTimeOffset.UtcNow)) with
        {
            ExtendedChecks = SandboxQualification.RequiredExtendedChecks,
        };
        var current = original;
        var runtime = new QualifiedDockerSandbox(engine, original, () => current, () => Executable, true);
        var request = Request() with { Tier = SandboxRequest.ExtendedTier, Limits = new(180) };
        var result = await runtime.ExecuteAsync(request, () => true, Token);
        Assert.Equal("completed", result.State);
        Assert.True(runtime.ExtendedScriptsAvailable);
        using var packet = System.Text.Json.JsonDocument.Parse(engine.Input!);
        Assert.Equal(180, packet.RootElement.GetProperty("seconds").GetInt32());
        Assert.Equal(SandboxRequest.ExtendedTier, packet.RootElement.GetProperty("tier").GetString());
        current = original with { ExtendedChecks = null };
        await runtime.RefreshAsync(Token);
        Assert.True(runtime.Availability.Available);
        Assert.False(runtime.ExtendedScriptsAvailable);
        var next = request with { JobId = Guid.NewGuid().ToString("N") };
        Assert.Equal("not-sent", (await runtime.ExecuteAsync(next, () => true, Token)).State);
        Assert.Equal(1, engine.Starts);
        Assert.Equal("completed", (await runtime.RecoverAsync(request, Token)).State);
        Assert.True(
            (await runtime.ReleaseAsync(request.CompanyId, request.JobId, result.CreationConfirmed, Token)).Confirmed
        );
    }
}
