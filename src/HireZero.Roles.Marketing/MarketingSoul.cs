namespace HireZero.Roles.Marketing;

// The Marketing employee's default personality (soul.md, owner feedback 2026-11-06), offered through the manifest. Core
// adds it, or the owner's edit, to conversation replies and morning rounds only (ADR 0184).
public static class MarketingSoul
{
    public static readonly string Text = Load();

    private static string Load()
    {
        using var stream =
            typeof(MarketingSoul).Assembly.GetManifestResourceStream("HireZero.Roles.Marketing.soul.md")
            ?? throw new InvalidOperationException("The Marketing soul is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
