using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using HireZero.Core;
using Microsoft.Data.Sqlite;

// Traces and metrics (docs/adr/0024-opentelemetry-traces-and-metrics.md), observed with the runtime's own listeners:
// jobs, tool calls, model calls and tokens per company and employee, committed changes only, identifiers only.
public sealed class CompanyTelemetryTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-telemetry-");
    private readonly string _company = CompanyStore.NewCompanyId();
    private readonly ConcurrentQueue<(string Name, long Value, Dictionary<string, string> Tags)> _measurements = new();
    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly MeterListener _meters = new();
    private readonly ActivityListener _activities;

    public CompanyTelemetryTests()
    {
        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == CompanyTelemetry.Name)
                listener.EnableMeasurementEvents(instrument);
        };
        _meters.SetMeasurementEventCallback<long>((i, value, tags, _) => Observe(i.Name, value, tags));
        _meters.SetMeasurementEventCallback<double>((i, value, tags, _) => Observe(i.Name, (long)value, tags));
        _meters.Start();
        _activities = new()
        {
            ShouldListenTo = source => source.Name == CompanyTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_activities);
    }

    private void Observe(string name, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var map = new Dictionary<string, string>();
        foreach (var tag in tags)
            map[tag.Key] = tag.Value?.ToString() ?? "";
        // Tests run in parallel against the same static meter; keep only this test's company.
        if (map.GetValueOrDefault("company") == _company)
            _measurements.Enqueue((name, value, map));
    }

    public void Dispose()
    {
        _meters.Dispose();
        _activities.Dispose();
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    private long Sum(string name, Func<Dictionary<string, string>, bool>? where = null) =>
        _measurements.Where(m => m.Name == name && (where?.Invoke(m.Tags) ?? true)).Sum(m => m.Value);

    private (CompanyStore Store, CompanyEmployee Employee, CompanyTask Task) Seeded()
    {
        var store = new CompanyStore(
            Path.Combine(_root.Path, "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions(),
            companyId: _company
        );
        var department = CompanyJson.Decode<CompanyDepartment>(
            store.Apply(Cmd("department.create") with { Name = "Engineering" })
        );
        var fields = store
            .Roles.Single(r => r.Id == "developer")
            .OnboardingFields.ToDictionary(f => f, f => "Approved fictional " + f);
        var knowledge = CompanyJson.Decode<CompanyKnowledge>(
            store.Apply(Cmd("knowledge.save") with { DepartmentId = department.Id, Body = CompanyJson.Encode(fields) })
        );
        store.Apply(
            Cmd("knowledge.review") with
            {
                Id = knowledge.Id,
                ExpectedVersion = knowledge.Version,
                Digest = knowledge.Digest,
                Decision = "approve",
            }
        );
        var employee = CompanyJson.Decode<CompanyEmployee>(
            store.Apply(
                Cmd("employee.hire") with
                {
                    DepartmentId = department.Id,
                    RoleId = "developer",
                    Name = "Developer partner",
                    Responsibility = "Fictional",
                }
            )
        );
        var thread = CompanyJson.Decode<CompanyThread>(
            store.Apply(Cmd("thread.create") with { EmployeeId = employee.Id, Name = "Work" })
        );
        var task = CompanyJson.Decode<CompanyTask>(
            store.Apply(
                Cmd("task.create") with
                {
                    EmployeeId = employee.Id,
                    ThreadId = thread.Id,
                    Skill = "developer-advice",
                    Body = "Secret fictional task text.",
                }
            )
        );
        return (store, employee, task);
    }

    [Fact]
    public async Task AJobIsTracedAndItsTokensCountOncePerCompanyAndEmployee()
    {
        var (store, employee, _) = Seeded();
        var model = new FakeModel { Usage = 100 };

        Assert.True(
            await new CompanyScheduler(store, model).TickAsync(employee.Id, TestContext.Current.CancellationToken)
        );

        var attempt = Assert.Single(store.AuditEntries(), e => e.Kind == "model-call" && e.Body.Contains("committed"));
        var reported = System.Text.Json.JsonDocument.Parse(attempt.Body).RootElement.GetProperty("reported").GetInt64();
        Assert.True(reported > 100);
        Assert.Equal(reported, Sum("hirezero.model.tokens", t => t["employee"] == employee.Id));
        Assert.Equal(1, Sum("hirezero.jobs", t => t["employee"] == employee.Id && t["outcome"] == "completed"));
        Assert.Contains(_measurements, m => m.Name == "hirezero.job.duration");
        Assert.True(Sum("hirezero.model.calls", t => t["phase"] == "committed") == 1);
        var job = Assert.Single(
            _spans,
            s => s.OperationName == "employee.job" && Equals(s.GetTagItem("hirezero.company"), _company)
        );
        Assert.Equal(employee.Id, job.GetTagItem("hirezero.employee"));
        // Identifiers only: no task text reaches a tag.
        Assert.DoesNotContain(_measurements, m => m.Tags.Values.Any(v => v.Contains("Secret")));
        Assert.DoesNotContain(job.TagObjects, t => t.Value?.ToString()?.Contains("Secret") == true);
    }

    [Fact]
    public void ToolCallsAreCountedByTheirOutcome()
    {
        var (store, employee, _) = Seeded();
        ToolCallRecord Call(string outcome) =>
            new(
                DateTimeOffset.UtcNow,
                "git.read",
                "read",
                "developer",
                employee.Id,
                null,
                new string('a', 64),
                null,
                outcome,
                null
            );
        store.Record(Call("completed"));
        store.Record(Call("refused"));
        Assert.Equal(1, Sum("hirezero.tool.calls", t => t["tool"] == "git.read" && t["outcome"] == "completed"));
        Assert.Equal(1, Sum("hirezero.tool.calls", t => t["outcome"] == "refused"));
    }

    [Fact]
    public void AMeasurementFromARolledBackChangeIsNeverEmitted()
    {
        var (store, employee, task) = Seeded();
        void Sql(string sql)
        {
            using var db = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(_root.Path, "company.sqlite"),
                    Pooling = false,
                }.ToString()
            );
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        // The claim queues its model-call measurement, then its audit entry fails and the transaction rolls back.
        Sql("CREATE TRIGGER refuse_audit BEFORE INSERT ON co_audit BEGIN SELECT RAISE(ABORT,'fixture'); END");
        Assert.Throws<SqliteException>(() => store.Claim(employee.Id, true, task.Id));
        Assert.Equal(0, Sum("hirezero.model.calls"));

        Sql("DROP TRIGGER refuse_audit");
        Assert.NotNull(store.Claim(employee.Id, true, task.Id));
        Assert.Equal(1, Sum("hirezero.model.calls", t => t["phase"] == "claimed"));
    }

    [Fact]
    public void TheHostExportsOnlyWhenAnOtlpEndpointIsConfigured()
    {
        var off = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddCompanyTelemetry(false);
        Assert.DoesNotContain(off, d => d.ServiceType == typeof(OpenTelemetry.Trace.TracerProvider));
        var on = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddCompanyTelemetry(true);
        Assert.Contains(on, d => d.ServiceType == typeof(OpenTelemetry.Trace.TracerProvider));
        Assert.Contains(on, d => d.ServiceType == typeof(OpenTelemetry.Metrics.MeterProvider));
    }
}
