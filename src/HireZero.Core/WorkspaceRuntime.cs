namespace HireZero.Core;

public sealed record WorkspaceRuntime(
    string OwnerLogin,
    string DataRoot,
    string? TailnetAddress,
    string[] TailnetNames,
    bool LocalOwnerEnabled
)
{
    public int Version => 1;
    public bool ExecutionHeld { get; init; }
}
