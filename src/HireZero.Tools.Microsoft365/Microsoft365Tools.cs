namespace HireZero.Tools.Microsoft365;

// Recent messages, read without moving any sync cursor. SinceHours is 1 to 72; Max is 1 to 50.
public sealed record InboxReadRequest(int SinceHours, int Max);

// One message as read: untrusted third-party text.
public sealed record InboxMessage(
    string Source,
    string Id,
    string Thread,
    string From,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAt
);

public sealed record Microsoft365StatusRequest;

public static class Microsoft365Tools
{
    public static readonly ToolContract<Microsoft365StatusRequest, IntegrationStatus> Status = new(
        "m365.status",
        ToolRisk.Read,
        "Whether Microsoft 365 is configured and signed in, and why not."
    );

    public static readonly ToolContract<InboxReadRequest, InboxMessage[]> ReadMail = new(
        "m365.mail.read",
        ToolRisk.Read,
        "Read recent Outlook inbox messages (subject, sender and preview) without changing them."
    );

    public static readonly ToolContract<InboxReadRequest, InboxMessage[]> ReadTeams = new(
        "m365.teams.read",
        ToolRisk.Read,
        "Read recent Teams chat messages without changing them."
    );
}
