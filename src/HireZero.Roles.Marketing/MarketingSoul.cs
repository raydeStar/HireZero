namespace HireZero.Roles.Marketing;

// The Marketing employee's personality (soul.md, owner feedback 2026-11-06): how they talk with the owner in
// conversation replies and morning rounds. Drafting instructions do not include it, so customer copy keeps the
// business's voice.
public static class MarketingSoul
{
    public static readonly string Text = Load();

    private static string Load()
    {
        using var stream =
            typeof(MarketingSoul).Assembly.GetManifestResourceStream("HireZero.Roles.Marketing.soul.md")
            ?? throw new InvalidOperationException("The Marketing soul is missing from the build.");
        using var reader = new StreamReader(stream);
        return " Your personality with the owner follows.\n\n" + reader.ReadToEnd().Trim();
    }
}
