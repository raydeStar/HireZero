namespace HireZero.Roles.Developer;

public static class JevConfiguration
{
    public static string? Key()
    {
        var key =
            Environment.GetEnvironmentVariable("HIREZERO_JEV_API_KEY")
            ?? Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        return !string.IsNullOrWhiteSpace(key) && !key.Contains('\r') && !key.Contains('\n') ? key.Trim() : null;
    }
}
