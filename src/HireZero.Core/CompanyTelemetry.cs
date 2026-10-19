using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HireZero.Core;

// Traces and metrics through the .NET runtime only (docs/adr/0024-opentelemetry-traces-and-metrics.md). Tags carry
// identifiers and outcomes, never task text, prompts, inputs, emails or paths.
public static class CompanyTelemetry
{
    public const string Name = "HireZero.Company";

    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);

    private static readonly Counter<long> Jobs = Meter.CreateCounter<long>("hirezero.jobs", "{job}");
    private static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>("hirezero.job.duration", "s");
    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>("hirezero.tool.calls", "{call}");
    private static readonly Counter<long> ModelCalls = Meter.CreateCounter<long>("hirezero.model.calls", "{call}");
    private static readonly Counter<long> ModelTokens = Meter.CreateCounter<long>("hirezero.model.tokens", "{token}");
    private static readonly Counter<long> CodingRuns = Meter.CreateCounter<long>("hirezero.coding.runs", "{run}");

    // Measurements that come from a database change wait for its commit; a rolled-back change counts nothing.
    // Transactions are synchronous, so the pending list belongs to the thread that opened one.
    [ThreadStatic]
    private static List<Action>? _pending;

    internal static void BeginTransaction() => _pending = [];

    internal static void Commit()
    {
        var pending = _pending;
        _pending = null;
        foreach (var emit in pending ?? [])
            emit();
    }

    internal static void Discard() => _pending = null;

    private static void Emit(Action emit)
    {
        if (_pending is { } pending)
            pending.Add(emit);
        else
            emit();
    }

    public static void Job(string company, string employee, string outcome, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "company", company },
            { "employee", employee },
            { "outcome", outcome },
        };
        Jobs.Add(1, tags);
        JobDuration.Record(duration.TotalSeconds, tags);
    }

    internal static void ToolCall(string company, ToolCallRecord record) =>
        Emit(() =>
            ToolCalls.Add(
                1,
                new TagList
                {
                    { "company", company },
                    { "employee", record.EmployeeId ?? "" },
                    { "tool", record.ToolId },
                    { "risk", record.Risk },
                    { "outcome", record.Outcome },
                }
            )
        );

    internal static void ModelCall(string company, string employee, string phase, long tokens) =>
        Emit(() =>
        {
            ModelCalls.Add(
                1,
                new TagList
                {
                    { "company", company },
                    { "employee", employee },
                    { "phase", phase },
                }
            );
            if (tokens > 0)
                ModelTokens.Add(tokens, new TagList { { "company", company }, { "employee", employee } });
        });

    internal static void CodingRun(string company, string employee, string state) =>
        Emit(() =>
            CodingRuns.Add(
                1,
                new TagList
                {
                    { "company", company },
                    { "employee", employee },
                    { "state", state },
                }
            )
        );
}
