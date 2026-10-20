using HireZero.Core;

// Where secrets come from (docs/adr/0027-secrets-from-files.md): a named file, then a credentials directory, then the
// environment. Values are fictional.
public sealed class SecretSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hirezero-secrets-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void ANamedFileWinsOverTheEnvironmentAndLosesItsTrailingNewline()
    {
        var file = Write("client-secret", "fictional-from-file\n");
        var environment = new Dictionary<string, string>
        {
            ["HIREZERO_FICTIONAL_SECRET"] = "fictional-from-environment",
            ["HIREZERO_FICTIONAL_SECRET_FILE"] = file,
        };
        var secrets = new SecretSource(environment.GetValueOrDefault, []);

        Assert.Equal("fictional-from-file", secrets.Read("HIREZERO_FICTIONAL_SECRET"));
        var status = secrets.Describe("HIREZERO_FICTIONAL_SECRET");
        Assert.Equal((SecretOrigin.File, file, true), (status.Origin, status.Location, status.AlsoInEnvironment));
        Assert.DoesNotContain("fictional-from", status.ToString());
    }

    [Fact]
    public void ASecretPastedInTheAppIsReadLast()
    {
        var pasted = new Dictionary<string, string> { ["HIREZERO_FICTIONAL_SECRET"] = "fictional-pasted" };
        var environment = new Dictionary<string, string>();
        var secrets = new SecretSource(environment.GetValueOrDefault, [], pasted.GetValueOrDefault);
        Assert.Equal(
            ("fictional-pasted", SecretOrigin.App),
            (secrets.Read("HIREZERO_FICTIONAL_SECRET"), secrets.Describe("HIREZERO_FICTIONAL_SECRET").Origin)
        );
        environment["HIREZERO_FICTIONAL_SECRET"] = "fictional-from-environment";
        Assert.Equal(
            ("fictional-from-environment", SecretOrigin.Environment),
            (secrets.Read("HIREZERO_FICTIONAL_SECRET"), secrets.Describe("HIREZERO_FICTIONAL_SECRET").Origin)
        );
    }

    [Fact]
    public void ACredentialsDirectoryIsReadByExactOrLowercaseName()
    {
        var credentials = Directory.CreateDirectory(Path.Combine(_root, "credentials")).FullName;
        File.WriteAllText(Path.Combine(credentials, "hirezero_fictional_secret"), "fictional-credential\r\n");
        var secrets = new SecretSource(_ => null, [credentials]);

        Assert.Equal("fictional-credential", secrets.Read("HIREZERO_FICTIONAL_SECRET"));
        Assert.Equal(SecretOrigin.Credential, secrets.Describe("HIREZERO_FICTIONAL_SECRET").Origin);
        Assert.Null(secrets.Read("HIREZERO_OTHER_SECRET"));
        Assert.Equal(SecretOrigin.Missing, secrets.Describe("HIREZERO_OTHER_SECRET").Origin);
    }

    [Fact]
    public void ARotatedFileIsReadOnTheNextUseAndAMissingNamedFileIsAnError()
    {
        var file = Write("rotating", "fictional-one");
        var secrets = new SecretSource(name => name == "HIREZERO_FICTIONAL_SECRET_FILE" ? file : null, []);
        Assert.Equal("fictional-one", secrets.Read("HIREZERO_FICTIONAL_SECRET"));
        File.WriteAllText(file, "fictional-two");
        Assert.Equal("fictional-two", secrets.Read("HIREZERO_FICTIONAL_SECRET"));

        File.Delete(file);
        Assert.Throws<InvalidOperationException>(() => secrets.Read("HIREZERO_FICTIONAL_SECRET"));
    }

    [Fact]
    public void TheEnvironmentIsTheLastResort()
    {
        var secrets = new SecretSource(
            name => name == "HIREZERO_FICTIONAL_SECRET" ? "fictional-environment" : null,
            []
        );
        Assert.Equal("fictional-environment", secrets.Read("HIREZERO_FICTIONAL_SECRET"));
        Assert.Equal(SecretOrigin.Environment, secrets.Describe("HIREZERO_FICTIONAL_SECRET").Origin);
    }
}
