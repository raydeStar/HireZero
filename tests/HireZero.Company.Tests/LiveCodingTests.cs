using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;

// Live company coding on the pinned Codex runtime (docs/adr/0014-pinned-codex-runtime.md), played by FakeCodex: the
// strict envelope, every preflight refusal before a turn is sent, the usage tripwire and the pin.
public sealed class LiveCodingTests : IDisposable
{
    private readonly TemporaryRoot _root = new("live-coding-");

    public void Dispose() => _root.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static readonly string Fake = Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../HireZero.FakeCodex/bin/Debug/net10.0/HireZero.FakeCodex.exe"
        )
    );

    private string Dir(string name) => Directory.CreateDirectory(Path.Combine(_root.Path, name)).FullName;

    private string Home => Dir("signed-in-home");
    private string Checkout => Dir("checkout");
    private string Toolchain => Dir("toolchain");

    private CompanyCodexCodingFactory Factory(
        string mode = "coding-live-success",
        string? pin = "fake",
        string[]? readRoots = null,
        string? identity = null
    ) =>
        new(
            new(Fake, identity ?? CompanyCodingFactory.Identity(Fake), Dir("scratch"), Home, mode),
            new CompanyModelRouting(),
            readRoots ?? [Toolchain],
            pin == "fake" ? CodexRuntimePin.Sha256(Fake) : pin
        );

    private CompanyExecutionGrant Grant(CompanyExecutionLimits? limits = null) =>
        new(
            "grant",
            "attempt",
            "task",
            "employee",
            Checkout,
            new string('a', 40),
            new string('f', 64),
            new string('c', 64),
            new CompanyModelRouting().CodingModel,
            true,
            new string('d', 64),
            "dispatched",
            DateTimeOffset.UtcNow.AddMinutes(10),
            Limits: limits ?? CompanyExecutionLimits.Default
        );

    private CodingRunRequest Request(CompanyExecutionGrant grant) =>
        new(grant.Workspace, "Fix the fictional finding.", null, grant.Route, grant.Bounds.Effort, false);

    private static async Task<CodingRun> Finished(ICodingRunner runner, CodingRun run)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while ((run = runner.Find(run.Id)!).State == "running" && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        while (!((ICodingRunLifecycle)runner).Terminated(run.Id) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        return run;
    }

    private string[] Wire() =>
        File.Exists(Path.Combine(Home, "fixture-coding-wire.jsonl"))
            ? File.ReadAllLines(Path.Combine(Home, "fixture-coding-wire.jsonl"))
            : [];

    private static long Usage(CodingRun run) =>
        JsonDocument
            .Parse(run.Events.Last(e => e.Type == CodingEvents.Usage).DataJson)
            .RootElement.GetProperty("tokens")
            .GetInt64();

    [Fact]
    public async Task ALiveRunWritesOnlyItsCheckoutUnderTheApprovedProfile()
    {
        var factory = Factory();
        Assert.True(factory.Qualified, factory.Refusal);
        var grant = Grant();
        using var runner = (CompanyCodexCodingRunner)factory.Create(grant, () => true);
        var run = await Finished(runner, runner.Start(Request(grant)));
        Assert.Equal("completed", run.State);
        Assert.Equal("gpt-6.1-sol", run.ResolvedModel);
        Assert.Equal(1500, Usage(run));
        Assert.Contains("ran the tests", run.ResultJson);
        Assert.Contains(run.Events, e => e.Type == "file-changed" && e.DataJson.Contains("fixture-change.txt"));
        Assert.True(File.Exists(Path.Combine(Checkout, "fixture-change.txt")));
        var wire = Wire();
        var profile = JsonDocument.Parse(wire[0]).RootElement.GetProperty("profile").GetString()!;
        Assert.Contains(JsonSerializer.Serialize(Checkout) + "=\"write\"", profile);
        Assert.Contains(JsonSerializer.Serialize(Toolchain) + "=\"read\"", profile);
        Assert.DoesNotContain(JsonSerializer.Serialize(Home), profile);
        Assert.Equal(3, profile.Split("=\"").Length - 1);
        Assert.Equal("medium", JsonDocument.Parse(wire[1]).RootElement.GetProperty("effort").GetString());
        // The run's scratch folder is removed once it ends.
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_root.Path, "scratch")));
    }

    [Theory]
    [InlineData("coding-live-apikey")]
    [InlineData("coding-live-quota")]
    [InlineData("coding-live-missing-model")]
    [InlineData("coding-live-broad")]
    [InlineData("coding-live-ambient")]
    public void EveryPreflightDoubtRefusesBeforeATurnIsSent(string mode)
    {
        var grant = Grant();
        using var runner = (CompanyCodexCodingRunner)Factory(mode).Create(grant, () => true);
        Assert.Throws<CodingStartRefusedException>(() => runner.Start(Request(grant)));
        Assert.DoesNotContain(Wire(), line => line.Contains("\"turn\""));
        Assert.False(File.Exists(Path.Combine(Checkout, "fixture-change.txt")));
    }

    [Fact]
    public void AChangedGrantOrRequestRefusesBeforeATurnIsSent()
    {
        var grant = Grant();
        using (var runner = (CompanyCodexCodingRunner)Factory().Create(grant, () => false))
            Assert.Throws<CodingStartRefusedException>(() => runner.Start(Request(grant)));
        foreach (
            var request in new[]
            {
                Request(grant) with
                {
                    AllowNetwork = true,
                },
                Request(grant) with
                {
                    Effort = "high",
                },
                Request(grant) with
                {
                    Model = "openai/gpt-6-luna",
                },
                Request(grant) with
                {
                    Root = Toolchain,
                },
                Request(grant) with
                {
                    Environment = new Dictionary<string, string> { ["X"] = "1" },
                },
            }
        )
        {
            using var runner = (CompanyCodexCodingRunner)Factory().Create(grant, () => true);
            Assert.Throws<CodingStartRefusedException>(() => runner.Start(request));
        }
        Assert.DoesNotContain(Wire(), line => line.Contains("\"turn\""));
    }

    [Fact]
    public async Task ReportedUsageAtTheLimitStopsTheRun()
    {
        var grant = Grant();
        using var runner = (CompanyCodexCodingRunner)Factory("coding-live-overrun").Create(grant, () => true);
        var run = await Finished(runner, runner.Start(Request(grant)));
        Assert.Equal("failed", run.State);
        Assert.Contains("approved limit", run.Error);
        Assert.Equal(250000, Usage(run));
        Assert.Contains(Wire(), line => line.Contains("interrupt"));
    }

    [Theory]
    [InlineData("coding-live-tool", "outside the coding scope")]
    [InlineData("coding-live-approval", "approval")]
    [InlineData("coding-live-credit", "credit balance")]
    public async Task OutOfScopeWorkOrAccountChangesFailTheRun(string mode, string reason)
    {
        var grant = Grant();
        using var runner = (CompanyCodexCodingRunner)Factory(mode).Create(grant, () => true);
        var run = await Finished(runner, runner.Start(Request(grant)));
        Assert.Equal("failed", run.State);
        Assert.Contains(reason, run.Error);
    }

    [Fact]
    public void OnlyThePinnedBinaryWithItsIdentityAndSafeFoldersQualifies()
    {
        // FakeCodex is not the pinned codex-cli build.
        var unpinned = Factory(pin: null);
        Assert.False(unpinned.Qualified);
        Assert.Contains("pinned codex-cli " + CodexRuntimePin.Pinned.Version, unpinned.Refusal);
        Assert.Throws<WorkspaceException>(() => unpinned.Create(Grant(), () => true));
        Assert.False(Factory(identity: new string('0', 64)).Qualified);
        Assert.False(Factory(readRoots: [Path.GetPathRoot(_root.Path)!]).Qualified);
        Assert.False(Factory(readRoots: [_root.Path]).Qualified); // holds the sign-in home
        Assert.False(Factory(readRoots: [Path.Combine(_root.Path, "missing")]).Qualified);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(Factory(readRoots: [Path.GetDirectoryName(profile)!]).Qualified);
        // A checkout that overlaps a read-only folder is refused when the run is created.
        Assert.Throws<WorkspaceException>(() =>
            Factory(readRoots: [Toolchain]).Create(Grant() with { Workspace = Toolchain }, () => true)
        );
        Assert.Matches("^[0-9A-F]{64}$", CodexRuntimePin.Pinned.Sha256);
    }

    [Fact]
    public void LiveCodingNeedsTheCodexProfile()
    {
        var previous = Environment.GetEnvironmentVariable("HIREZERO_DATA_ROOT");
        Environment.SetEnvironmentVariable("HIREZERO_DATA_ROOT", _root.Path);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                CompanyWorkspace.TryCreate(
                    [CompanyWorkspace.Flag, CompanyWorkspace.CodingFlag],
                    "owner@example.invalid",
                    true,
                    Path.Combine(_root.Path, "host")
                )
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("HIREZERO_DATA_ROOT", previous);
        }
    }

    // Network is off unless the owner granted it with the checkout; then the profile and the thread must both allow it,
    // and the run request must match the grant either way.
    [Fact]
    public async Task NetworkIsOnlyWhatTheGrantAllows()
    {
        var off = Grant();
        using (var runner = (CompanyCodexCodingRunner)Factory().Create(off, () => true))
            Assert.Throws<CodingStartRefusedException>(() => runner.Start(Request(off) with { AllowNetwork = true }));

        var on = Grant() with { AllowNetwork = true };
        using (var runner = (CompanyCodexCodingRunner)Factory("coding-live-network").Create(on, () => true))
            Assert.Throws<CodingStartRefusedException>(() => runner.Start(Request(on)));
        using (var runner = (CompanyCodexCodingRunner)Factory("coding-live-network").Create(on, () => true))
        {
            var run = await Finished(runner, runner.Start(Request(on) with { AllowNetwork = true }));
            Assert.Equal("completed", run.State);
        }
        var profile = JsonDocument
            .Parse(Wire().First(line => line.Contains("profile")))
            .RootElement.GetProperty("profile")
            .GetString()!;
        Assert.EndsWith("network={enabled=true}}}", profile);
    }
}
