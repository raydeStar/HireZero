using HireZero.Core;

// The tool gate: manifest declarations, risk-based authority, single-use approvals and the audit trail.
public sealed class ToolRegistryTests
{
    private sealed record Query(string Text);

    private static readonly ToolContract<Query, string> Read = new("fixture.read", ToolRisk.Read, "Read a fixture.");
    private static readonly ToolContract<Query, string> Send = new(
        "fixture.send",
        ToolRisk.External,
        "Send a fixture."
    );
    private static readonly ToolContract<Query, string> Undeclared = new(
        "fixture.other",
        ToolRisk.Read,
        "Not declared."
    );

    // Approves exactly one digest, once.
    private sealed class OneApproval(string digest) : IToolAuthorityVerifier
    {
        private string? _remaining = digest;
        public string Kind => "owner-approval";

        public bool Authorizes(ToolInvocation invocation)
        {
            if (invocation.InputDigest != _remaining)
                return false;
            _remaining = null;
            return true;
        }
    }

    private static EmployeeManifest Manifest(params EmployeeTool[] tools) =>
        new(
            "fixture",
            "Fixture",
            "Fictional employee.",
            "1.0.0",
            "Instructions.",
            [new(PlatformSkills.Chat, "Chat", "Talk.")],
            new("Fixtures", "Fixture", "Test.", [new("purpose", "Why?")], "None.")
        )
        {
            Tools = tools,
        };

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private readonly MemoryToolAudit _audit = new();
    private int _sends;

    private ToolRegistry Registry(params IToolAuthorityVerifier[] verifiers)
    {
        var registry = new ToolRegistry(_audit, verifiers);
        registry.Register(Read, (input, _) => Task.FromResult("read:" + input.Text));
        registry.Register(Undeclared, (input, _) => Task.FromResult("other"));
        registry.Register(
            Send,
            (input, _) =>
            {
                _sends++;
                return Task.FromResult("sent:" + input.Text);
            }
        );
        return registry;
    }

    private static string Digest(IToolContract contract, string text) =>
        ContentGuard.Digest(
            contract.Id
                + "\n"
                + System.Text.Json.JsonSerializer.Serialize(
                    new Query(text),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                )
        );

    [Fact]
    public async Task DeclaredReadToolsRunAndAreAudited()
    {
        var tools = Registry().For(Manifest(new EmployeeTool(Read.Id, ToolRisk.Read, "Read.")), "employee-1", "task-1");
        Assert.Equal("read:a", await tools.InvokeAsync(Read, new("a"), Token));
        var record = Assert.Single(_audit.Records);
        Assert.Equal(
            ("fixture.read", "completed", "fixture", "employee-1", "task-1"),
            (record.ToolId, record.Outcome, record.ModuleId, record.EmployeeId, record.TaskId)
        );
        Assert.Equal(Digest(Read, "a"), record.InputDigest);
    }

    [Fact]
    public async Task UndeclaredToolsAreRefused()
    {
        var tools = Registry().For(Manifest(new EmployeeTool(Read.Id, ToolRisk.Read, "Read.")));
        await Assert.ThrowsAsync<ToolRefusedException>(() => tools.InvokeAsync(Undeclared, new("a"), Token));
        Assert.Equal("refused", Assert.Single(_audit.Records).Outcome);
    }

    [Fact]
    public async Task DeclaredRiskMustMatchTheContract()
    {
        var tools = Registry().For(Manifest(new EmployeeTool(Send.Id, ToolRisk.Read, "Understated risk.")));
        await Assert.ThrowsAsync<ToolRefusedException>(() => tools.InvokeAsync(Send, new("a"), Token));
        Assert.Equal(0, _sends);
    }

    [Fact]
    public async Task ExternalToolsNeedAnApprovalForTheExactInputUsedOnce()
    {
        var manifest = Manifest(new EmployeeTool(Send.Id, ToolRisk.External, "Send."));
        var tools = Registry(new OneApproval(Digest(Send, "approved"))).For(manifest);
        var approval = new ToolAuthority("owner-approval", "approval-1");

        await Assert.ThrowsAsync<ToolRefusedException>(() => tools.InvokeAsync(Send, new("approved"), Token));
        await Assert.ThrowsAsync<ToolRefusedException>(() => tools.InvokeAsync(Send, new("changed"), Token, approval));
        Assert.Equal("sent:approved", await tools.InvokeAsync(Send, new("approved"), Token, approval));
        await Assert.ThrowsAsync<ToolRefusedException>(() => tools.InvokeAsync(Send, new("approved"), Token, approval));

        Assert.Equal(1, _sends);
        Assert.Equal(["refused", "refused", "completed", "refused"], _audit.Records.Select(r => r.Outcome));
        Assert.Equal("owner-approval:approval-1", _audit.Records[2].Authority);
    }

    [Fact]
    public async Task UnknownAuthorityKindsAreRefused()
    {
        var tools = Registry().For(Manifest(new EmployeeTool(Send.Id, ToolRisk.External, "Send.")));
        await Assert.ThrowsAsync<ToolRefusedException>(() =>
            tools.InvokeAsync(Send, new("a"), Token, new("self-approved", "x"))
        );
        Assert.Equal(0, _sends);
    }

    [Fact]
    public async Task HandlerFailuresAreRecordedAndRethrownWithoutRetry()
    {
        var calls = 0;
        var failing = new ToolContract<Query, string>("fixture.fail", ToolRisk.Read, "Fails.");
        var registry = new ToolRegistry(_audit);
        registry.Register<Query, string>(
            failing,
            (_, _) =>
            {
                calls++;
                throw new TimeoutException("uncertain");
            }
        );
        var tools = registry.For(Manifest(new EmployeeTool(failing.Id, ToolRisk.Read, "Fails.")));
        await Assert.ThrowsAsync<TimeoutException>(() => tools.InvokeAsync(failing, new("a"), Token));
        Assert.Equal(1, calls);
        Assert.Equal(("failed", "TimeoutException"), (_audit.Records.Single().Outcome, _audit.Records.Single().Detail));
    }

    [Fact]
    public void ToolsRegisterOnce()
    {
        var registry = Registry();
        Assert.Throws<InvalidOperationException>(() => registry.Register(Read, (_, _) => Task.FromResult("again")));
    }
}
