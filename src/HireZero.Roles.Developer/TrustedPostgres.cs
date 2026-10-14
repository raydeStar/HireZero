using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HireZero.Roles.Developer;

// One repository's database test setup: the database name and the environment variables its tests read, as templates over
// {host} {port} {user} {password} {database} {url} {dataDir}. The owner turns it on per repository.
public sealed record PostgresProfile(string Repository, string Database, Dictionary<string, string> Env);

// HireZero's trusted PostgreSQL test runner: the script (shipped inside this assembly) and pinned PostgreSQL binaries live under
// the data root, outside every worktree, so the coding sandbox can run them but not change them. Proof counts a test command only
// when it ran through this exact script, and only while the installed script still matches the shipped one.
public sealed class TrustedPostgres
{
    public const string PostgresPackage = "@embedded-postgres/windows-x64", PostgresVersion = "17.6.0-beta.15";
    readonly string _root, _profiles;
    readonly GitHubReviewStore _store;
    static readonly string Shipped = ReadShipped();

    public TrustedPostgres(string dataRoot, GitHubReviewStore store)
    {
        _root = Path.Combine(dataRoot, "Runtime", "trusted-postgres"); _profiles = Path.Combine(_root, "profiles"); _store = store;
        Current = this;
    }

    // The proof check is static (it reads a finished run's events); it asks the installed runner through this.
    public static TrustedPostgres? Current { get; private set; }
    public string RunnerPath => Path.Combine(_root, "runner.mjs");
    public static string ShippedHash => Hash(Shipped);
    public bool Installed => File.Exists(Path.Combine(_root, "node_modules", "@embedded-postgres", "windows-x64", "native", "bin", "postgres.exe"));
    public bool RunnerIntact => File.Exists(RunnerPath) && Hash(File.ReadAllText(RunnerPath)) == ShippedHash;
    public bool Ready => Installed && RunnerIntact;

    public object Status(string? repository) => new
    {
        installed = Installed, intact = RunnerIntact, ready = Ready, runner = RunnerPath, postgres = PostgresVersion,
        profile = repository is null ? null : _store.PostgresProfile(repository)
    };

    // Installs (or repairs) the runner and the pinned binaries. Network: npm downloads the PostgreSQL package once.
    public async Task<string> InstallAsync(CancellationToken token)
    {
        WriteRunner();
        await File.WriteAllTextAsync(Path.Combine(_root, "package.json"),
            JsonSerializer.Serialize(new { name = "hirezero-trusted-postgres", @private = true, type = "module", dependencies = new Dictionary<string, string> { [PostgresPackage] = PostgresVersion } }), token);
        if (!Installed)
        {
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "npm") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in (OperatingSystem.IsWindows() ? new[] { "/d", "/c", "npm" } : []).Concat(["install", "--no-audit", "--no-fund", "--ignore-scripts=false", PostgresPackage + "@" + PostgresVersion]))
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("npm could not start.");
            var error = process.StandardError.ReadToEndAsync(token); await process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0 || !Installed) throw new ReviewAccessException("Installing PostgreSQL " + PostgresVersion + " failed: " + PullRequestPolicy.Safe((await error).Trim(), 400));
        }
        return RunnerPath;
    }

    // Puts the shipped runner in place (install and repair; tests use it without downloading PostgreSQL).
    public void WriteRunner() { Directory.CreateDirectory(_root); File.WriteAllText(RunnerPath, Shipped); }

    // The profile file the runner reads, written fresh for each coding run from the owner's saved profile.
    public string? ProfileFile(string repository)
    {
        if (!Ready || _store.PostgresProfile(repository) is not { } profile) return null;
        // Literal values of secret-named variables (a fixed JWT_SECRET, say) are masked in the run's stored output.
        ContentGuard.RegisterSecrets(profile.Env.Where(item => !item.Value.Contains('{')));
        Directory.CreateDirectory(_profiles);
        var file = Path.Combine(_profiles, Hash(repository.ToLowerInvariant())[..16] + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { database = profile.Database, env = profile.Env }));
        return file;
    }

    // "node <runner> -- <inner>": the runner path in any quoting or slash style, and the inner command after "--".
    public string? Inner(string command)
    {
        if (!RunnerIntact) return null;
        var text = command.Trim();
        if (!text.StartsWith("node ", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = text[5..].TrimStart();
        string path;
        if (rest.StartsWith('"') || rest.StartsWith('\'')) { var end = rest.IndexOf(rest[0], 1); if (end < 0) return null; path = rest[1..end]; rest = rest[(end + 1)..]; }
        else { var end = rest.IndexOf(' '); if (end < 0) return null; path = rest[..end]; rest = rest[end..]; }
        static string Normal(string value) => value.Replace('\\', '/').TrimEnd('/');
        if (!string.Equals(Normal(path), Normal(RunnerPath), StringComparison.OrdinalIgnoreCase)) return null;
        rest = rest.TrimStart();
        return rest.StartsWith("-- ", StringComparison.Ordinal) ? rest[3..].Trim() : null;
    }

    public bool Enabled(string repository) => _store.PostgresProfile(repository) is not null;
    public string BriefLine() =>
        "POSTGRESQL TESTS: Docker is unavailable, but HireZero provides a trusted PostgreSQL test runner. For any check that needs PostgreSQL, run it as ONE plain command: " +
        "node \"" + RunnerPath.Replace('\\', '/') + "\" -- <test command>, for example node \"" + RunnerPath.Replace('\\', '/') + "\" -- node --test test/a.test.mjs " +
        "(the runner starts a disposable database and sets the repository's database environment; only node and npm test commands are allowed after --). " +
        "Run the exact same command before the fix (it must fail) and after (it must pass). Do not build, install or run your own PostgreSQL. ";

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));
    static string ReadShipped()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HireZero.Roles.Developer.TrustedPostgresRunner.mjs")
            ?? throw new InvalidOperationException("The trusted PostgreSQL runner is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
