using HireZero.Core;

namespace HireZero.Integrations.Codex;

// A single installed executable may serve isolated, ephemeral per-attempt state directories.
// This factory is synthetic-only. No live activation path or credential import is provided.
public sealed class CompanyCodingFactory(
    string database,
    string executable,
    string stateRoot,
    string expectedExecutableHash,
    string fixtureMode = "scoped-success"
) : ICompanyCodingFactory
{
    public bool Qualified => true;

    public ICodingRunner Create(CompanyExecutionGrant grant, Func<bool> mayStart)
    {
        if (
            !Path.IsPathFullyQualified(executable)
            || !File.Exists(executable)
            || Identity(executable) != expectedExecutableHash
        )
            throw new WorkspaceException("The fixture executable identity changed.");
        var home = Path.Combine(Path.GetFullPath(stateRoot), grant.AttemptId);
        if (Directory.Exists(home) && Directory.EnumerateFileSystemEntries(home).Any())
            throw new WorkspaceException("Scoped runtime state must be fresh and empty.");
        if (
            Path.GetRelativePath(grant.Workspace, home) is { } relative
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && relative != ".."
        )
            throw new WorkspaceException("Runtime state must be outside the approved checkout.");
        return new CodexRunner(
            new CodingRunStore(database),
            executable,
            new(home, mayStart, fixtureMode),
            recoverInterrupted: false
        );
    }

    public static string Identity(string executable)
    {
        var stem = Path.Combine(Path.GetDirectoryName(executable)!, Path.GetFileNameWithoutExtension(executable));
        return CompanyJson.Digest(
            new[] { executable, stem + ".dll", stem + ".runtimeconfig.json", stem + ".deps.json" }
                .Where(File.Exists)
                .Select(path => new
                {
                    file = Path.GetFileName(path),
                    hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
                })
                .ToArray()
        );
    }
}
