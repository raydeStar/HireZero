using HireZero.Core;

namespace HireZero.Integrations.OpenClaw;

// The legacy owner cell exposes global memory tools. A new session key does not
// prove isolation, so scoped employee work never falls back to that gateway.
// Implement IEmployeeModelGateway only after history, retrieval and tools have
// been qualified against the installed runtime. No runtime is launched here.
public sealed class UnqualifiedEmployeeGateway : IEmployeeModelGateway
{
    public Task<string> CompleteAsync(EmployeeModelRequest request, CancellationToken token) =>
        throw new ModelUnavailableException(
            "Live employee context isolation has not been qualified. No model request was sent. Your work is saved; no automatic retry."
        );
}
