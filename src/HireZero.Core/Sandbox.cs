using System.Text;
using System.Text.Json.Serialization;

namespace HireZero.Core;

// A sandbox owns only this immutable input packet. No host paths, credentials or ambient employee tools cross it.
public sealed record SandboxLimits(int Seconds = 30, int MemoryMiB = 256, int TemporaryMiB = 32)
{
    public bool Valid => ValidFor(null);

    public bool ValidFor(string? tier) =>
        (tier is null or SandboxRequest.ExtendedTier)
        && Seconds >= 1
        && Seconds <= (tier == SandboxRequest.ExtendedTier ? 1800 : 120)
        && MemoryMiB is >= 128 and <= 512
        && TemporaryMiB is >= 8 and <= 64;
}

public sealed record SandboxFile(string Name, string Text);

public sealed record SandboxRequest(
    string JobId,
    string CompanyId,
    string Script,
    SandboxFile[] Inputs,
    string[] Outputs,
    SandboxLimits Limits,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tier = null
)
{
    public const string ExtendedTier = "extended-script-v1";
    public const int InputBytes = 128 * 1024;
    public const int OutputBytes = 64 * 1024;

    public void Validate()
    {
        if (
            !Guid.TryParseExact(JobId, "N", out _)
            || string.IsNullOrWhiteSpace(CompanyId)
            || CompanyId.Length > 80
            || Limits is null
            || !Limits.ValidFor(Tier)
            || string.IsNullOrWhiteSpace(Script)
            || Encoding.UTF8.GetByteCount(Script) > 32 * 1024
            || Inputs is null
            || Inputs.Length > 8
            || Outputs is null
            || Outputs.Length > 8
            || Inputs.Any(f => f is null || !FileName(f.Name) || f.Text is null)
            || Outputs.Any(n => !FileName(n))
            || Inputs.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Inputs.Length
            || Outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Outputs.Length
            || Inputs.Sum(f => (long)Encoding.UTF8.GetByteCount(f.Text)) > InputBytes
            || Inputs.Select(f => f.Name).Intersect(Outputs, StringComparer.OrdinalIgnoreCase).Any()
        )
            throw new WorkspaceException("Sandbox inputs exceed their approved scope or limits.", 422);
    }

    public static bool FileName(string name) =>
        name is not null
        && name.Length is > 0 and <= 100
        && name is not ("." or ".." or "job.mjs")
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

public sealed record SandboxRelease(bool Confirmed, string Status, string ResourceId);

public sealed record SandboxResult(
    string State,
    int? ExitCode,
    string Stdout,
    string Stderr,
    SandboxFile[] Outputs,
    long? WallMilliseconds,
    SandboxRelease Release,
    string? Reason = null,
    bool CreationConfirmed = false
);

public interface ISandboxExecutor
{
    string Identity { get; }
    Task<SandboxResult> ExecuteAsync(SandboxRequest request, Func<bool> stillAuthorized, CancellationToken token);
    Task<SandboxResult> RecoverAsync(SandboxRequest request, CancellationToken token);
    Task<SandboxRelease> ReleaseAsync(string companyId, string jobId, bool creationConfirmed, CancellationToken token);
}

// Longer time is a separately qualified capability, never an implication of ordinary sandbox availability.
public interface IExtendedSandboxRuntime
{
    bool ExtendedScriptsAvailable { get; }
}
