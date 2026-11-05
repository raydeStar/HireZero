using System.Text.Json;
using HireZero.Integrations.Jev;

public sealed partial class CompanyModelConnection
{
    private bool HasDecisionKey(Saved saved) =>
        SpendStore is null
        && !saved.Unreadable
        && saved.Mode is "company-key" or "host-and-company-key"
        && saved.Provider == "openrouter"
        && !string.IsNullOrWhiteSpace(saved.Key);

    public object DecisionConnection()
    {
        var saved = Current;
        return new
        {
            available = HasDecisionKey(saved),
            model = HasDecisionKey(saved)
            && saved.Route?.StartsWith("typesafe/jev-", StringComparison.Ordinal) == true
            && !saved.Route.Contains("router", StringComparison.Ordinal)
                ? saved.Route
                : null,
            endpoint = OpenRouterDecisions.Endpoint,
            status = HasDecisionKey(saved)
                ? "Uses this company's saved OpenRouter API account. Access and charges are unverified until a reviewed request completes."
                : "Add an OpenRouter account in AI model connection. Keep the host connection alongside it to retain Codex for text work.",
        };
    }

    private OpenRouterDecisions DecisionTransport(string revision) =>
        new(
            companyId,
            () =>
            {
                var saved = Current;
                return HasDecisionKey(saved) && saved.Revision == revision ? saved.Key : null;
            },
            decisionHandler
        );

    public CompanyDecisionPacket PrepareDecision(CompanyDecisionInput input)
    {
        var saved = Current;
        if (!HasDecisionKey(saved))
            throw new WorkspaceException(
                "Connect this company's OpenRouter account before preparing a Jev decision.",
                409
            );
        try
        {
            using var transport = DecisionTransport(saved.Revision);
            var request = transport.Prepare(input.Model, input.StateJson, input.Instructions, input.Criteria);
            return new(
                companyId,
                "openrouter",
                request.Model,
                request.Json,
                request.Digest,
                input.Criteria.Keys.Order(StringComparer.Ordinal).ToArray(),
                new("company-key", "openrouter", saved.Revision)
            );
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            throw new WorkspaceException(
                "Use a versioned Jev model, valid bounded JSON, and two to eight named choices. Router models cannot make this decision.",
                422
            );
        }
    }

    public bool DecisionCurrent(CompanyDecisionPacket packet)
    {
        var saved = Current;
        return HasDecisionKey(saved)
            && packet.CompanyId == companyId
            && packet.Provider == "openrouter"
            && packet.Funding == new CompanyModelFunding("company-key", "openrouter", saved.Revision);
    }

    public async Task<CompanyDecisionOutcome> SendDecisionAsync(CompanyDecision decision, CancellationToken token)
    {
        if (!decision.DispatchTaken || decision.State != "running" || !DecisionCurrent(decision.Packet))
            return new("not_sent", Usage: new(0, 0, BilledUsd: 0), ReportedCostUsd: 0);
        using var transport = DecisionTransport(decision.Packet.Funding.ConnectionRevision);
        JevChoiceRequest request;
        try
        {
            request = transport.Restore(decision.Packet.Model, decision.Packet.Json, decision.Packet.TransportDigest);
        }
        catch (Exception error)
            when (error is ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new("not_sent", Usage: new(0, 0, BilledUsd: 0), ReportedCostUsd: 0);
        }
        var result = await transport.SendAsync(request, decision.Packet.TransportDigest, token);
        return new(
            result.State,
            result.Choice,
            result.Confidence,
            result.Probabilities?.ToDictionary(),
            result.Usage,
            result.Receipt,
            result.ResolvedModel,
            result.ReportedCostUsd
        );
    }
}
