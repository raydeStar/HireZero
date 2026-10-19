namespace HireZero.Tools.Crm;

// Vendor-neutral CRM pipeline reads (docs/adr/0035-email-and-crm-read-contracts.md). Aggregates only: counts and values
// by stage and lead source, never a contact, company or deal record. Stage and source names are third-party text.

public sealed record CrmStatusRequest;

public sealed record PipelineRequest(DateOnly Start, DateOnly End);

// Open deals in a stage now (a snapshot when read), in pipeline order.
public sealed record PipelineStage(string Name, int Order, long OpenDeals, decimal OpenValue);

// What happened from each lead source during the range.
public sealed record SourceRow(string Source, long NewContacts, long NewDeals, long WonDeals, decimal WonValue);

// Deals created, won and lost during the range; DaysToClose is the average age of the deals won in it, if any.
public sealed record PipelineReport(
    string Source,
    string Currency,
    DateOnly Start,
    DateOnly End,
    PipelineStage[] Stages,
    long NewContacts,
    long NewDeals,
    long WonDeals,
    decimal WonValue,
    long LostDeals,
    decimal LostValue,
    double? DaysToClose,
    SourceRow[] Sources
);

public static class CrmTools
{
    public const int MaxDays = 400;

    public static readonly ToolContract<CrmStatusRequest, IntegrationStatus> Status = new(
        "crm.status",
        ToolRisk.Read,
        "Which CRM is connected, and why not."
    );

    public static readonly ToolContract<PipelineRequest, PipelineReport> Pipeline = new(
        "crm.pipeline",
        ToolRisk.Read,
        "Read the sales pipeline: open deals by stage, deals won and lost, and results by lead source."
    );

    public static void Check(PipelineRequest request)
    {
        if (request.End < request.Start)
            throw new ToolFailureException("refused", "The range ends before it starts.", 422);
        if (request.End.DayNumber - request.Start.DayNumber + 1 > MaxDays)
            throw new ToolFailureException("refused", "Read at most " + MaxDays + " days at once.", 422);
    }
}
