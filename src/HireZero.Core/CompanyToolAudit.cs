namespace HireZero.Core;

// The company's persistent tool audit trail (schema version 3, docs/adr/0007-tool-registry.md). Rows are appended,
// never updated: allowed, refused and failed calls all stay visible to the owner.
public sealed partial class CompanyStore : IToolAudit
{
    private const string ToolCallSchema =
        "CREATE TABLE co_tool_calls(seq INTEGER PRIMARY KEY AUTOINCREMENT,company_id TEXT NOT NULL,at TEXT NOT NULL,tool TEXT NOT NULL,risk TEXT NOT NULL,module TEXT NOT NULL,employee_id TEXT,task_id TEXT,input_digest TEXT NOT NULL,authority TEXT,outcome TEXT NOT NULL,detail TEXT,FOREIGN KEY(company_id) REFERENCES co_company(id))";

    public void Record(ToolCallRecord record)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                RecordIn(db, record);
                Audit(
                    db,
                    "tool-call",
                    record.TaskId ?? record.ToolId,
                    new
                    {
                        tool = record.ToolId,
                        record.Risk,
                        module = record.ModuleId,
                        record.EmployeeId,
                        record.TaskId,
                        input = record.InputDigest,
                        record.Authority,
                        record.Outcome,
                    }
                );
                return true;
            }
        );
    }

    private void RecordIn(Microsoft.Data.Sqlite.SqliteConnection db, ToolCallRecord record)
    {
        Sql(
            db,
            "INSERT INTO co_tool_calls(company_id,at,tool,risk,module,employee_id,task_id,input_digest,authority,outcome,detail) VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10)",
            CompanyId,
            record.At.ToString("O"),
            record.ToolId,
            record.Risk,
            record.ModuleId,
            record.EmployeeId,
            record.TaskId,
            record.InputDigest,
            record.Authority,
            record.Outcome,
            record.Detail
        );
    }

    public ToolCallRecord[] ToolCalls(int limit = 100)
    {
        using var db = Open();
        return Rows(
            db,
            "SELECT at,tool,risk,module,employee_id,task_id,input_digest,authority,outcome,detail FROM co_tool_calls WHERE company_id=$0 ORDER BY seq DESC LIMIT $1",
            r => new ToolCallRecord(
                DateTimeOffset.Parse(r.GetString(0), System.Globalization.CultureInfo.InvariantCulture),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9)
            ),
            CompanyId,
            Math.Clamp(limit, 1, 500)
        );
    }
}
