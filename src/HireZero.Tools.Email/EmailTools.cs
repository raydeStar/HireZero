namespace HireZero.Tools.Email;

// Vendor-neutral email marketing reads (docs/adr/0035-email-and-crm-read-contracts.md). Aggregates per campaign only:
// no recipient, address or person ever crosses this contract. Campaign names are untrusted third-party text.

public sealed record EmailStatusRequest;

// Campaigns sent between Start and End inclusive, newest first. Limit is 1 to 100.
public sealed record EmailCampaignsRequest(DateOnly Start, DateOnly End, int Limit);

// Opens and clicks are unique per recipient. Opens are inflated by mail privacy features that preload images, so
// clicks are the firmer signal.
public sealed record EmailCampaignRow(
    string Id,
    string? Name,
    DateOnly? Sent,
    long Recipients,
    long Delivered,
    long Opens,
    long Clicks,
    long Unsubscribes,
    long Bounces,
    long Complaints
);

public sealed record EmailReport(
    string Source,
    DateOnly Start,
    DateOnly End,
    EmailCampaignRow Totals,
    EmailCampaignRow[] Rows
);

public static class EmailTools
{
    public const int MaxDays = 400;

    public static readonly ToolContract<EmailStatusRequest, IntegrationStatus> Status = new(
        "email.status",
        ToolRisk.Read,
        "Which email marketing account is connected, and why not."
    );

    public static readonly ToolContract<EmailCampaignsRequest, EmailReport> Campaigns = new(
        "email.campaigns",
        ToolRisk.Read,
        "Read sent email campaigns' delivery, opens, clicks, unsubscribes, bounces and complaints."
    );

    // The request rules every email provider applies before it sends anything.
    public static void Check(EmailCampaignsRequest request)
    {
        if (request.End < request.Start)
            throw new ToolFailureException("refused", "The range ends before it starts.", 422);
        if (request.End.DayNumber - request.Start.DayNumber + 1 > MaxDays)
            throw new ToolFailureException("refused", "Read at most " + MaxDays + " days at once.", 422);
        if (request.Limit is < 1 or > 100)
            throw new ToolFailureException("refused", "Read between 1 and 100 campaigns.", 422);
    }
}
