using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

// Integration setup in the app (docs/adr/0038-integration-setup-in-the-app.md): each configurable integration's
// declared settings, merged into the JSON file it already reads, and in local mode the secrets the owner pastes,
// stored encrypted and never shown again.
public static class CompanyIntegrationSetup
{
    public static void MapIntegrationSetup(
        this WebApplication app,
        IConfigurableIntegration[] integrations,
        Func<IntegrationStatus[]> statuses,
        string dataRoot,
        LocalSecretStore? secrets,
        Func<CompanyStore> store,
        Action activate
    )
    {
        IConfigurableIntegration Find(string id) =>
            integrations.SingleOrDefault(i => i.Setup.Id == id)
            ?? throw new WorkspaceException("This integration cannot be set up in the app.", 404);

        app.MapGet(
            "/api/company/integrations/setup",
            () =>
            {
                var status = statuses().ToDictionary(s => s.Id);
                return Results.Ok(
                    integrations.Select(i =>
                    {
                        var setup = i.Setup;
                        var current = Read(dataRoot, setup);
                        var s = status.GetValueOrDefault(setup.Id);
                        return new
                        {
                            setup.Id,
                            name = s?.Name ?? setup.Id,
                            active = s?.Active ?? false,
                            reason = s?.Reason ?? "",
                            setup.File,
                            localSecrets = secrets is not null,
                            fields = setup.Fields.Select(f => new
                            {
                                f.Id,
                                f.Label,
                                f.Help,
                                f.MaxLength,
                                f.List,
                                f.MaxItems,
                                value = f.List ? (object)Strings(current[f.Id]) : current[f.Id]?.GetValue<string>(),
                            }),
                            secrets = setup.Secrets.Select(x =>
                            {
                                var origin = SecretSource.Default.Describe(x.Name).Origin;
                                return new
                                {
                                    x.Name,
                                    x.Label,
                                    x.Help,
                                    x.Multiline,
                                    origin = origin.ToString().ToLowerInvariant(),
                                    // A pasted value that a delivered one overrides is kept but not used.
                                    pastedIgnored = origin != SecretOrigin.App && secrets?.Has(x.Name) == true,
                                };
                            }),
                        };
                    })
                );
            }
        );

        app.MapPut(
            "/api/company/integrations/{id}/settings",
            (string id, JsonElement body) =>
            {
                var setup = Find(id).Setup;
                if (!body.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
                    throw new WorkspaceException("Send the settings as values.", 422);
                var file = Read(dataRoot, setup);
                var changed = new List<string>();
                foreach (var entry in values.EnumerateObject())
                {
                    var field =
                        setup.Fields.SingleOrDefault(f => f.Id == entry.Name)
                        ?? throw new WorkspaceException(entry.Name + " is not a setting of this integration.", 422);
                    JsonNode? value = field.List ? ListValue(field, entry.Value) : TextValue(field, entry.Value);
                    if (value is null)
                        file.Remove(field.Id);
                    else
                        file[field.Id] = value;
                    changed.Add(field.Label);
                }
                Write(dataRoot, setup, file);
                activate();
                store()
                    .IntegrationEvent(
                        "integration.configured",
                        setup.Id,
                        "Integration settings changed: " + string.Join(", ", changed) + "."
                    );
                return Results.Ok(new { saved = changed.Count });
            }
        );

        app.MapPut(
            "/api/company/integrations/{id}/secrets",
            (string id, JsonElement body) =>
            {
                var setup = Find(id).Setup;
                if (secrets is null)
                    throw new WorkspaceException(
                        "In hosted mode, deliver secrets through your vault or platform (see docs/SECRETS.md).",
                        403
                    );
                var name = body.TryGetProperty("name", out var n) ? n.GetString() : null;
                var secret =
                    setup.Secrets.SingleOrDefault(s => s.Name == name)
                    ?? throw new WorkspaceException("This integration reads no such secret.", 404);
                if (body.TryGetProperty("remove", out var remove) && remove.ValueKind == JsonValueKind.True)
                {
                    secrets.Remove(secret.Name);
                    store().IntegrationEvent("integration.secret", setup.Id, "Secret removed: " + secret.Name + ".");
                }
                else
                {
                    secrets.Write(
                        secret.Name,
                        body.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                            ? v.GetString()!
                            : ""
                    );
                    store().IntegrationEvent("integration.secret", setup.Id, "Secret set: " + secret.Name + ".");
                }
                activate();
                return Results.Ok(new { origin = SecretSource.Default.Describe(secret.Name).Origin });
            }
        );
    }

    private static string[] Strings(JsonNode? node) =>
        node is JsonArray array ? [.. array.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0)] : [];

    private static JsonNode? TextValue(IntegrationField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new WorkspaceException(field.Label + ": send text.", 422);
        var text = value.GetString()!.Trim();
        if (text.Length == 0)
            return null;
        Check(field, text);
        return JsonValue.Create(text);
    }

    private static JsonArray? ListValue(IntegrationField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new WorkspaceException(field.Label + ": send a list.", 422);
        var items = value
            .EnumerateArray()
            .Select(v =>
                v.ValueKind == JsonValueKind.String
                    ? v.GetString()!.Trim()
                    : throw new WorkspaceException(field.Label + ": send text.", 422)
            )
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (items.Length > field.MaxItems)
            throw new WorkspaceException(field.Label + ": at most " + field.MaxItems + ".", 422);
        foreach (var item in items)
            Check(field, item);
        return items.Length == 0 ? null : new JsonArray([.. items.Select(i => (JsonNode)JsonValue.Create(i))]);
    }

    private static void Check(IntegrationField field, string text)
    {
        if (text.Length > field.MaxLength || text.Any(char.IsControl))
            throw new WorkspaceException(field.Label + ": too long or not plain text.", 422);
        if (field.Check?.Invoke(text) is { } refusal)
            throw new WorkspaceException(field.Label + ": " + refusal, 422);
    }

    // The integration's file as a JSON object; a file that is not one is refused rather than overwritten.
    private static JsonObject Read(string dataRoot, IntegrationSetup setup)
    {
        var path = Path.Combine(dataRoot, setup.File);
        if (!File.Exists(path))
            return [];
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new WorkspaceException(setup.File + " is not a JSON object; fix or remove it first.", 409);
        }
        catch (JsonException)
        {
            throw new WorkspaceException(setup.File + " is not valid JSON; fix or remove it first.", 409);
        }
    }

    private static void Write(string dataRoot, IntegrationSetup setup, JsonObject file)
    {
        Directory.CreateDirectory(dataRoot);
        var path = Path.Combine(dataRoot, setup.File);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}

// Secrets the owner pasted in local mode (docs/adr/0038-integration-setup-in-the-app.md): only the names integrations
// declare, at most 64 KiB, each encrypted with the host's Data Protection keys (a key-encryption key on any OS, else
// DPAPI on Windows) in an owner-only directory under the data root. Values are never returned to a page.
public sealed class LocalSecretStore
{
    public const int MaxBytes = 64 * 1024;

    // The integration secrets that may be pasted. Host secrets (database, audit and key-encryption keys, sign-in) never
    // are: they protect this store or the company itself. A test keeps this equal to what integrations declare.
    public static readonly string[] Names =
    [
        HireZero.Integrations.Google.GoogleAnalyticsProvider.KeySecret,
        HireZero.Integrations.Google.GoogleAdsProvider.DeveloperTokenSecret,
        HireZero.Integrations.Google.GoogleAdsProvider.ClientSecretSecret,
        HireZero.Integrations.Google.GoogleAdsProvider.RefreshTokenSecret,
        HireZero.Integrations.Google.GoogleAdsProvider.ChangeRefreshTokenSecret,
        "HIREZERO_OPENROUTER_API_KEY",
    ];
    private readonly string _directory;
    private readonly IDataProtector _protector;
    private readonly HashSet<string> _names;

    public LocalSecretStore(string dataRoot, IDataProtectionProvider protection, IEnumerable<string> names)
    {
        _directory = Path.Combine(dataRoot, "secrets");
        _protector = protection.CreateProtector("HireZero.LocalSecrets.v1");
        _names = names.ToHashSet(StringComparer.Ordinal);
    }

    private string PathOf(string name) => Path.Combine(_directory, name + ".secret");

    public bool Has(string name) => _names.Contains(name) && File.Exists(PathOf(name));

    public string? Read(string name)
    {
        if (!Has(name))
            return null;
        try
        {
            return _protector.Unprotect(File.ReadAllText(PathOf(name)));
        }
        catch (CryptographicException)
        {
            // Encrypted under keys this host no longer has: treated as missing, so the owner can paste it again.
            return null;
        }
    }

    public void Write(string name, string value)
    {
        if (!_names.Contains(name))
            throw new WorkspaceException("This integration reads no such secret.", 404);
        value = value.Trim();
        if (value.Length == 0)
            throw new WorkspaceException("Paste the secret first.", 422);
        if (Encoding.UTF8.GetByteCount(value) > MaxBytes)
            throw new WorkspaceException("A secret is at most 64 KiB.", 422);
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(_directory);
        else
            Directory.CreateDirectory(
                _directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        var path = PathOf(name);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, _protector.Protect(value));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, overwrite: true);
    }

    public void Remove(string name)
    {
        if (Has(name))
            File.Delete(PathOf(name));
    }
}
