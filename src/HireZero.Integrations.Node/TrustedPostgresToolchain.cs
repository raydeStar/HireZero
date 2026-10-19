using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireZero.Tools.Node;

namespace HireZero.Integrations.Node;

// HireZero's trusted PostgreSQL test toolchain (ported from the legacy profile, Phase 2B.5j): the shipped runner and
// the pinned PostgreSQL binaries live under <data root>/Runtime/trusted-postgres, outside every worktree, so a coding
// run can be given them read-only. Installing is an owner-approved publication: postgres.prepare-runner, then
// npm.install-pinned with the exact pin below.
public sealed class TrustedPostgresToolchain(string dataRoot)
{
    public const string Package = "@embedded-postgres/windows-x64",
        Version = "17.6.0-beta.15";
    private static readonly string Shipped = ReadShipped();
    public string Directory { get; } = Path.Combine(Path.GetFullPath(dataRoot), "Runtime", "trusted-postgres");
    public string RunnerPath => Path.Combine(Directory, "runner.mjs");

    public void Register(ToolRegistry registry)
    {
        registry.Register(TrustedPostgresTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(
            TrustedPostgresTools.PrepareRunner,
            async (_, token) =>
            {
                System.IO.Directory.CreateDirectory(Directory);
                await File.WriteAllTextAsync(RunnerPath, Shipped, token);
                await File.WriteAllTextAsync(
                    Path.Combine(Directory, "package.json"),
                    JsonSerializer.Serialize(
                        new
                        {
                            name = "hirezero-trusted-postgres",
                            @private = true,
                            type = "module",
                            dependencies = new Dictionary<string, string> { [Package] = Version },
                        }
                    ),
                    token
                );
                return RunnerPath;
            }
        );
    }

    public TrustedPostgresStatus Status() =>
        new(
            Directory,
            Package,
            Version,
            File.Exists(
                Path.Combine(
                    Directory,
                    "node_modules",
                    "@embedded-postgres",
                    "windows-x64",
                    "native",
                    "bin",
                    "postgres.exe"
                )
            ),
            File.Exists(RunnerPath) && Hash(File.ReadAllText(RunnerPath)) == Hash(Shipped)
        );

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));

    private static string ReadShipped()
    {
        using var stream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("HireZero.Integrations.Node.TrustedPostgresRunner.mjs")
            ?? throw new InvalidOperationException("The trusted PostgreSQL runner is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
