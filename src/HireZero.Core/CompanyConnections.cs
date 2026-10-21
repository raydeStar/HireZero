using System.Text.RegularExpressions;

namespace HireZero.Core;

// A request to connect an integration, shown as a Connect card in the conversation it came from
// (docs/adr/0046-connect-cards.md). An employee's reply or a refused action raises it, or an assistant through
// HireZero's MCP endpoint (docs/adr/0048-assistant-connect-requests.md), in which case it has no conversation and
// waits under Needs you. Only the owner connects, and only an integration the company knows. Connecting is recorded;
// a card never holds a secret.
public sealed record CompanyConnectionRequest(
    string Id,
    string IntegrationId,
    string Name,
    string? ThreadId,
    string? EmployeeId,
    string Reason,
    string State = "open",
    int Version = 1
)
{
    // The refused work that needed the connection, when an action raised it.
    public string? TaskId { get; init; }

    // The account it was connected as, when the integration names one.
    public string? Account { get; init; }

    // The assistant that asked through the MCP endpoint, as it named itself.
    public string? From { get; init; }

    // Where an assistant says the key already is (only "env:NAME"), and which of the integration's secrets it fills.
    // The value is read on this computer when the owner agrees; it never passes through the assistant.
    public string? KeySource { get; init; }
    public string? SecretName { get; init; }
}

// A refusal because an integration is not connected. It names the integration so the conversation can offer to
// connect it; integrations name their own ids through their status, so no role hard-codes one.
public sealed class IntegrationMissingException(string integrationId, string message) : WorkspaceException(message, 409)
{
    public string IntegrationId { get; } = integrationId;
}

public sealed partial class CompanyStore
{
    // Every integration and whether it is active, from the host (role-agnostic); empty when the host names none.
    public Func<IntegrationStatus[]> IntegrationStatuses { get; set; } = () => [];

    // A reply asks for a connection with "[connect: id]", on its own line or at the end of a sentence (a real reply put
    // it inline, 2026-10-21); every such tag is removed from what the owner reads.
    [GeneratedRegex(@"[ \t]*\[connect:\s*([a-z0-9][a-z0-9-]{0,47})\][ \t]*")]
    private static partial Regex ConnectLine();

    // Kept short: the reply packet is capped and its history grows (real use, 2026-10-21: names and reasons pushed a
    // real reply over the cap).
    public const string ConnectInstructions =
        " If you need an integration listed as not connected, say so and end with one line [connect: id]. Only the owner connects; never ask for keys.";

    // The connections line for a reply's packet: integration ids only, connected first. Demo sources are left out.
    private string ConnectionsLine()
    {
        var real = IntegrationStatuses().Where(s => !s.Id.StartsWith("synthetic-", StringComparison.Ordinal)).ToArray();
        var line =
            "connected: "
            + string.Join(", ", real.Where(s => s.Active).Select(s => s.Id))
            + "; not connected: "
            + string.Join(", ", real.Where(s => !s.Active).Select(s => s.Id));
        return line.Length <= 400 ? line : line[..400];
    }

    // The connect lines a reply carried, removed from its text, and the open requests they raised.
    private string TakeConnectLines(StoreConnection db, string body, CompanyTask task)
    {
        var ids = ConnectLine().Matches(body).Select(m => m.Groups[1].Value).Distinct().Take(3).ToArray();
        if (ids.Length == 0)
            return body;
        foreach (var id in ids)
            RequestConnection(db, id, task.ThreadId, task.EmployeeId, "Asked for in a reply.", null);
        var text = ConnectLine().Replace(body, "").TrimEnd();
        return text.Length > 0 ? text : body;
    }

    // Raises (or returns) the open request for an integration in a conversation. Unknown or already connected
    // integrations raise nothing.
    private CompanyConnectionRequest? RequestConnection(
        StoreConnection db,
        string integrationId,
        string? threadId,
        string? employeeId,
        string reason,
        string? taskId
    )
    {
        var status = IntegrationStatuses().FirstOrDefault(s => s.Id == integrationId);
        if (status is null || status.Active)
            return null;
        if (
            All<CompanyConnectionRequest>(db)
                .FirstOrDefault(r => r.IntegrationId == integrationId && r.ThreadId == threadId && r.State == "open") is
            { } open
        )
            return open;
        var request = new CompanyConnectionRequest(
            Id(),
            integrationId,
            status.Name,
            threadId,
            employeeId,
            Text(reason, 300)
        )
        {
            TaskId = taskId,
        };
        Save(db, request, request.Id, state: request.State, version: request.Version);
        Event(
            db,
            "connection.requested",
            request.Id,
            employeeId,
            status.Name + " is needed: " + request.Reason + " Nothing is connected until the owner connects it."
        );
        return request;
    }

    // connection.resolve: the owner connected it (Body: the account, when the integration names one). It must be
    // active now; connection.dismiss closes the card without connecting.
    private CompanyConnectionRequest ResolveConnection(StoreConnection db, CompanyCommand c, bool dismiss)
    {
        var request = Need<CompanyConnectionRequest>(db, Text(c.Id, 64));
        Version(request.Version, c.ExpectedVersion);
        if (request.State != "open")
            throw new WorkspaceException("This connection request is already closed.");
        string? account = null;
        if (!dismiss)
        {
            var status = IntegrationStatuses().FirstOrDefault(s => s.Id == request.IntegrationId);
            if (status is not { Active: true })
                throw new WorkspaceException(
                    request.Name + " is not connected yet" + (status is null ? "." : ": " + status.Reason),
                    409
                );
            account = string.IsNullOrWhiteSpace(c.Body) ? null : Text(c.Body, 100);
        }
        var closed = request with
        {
            State = dismiss ? "dismissed" : "connected",
            Version = request.Version + 1,
            Account = account,
        };
        Save(db, closed, closed.Id, state: closed.State, version: closed.Version);
        Event(
            db,
            dismiss ? "connection.dismissed" : "integration.connected",
            closed.Id,
            closed.EmployeeId,
            dismiss
                ? closed.Name + " connection request dismissed."
                : closed.Name
                    + " connected"
                    + (account is null ? "" : " as " + account)
                    + (closed.From is null ? " from the conversation." : " at " + closed.From + "'s request.")
        );
        return closed;
    }

    [GeneratedRegex(@"^env:[A-Za-z_][A-Za-z0-9_]{0,99}$")]
    private static partial Regex EnvironmentSource();

    // An assistant asks for a connection through the MCP endpoint (docs/adr/0048-assistant-connect-requests.md). It
    // may say where the key already is (env:NAME) and which secret it fills; it never sends a key. The text it gives
    // is shown as its words, and nothing happens until the owner acts on the card.
    public CompanyConnectionRequest RequestFromAssistant(
        string integrationId,
        string from,
        string reason,
        string? keySource,
        string? secretName
    )
    {
        var status =
            IntegrationStatuses().FirstOrDefault(s => s.Id == integrationId)
            ?? throw new WorkspaceException("No such integration: " + Clip(integrationId) + ".", 404);
        if (status.Active)
            throw new WorkspaceException(status.Name + " is already connected.", 409);
        if (keySource is not null && (!EnvironmentSource().IsMatch(keySource) || string.IsNullOrWhiteSpace(secretName)))
            throw new WorkspaceException(
                "A key source is env:NAME, with the secret it fills; the key itself is never sent.",
                422
            );
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var request = new CompanyConnectionRequest(Id(), status.Id, status.Name, null, null, Text(reason, 300))
                {
                    From = Text(from, 60),
                    KeySource = keySource,
                    SecretName = keySource is null ? null : Text(secretName, 100),
                };
                Save(db, request, request.Id, state: request.State, version: request.Version);
                Event(
                    db,
                    "connection.requested",
                    request.Id,
                    null,
                    request.From
                        + " asked to connect "
                        + status.Name
                        + (keySource is null ? "" : " using the key in " + keySource)
                        + ". Nothing is connected until the owner agrees."
                );
                return request;
            }
        );
    }

    public CompanyConnectionRequest Connection(string id)
    {
        using var db = Open();
        return Need<CompanyConnectionRequest>(db, Text(id, 64));
    }
}
