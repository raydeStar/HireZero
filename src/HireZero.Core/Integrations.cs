namespace HireZero.Core;

// Whether an optional integration is active, and why not (docs/adr/0009-legacy-features-are-optional-integrations.md).
// Integrations report it; hosts show the owner every integration's status.
public sealed record IntegrationStatus(string Id, string Name, bool Active, string Reason);

public interface IIntegrationStatusSource
{
    IntegrationStatus Status();
}
