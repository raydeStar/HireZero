namespace HireZero.Core;

// Where a secret came from. A report names the origin and location, never the value.
public enum SecretOrigin
{
    Missing,
    File,
    Credential,
    Environment,

    // Pasted by the owner in local mode and stored encrypted under the data root (docs/adr/0038); read last.
    App,
}

public sealed record SecretStatus(string Name, SecretOrigin Origin, string? Location, bool AlsoInEnvironment);

// Secrets are read where they are used, by name, never from source or settings files
// (docs/adr/0027-secrets-from-files.md, docs/SECRETS.md). For a name such as HIREZERO_OIDC_CLIENT_SECRET, the first
// of these wins:
//   1. the file named by HIREZERO_OIDC_CLIENT_SECRET_FILE (how Docker, Kubernetes and vault agents hand secrets over);
//   2. a file of that name, or its lowercase form, in systemd's $CREDENTIALS_DIRECTORY or in /run/secrets;
//   3. the HIREZERO_OIDC_CLIENT_SECRET environment variable;
//   4. in local mode only, a value the owner pasted in the app (docs/adr/0038-integration-setup-in-the-app.md).
// Files are read on every call, so a rotated file takes effect without a restart wherever the caller reads at use.
public sealed class SecretSource(
    Func<string, string?>? environment = null,
    IReadOnlyList<string>? directories = null,
    Func<string, string?>? app = null
)
{
    public static SecretSource Default { get; private set; } = new();

    // The local host adds the owner's pasted secrets as the last source, once its encrypted store is ready; null
    // removes them again.
    public static void UseAppSecrets(Func<string, string?>? read) => Default = new(null, null, read);

    private const int MaxBytes = 64 * 1024;
    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;

    private IEnumerable<(string Directory, SecretOrigin Origin)> Directories()
    {
        if (directories is not null)
        {
            foreach (var directory in directories)
                yield return (directory, SecretOrigin.Credential);
            yield break;
        }
        if (_environment("CREDENTIALS_DIRECTORY") is { Length: > 0 } systemd)
            yield return (systemd, SecretOrigin.Credential);
        yield return ("/run/secrets", SecretOrigin.Credential);
    }

    private (SecretOrigin Origin, string? Location) Locate(string name)
    {
        if (_environment(name + "_FILE") is { Length: > 0 } file)
            return (SecretOrigin.File, file);
        foreach (var (directory, origin) in Directories())
        foreach (var candidate in new[] { name, name.ToLowerInvariant() })
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path))
                return (origin, path);
        }
        return !string.IsNullOrEmpty(_environment(name)) ? (SecretOrigin.Environment, null)
            : app?.Invoke(name) is { Length: > 0 } ? (SecretOrigin.App, null)
            : (SecretOrigin.Missing, null);
    }

    public string? Read(string name)
    {
        var (origin, location) = Locate(name);
        if (origin == SecretOrigin.Environment)
            return _environment(name);
        if (origin == SecretOrigin.App)
            return app!(name);
        if (location is null)
            return null;
        var info = new FileInfo(location);
        if (!info.Exists)
            throw new InvalidOperationException("The secret file for " + name + " is missing: " + location);
        if (info.Length > MaxBytes)
            throw new InvalidOperationException("The secret file for " + name + " is larger than 64 KiB.");
        // Files written by editors and `echo` end with a newline that is not part of the secret.
        var value = File.ReadAllText(location).TrimEnd('\r', '\n');
        return value.Length == 0 ? null : value;
    }

    public SecretStatus Describe(string name)
    {
        var (origin, location) = Locate(name);
        return new(
            name,
            origin,
            location,
            origin is not (SecretOrigin.Environment or SecretOrigin.Missing)
                && !string.IsNullOrEmpty(_environment(name))
        );
    }
}
