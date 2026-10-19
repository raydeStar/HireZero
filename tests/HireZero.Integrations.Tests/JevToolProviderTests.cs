using HireZero.Integrations.Jev;

// The company Jev provider reports whether its key is set, and why not; it reads only the environment variable.
public sealed class JevToolProviderTests
{
    [Fact]
    public void TheStatusSaysWhetherTheKeyIsSet()
    {
        string[] names = ["HIREZERO_JEV_API_KEY", "TYPESAFE_API_KEY"];
        var previous = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in names)
                Environment.SetEnvironmentVariable(name, null);
            var status = JevToolProvider.Company().Status();
            Assert.Equal((false, "Set HIREZERO_JEV_API_KEY to use Jev."), (status.Active, status.Reason));
            Environment.SetEnvironmentVariable("HIREZERO_JEV_API_KEY", "fictional-key");
            Assert.True(JevToolProvider.Company().Status().Active);
            Environment.SetEnvironmentVariable("HIREZERO_JEV_API_KEY", "fictional\nkey");
            Assert.False(JevToolProvider.Company().Status().Active);
        }
        finally
        {
            foreach (var (name, value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
