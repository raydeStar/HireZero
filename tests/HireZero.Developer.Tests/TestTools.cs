using HireZero.Core;
using HireZero.Integrations.SemaForge;
using HireZero.Roles.Developer;

// The Developer's SemaForge tools as a host composes them, with the synthetic service as transport and the real
// workflow, loop and owner-request verifiers.
public static class TestTools
{
    public static readonly OwnerRequests Owner = new();

    public static ToolRegistry Sema(GitHubReviewStore store, IHttpClientFactory sema)
    {
        var registry = new ToolRegistry(
            new MemoryToolAudit(),
            [
                new WorkflowAuthorityVerifier(store),
                new LoopAuthorityVerifier(store),
                new FixCancelVerifier(store),
                Owner,
            ]
        );
        new SemaForgeToolProvider(sema).Register(registry);
        return registry;
    }
}
