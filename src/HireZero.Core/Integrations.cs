namespace HireZero.Core;

// Whether an optional integration is active, and why not (docs/adr/0009-legacy-features-are-optional-integrations.md).
// Integrations report it; hosts show the owner every integration's status.
public sealed record IntegrationStatus(string Id, string Name, bool Active, string Reason);

public interface IIntegrationStatusSource
{
    IntegrationStatus Status();
}

// What the owner can set up in the app for one integration (docs/adr/0038-integration-setup-in-the-app.md): the
// settings file it already reads, the fields the app may write there, and the secrets it reads by name. Values that
// name a program to run are never declared, so a form can never make HireZero run one.
public sealed record IntegrationSetup(string Id, string File, IntegrationField[] Fields, IntegrationSecret[] Secrets)
{
    // How to connect, step by step, shown beside the form (owner feedback, 2026-10-20: linking Google took a chat
    // walkthrough). A step may link to the page where it is done.
    public IntegrationStep[] Steps { get; init; } = [];
}

public sealed record IntegrationStep(string Text, string? Url = null, string? LinkText = null);

// One setting. Check returns why a value is refused, or null; a list field holds up to MaxItems values.
public sealed record IntegrationField(string Id, string Label, string Help, int MaxLength = 200)
{
    public bool List { get; init; }
    public int MaxItems { get; init; } = 5;

    [System.Text.Json.Serialization.JsonIgnore]
    public Func<string, string?>? Check { get; init; }
}

// One secret, by the name SecretSource reads. Multiline secrets are files such as a service account key.
public sealed record IntegrationSecret(string Name, string Label, string Help)
{
    public bool Multiline { get; init; }
}

public interface IConfigurableIntegration
{
    IntegrationSetup Setup { get; }
}
