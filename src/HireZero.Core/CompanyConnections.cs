using System.Text.RegularExpressions;

namespace HireZero.Core;

// A request to connect an integration, shown as a Connect card in the conversation it came from
// (docs/adr/0046-connect-cards.md). An employee's reply or a refused action raises it; only the owner connects, and
// only an integration the company knows. Connecting is recorded; a card never holds a secret.
public sealed record CompanyConnectionRequest(
    string Id,
    string IntegrationId,
    string Name,
    string ThreadId,
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

    // A reply asks for a connection with one line "[connect: id]"; the line is removed from what the owner reads.
    [GeneratedRegex(@"^\s*\[connect:\s*([a-z0-9][a-z0-9-]{0,47})\]\s*$", RegexOptions.Multiline)]
    private static partial Regex ConnectLine();

    // Kept short: the whole reply packet is capped at 4 KiB, and its history grows (real use, 2026-10-21: names and
    // reasons pushed a real reply over the cap).
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
        string threadId,
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
                : closed.Name + " connected" + (account is null ? "" : " as " + account) + " from the conversation."
        );
        return closed;
    }
}
