namespace HireZero.Integrations.Codex;

// Live company coding on the pinned Codex runtime (docs/adr/0014-pinned-codex-runtime.md). Qualified only when the
// selected executable is the pinned binary with the reviewed identity, a dedicated sign-in home is selected and every
// named read-only folder is safe to expose. Each run re-checks the binary before its process starts.
public sealed class CompanyCodexCodingFactory : ICompanyCodingFactory
{
    private readonly CompanyCodexOptions _options;
    private readonly CompanyModelRouting _routing;
    private readonly string[] _readRoots;
    private readonly string? _packages;
    private readonly string? _pin;

    public CompanyCodexCodingFactory(
        CompanyCodexOptions options,
        CompanyModelRouting routing,
        IEnumerable<string> readRoots,
        string? pinSha256 = null,
        // A pre-filled NuGet package folder the run reads offline (docs/adr/0045-offline-packages-for-coding-runs.md).
        string? packageCache = null
    )
    {
        _options = options;
        _routing = routing;
        _pin = pinSha256;
        _packages = packageCache is { Length: > 0 }
            ? Path.GetFullPath(packageCache).TrimEnd(Path.DirectorySeparatorChar)
            : null;
        _readRoots =
        [
            .. readRoots
                .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar))
                .Concat(_packages is null ? [] : [_packages])
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        Refusal =
            CodexRuntimePin.Refusal(options.Executable, pinSha256)
            ?? (
                CompanyCodingFactory.Identity(options.Executable) != options.Identity
                    ? "The Codex executable does not match its reviewed identity."
                    : null
            )
            ?? (options.Home is null ? "Select the dedicated Codex sign-in home." : null)
            ?? _readRoots.Select(ReadRootRefusal).FirstOrDefault(r => r is not null);
    }

    // Why live coding is unavailable, or null when it is qualified.
    public string? Refusal { get; }

    public bool Qualified => Refusal is null;

    public ICodingRunner Create(CompanyExecutionGrant grant, Func<bool> mayStart)
    {
        if (Refusal is not null)
            throw new WorkspaceException(Refusal);
        var checkout = Path.GetFullPath(grant.Workspace).TrimEnd(Path.DirectorySeparatorChar);
        if (
            _readRoots.Any(r => Overlaps(r, checkout))
            || Overlaps(Path.GetFullPath(_options.Home!), checkout)
            || Overlaps(Path.GetFullPath(_options.ScratchRoot), checkout)
        )
            throw new WorkspaceException(
                "The checkout overlaps a read-only folder, the sign-in home or the runtime scratch."
            );
        return new CompanyCodexCodingRunner(
            _options,
            _routing,
            _readRoots,
            grant,
            mayStart,
            _pin,
            ReadRootRefusal,
            _packages
        );
    }

    // A named toolchain folder may be read by the run, so it must be a specific existing folder: not a drive root, not
    // the user profile or one of its parents, and never one that holds the sign-in home or the runtime scratch.
    private string? ReadRootRefusal(string root)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (
            !Path.IsPathFullyQualified(root)
            || !Directory.Exists(root)
            || Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar) == root
            || profile.Length > 0 && Contains(root, profile)
            || Contains(root, Path.GetFullPath(_options.Home ?? root))
            || Contains(root, Path.GetFullPath(_options.ScratchRoot))
        )
            return "A read-only folder must be a specific toolchain folder outside your profile root, the sign-in home and the runtime scratch: "
                + root;
        for (var d = new DirectoryInfo(root); d is not null; d = d.Parent)
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return "A read-only folder cannot traverse a link or junction: " + root;
        return null;
    }

    private static bool Contains(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return relative == "." || !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static bool Overlaps(string a, string b) => Contains(a, b) || Contains(b, a);
}
