using System.Text.Json;
using HireZero.Core;
using Microsoft.Data.Sqlite;

// The tamper-evident audit chain (docs/adr/0022-tamper-evident-audit-log.md): events with their actors, tool calls
// and model-call transitions are chained with a keyed hash, and any edit, removal or keyless forgery breaks it.
public sealed class CompanyAuditTests : IDisposable
{
    private const string Owner = "owner@example.invalid";
    private readonly TemporaryRoot _root = new("company-audit-");
    private string Path_ => Path.Combine(_root.Path, "company.sqlite");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static CompanyCommand Cmd(string action) => new(Guid.NewGuid().ToString("D"), action);

    private void Sql(string sql)
    {
        using var db = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = Path_, Pooling = false }.ToString()
        );
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private CompanyStore Seeded()
    {
        var store = new CompanyStore(Path_, Owner, CompanyWorkspace.Definitions());
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
        store.Record(
            new ToolCallRecord(
                DateTimeOffset.UtcNow,
                "git.read",
                "Read",
                "developer",
                null,
                null,
                new string('a', 64),
                null,
                "completed",
                null
            )
        );
        return store;
    }

    [Fact]
    public void ApprovalsAndToolCallsAreChainedWithTheirActors()
    {
        var store = Seeded();
        var entries = store.AuditEntries();
        Assert.Equal("audit.started", entries[0].Kind);
        var approval = entries.Single(e => e.Kind == "event" && e.Body.Contains("knowledge.approved"));
        Assert.Equal(Owner, approval.Actor);
        Assert.Contains(entries, e => e.Kind == "tool-call" && e.Body.Contains("git.read"));
        Assert.Equal(entries.Select((_, i) => (long)i + 1), entries.Select(e => e.Seq));
        var check = store.VerifyAudit();
        Assert.True(check.Intact);
        Assert.Equal(entries.Length, check.Count);
        Assert.Equal(entries[^1].Hash, check.Head);
        // A second store for the same company continues the same chain.
        new CompanyStore(Path_, Owner, CompanyWorkspace.Definitions()).Apply(
            Cmd("department.create") with
            {
                Name = "Ops",
            }
        );
        Assert.True(store.VerifyAudit().Intact);
    }

    [Fact]
    public void AnEditARemovalOrAKeylessForgeryBreaksTheChain()
    {
        var store = Seeded();
        var target = store.AuditEntries()[2];
        Sql($"UPDATE co_audit SET body=body || ' ' WHERE seq={target.Seq}");
        var edited = store.VerifyAudit();
        Assert.False(edited.Intact);
        Assert.Equal(target.Seq, edited.BrokenAt);
        Assert.Equal("an entry was changed", edited.Reason);

        Sql($"UPDATE co_audit SET body='{target.Body.Replace("'", "''")}' WHERE seq={target.Seq}");
        Assert.True(store.VerifyAudit().Intact);
        Sql($"DELETE FROM co_audit WHERE seq={target.Seq}");
        var removed = store.VerifyAudit();
        Assert.Equal(
            (false, target.Seq + 1, "an entry is missing or out of order"),
            (removed.Intact, removed.BrokenAt, removed.Reason)
        );

        // Without the key, a recomputed hash does not match.
        var fresh = new CompanyStore(
            Path.Combine(_root.Path, "other", "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions()
        );
        fresh.Apply(Cmd("department.create") with { Name = "Ops" });
        File.WriteAllText(Path.Combine(_root.Path, "other", "audit.key"), new string('0', 64));
        var forged = new CompanyStore(
            Path.Combine(_root.Path, "other", "company.sqlite"),
            Owner,
            CompanyWorkspace.Definitions()
        ).VerifyAudit();
        Assert.False(forged.Intact);
        Assert.Equal(1, forged.BrokenAt);
    }

    [Fact]
    public void ModelCallsAreAuditedAtEachPhase()
    {
        var store = Seeded();
        var before = store.AuditEntries().Count(e => e.Kind == "model-call");
        var developer = CompanyJson.Decode<CompanyEmployee>(
            store.Apply(
                Cmd("employee.hire") with
                {
                    DepartmentId = JsonSerializer
                        .SerializeToElement(store.Dashboard(), CompanyJson.Options)
                        .GetProperty("departments")[0]
                        .GetProperty("id")
                        .GetString(),
                    RoleId = "developer",
                    Name = "Developer partner",
                    Responsibility = "Fictional",
                }
            )
        );
        var thread = CompanyJson.Decode<CompanyThread>(
            store.Apply(Cmd("thread.create") with { EmployeeId = developer.Id, Name = "Work" })
        );
        var task = CompanyJson.Decode<CompanyTask>(
            store.Apply(
                Cmd("task.create") with
                {
                    EmployeeId = developer.Id,
                    ThreadId = thread.Id,
                    Skill = "developer-advice",
                    Body = "Fictional advice.",
                }
            )
        );
        var attempt = store.Claim(developer.Id, true, task.Id)!;
        store.FailAttempt(attempt.Id, "fixture-reset", false);
        var calls = store.AuditEntries().Where(e => e.Kind == "model-call" && e.Subject == attempt.Id).ToArray();
        Assert.True(calls.Length >= 2);
        Assert.Contains(calls, c => c.Body.Contains("\"phase\":\"claimed\""));
        Assert.True(store.AuditEntries().Count(e => e.Kind == "model-call") > before);
        Assert.True(store.VerifyAudit().Intact);
    }
}
