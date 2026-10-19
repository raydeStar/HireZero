using System.Net;
using HireZero.Core;

// Typed settings (docs/adr/0028-typed-settings.md): one name per setting in the environment or a settings file, the
// environment winning; every problem reported at once; secrets and unknown names refused in the file.
public sealed class CompanySettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hirezero-settings-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string SettingsFile(string json)
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static Func<string, string?> Environment(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void TheEnvironmentWinsOverTheSettingsFile()
    {
        var file = SettingsFile(
            """
            {
              "HIREZERO_PUBLIC_URL": "https://hirezero.example.invalid/",
              "HIREZERO_TRUSTED_PROXY": "10.0.0.2",
              "HIREZERO_COMPANY_CODING_READ": "/srv/one; /srv/two",
              "HIREZERO_LOCAL_OWNER": "0"
            }
            """
        );
        var settings = CompanySettings.Load(
            Environment((CompanySettings.FileVariable, file), ("HIREZERO_LOCAL_OWNER", "1"))
        );

        Assert.Equal(new Uri("https://hirezero.example.invalid/"), settings.PublicUrl);
        Assert.Equal(IPAddress.Parse("10.0.0.2"), settings.TrustedProxy);
        Assert.Equal(["/srv/one", "/srv/two"], settings.CodingRead);
        Assert.True(settings.LocalOwner);
        Assert.Equal("environment", settings.Sources["HIREZERO_LOCAL_OWNER"]);
        Assert.Equal("settings file", settings.Sources["HIREZERO_PUBLIC_URL"]);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            CompanySettings.Load(
                Environment(
                    ("HIREZERO_LOCAL_OWNER", "yes"),
                    ("HIREZERO_PUBLIC_URL", "not a url"),
                    ("HIREZERO_TRUSTED_PROXY", "proxy.example.invalid")
                )
            )
        );
        Assert.Contains("HIREZERO_LOCAL_OWNER (environment) must be 1 or 0.", error.Message);
        Assert.Contains("HIREZERO_PUBLIC_URL (environment) is not an absolute URL.", error.Message);
        Assert.Contains("HIREZERO_TRUSTED_PROXY (environment) is not an IP address.", error.Message);
        Assert.DoesNotContain("not a url", error.Message);
    }

    [Fact]
    public void ASettingsFileMayNotHoldASecretOrAnUnknownName()
    {
        var file = SettingsFile(
            """
            { "HIREZERO_OIDC_CLIENT_SECRET": "fictional-secret", "HIREZERO_PUBLIK_URL": "https://typo.example.invalid/" }
            """
        );
        var error = Assert.Throws<InvalidOperationException>(() =>
            CompanySettings.Load(Environment((CompanySettings.FileVariable, file)))
        );
        Assert.Contains("HIREZERO_OIDC_CLIENT_SECRET is a secret", error.Message);
        Assert.Contains("HIREZERO_PUBLIK_URL in the settings file is not a setting", error.Message);
        Assert.DoesNotContain("fictional-secret", error.Message);

        Assert.Throws<InvalidOperationException>(() =>
            CompanySettings.Load(Environment((CompanySettings.FileVariable, Path.Combine(_root, "missing.json"))))
        );
    }

    [Fact]
    public void HostedModeNamesEveryMissingSetting()
    {
        var settings = CompanySettings.Load(Environment(("HIREZERO_PUBLIC_URL", "https://hirezero.example.invalid/")));
        var error = Assert.Throws<InvalidOperationException>(() =>
            HostedOptions.FromSettings(settings, new SecretSource(_ => null, []))
        );
        Assert.Equal(
            "Hosted mode needs HIREZERO_LISTEN, HIREZERO_OIDC_AUTHORITY, HIREZERO_OIDC_CLIENT_ID.",
            error.Message
        );
    }

    [Fact]
    public void EverySettingIsInTheReference()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "HireZero.slnx")))
            repository = repository.Parent!;
        var reference = File.ReadAllText(Path.Combine(repository.FullName, "docs", "SETTINGS.md"));
        Assert.All(CompanySettings.All, s => Assert.Contains("`" + s.Name + "`", reference));
    }
}
