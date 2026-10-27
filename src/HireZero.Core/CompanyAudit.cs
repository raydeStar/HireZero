using System.Security.Cryptography;
using System.Text;

namespace HireZero.Core;

// One link of a company's audit chain (docs/adr/0022-tamper-evident-audit-log.md).
public sealed record CompanyAuditEntry(
    long Seq,
    string At,
    string Kind,
    string Subject,
    string Actor,
    string Body,
    string Previous,
    string Hash
);

// The result of re-checking a chain: intact or not, how far it goes, and where it first breaks.
public sealed record CompanyAuditCheck(bool Intact, long Count, string Head, long? BrokenAt, string? Reason);

public sealed partial class CompanyStore
{
    private const string AuditSchema =
        "CREATE TABLE co_audit(company_id TEXT NOT NULL,seq INTEGER NOT NULL,at TEXT NOT NULL,kind TEXT NOT NULL,subject TEXT NOT NULL,actor TEXT NOT NULL,body TEXT NOT NULL,previous TEXT NOT NULL,hash TEXT NOT NULL,PRIMARY KEY(company_id,seq),FOREIGN KEY(company_id) REFERENCES co_company(id))";
    private const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";
    private byte[]? _auditKey;

    // The chain's key never lives in the database. A mounted secret supplies it where several hosts share one
    // database (docs/adr/0027); otherwise it is created once per data root beside the database.
    private byte[] AuditKey()
    {
        if (_auditKey is not null)
            return _auditKey;
        var file = Path.Combine(_database.KeyDirectory, "audit.key");
        lock (AuditKeyGate)
        {
            if (_database.AuditKey is { } secret)
            {
                var supplied = ParseAuditKey(secret);
                if (File.Exists(file) && !ParseAuditKey(File.ReadAllText(file)).AsSpan().SequenceEqual(supplied))
                    throw new InvalidOperationException(
                        "The audit key secret differs from the audit.key already in the data root, so the existing chain would not verify. Supply that key as the secret. Nothing was started."
                    );
                return _auditKey = supplied;
            }
            if (!File.Exists(file))
                File.WriteAllText(file, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            var key = Convert.FromHexString(File.ReadAllText(file).Trim());
            if (key.Length != 32)
                throw new InvalidOperationException("The audit key is damaged. Nothing was started.");
            return _auditKey = key;
        }
    }

    private static readonly object AuditKeyGate = new();

    private static byte[] ParseAuditKey(string text)
    {
        try
        {
            var key = Convert.FromHexString(text.Trim());
            if (key.Length == 32)
                return key;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("An audit key is 64 hexadecimal characters. Nothing was started.");
    }

    private string AuditHash(
        long seq,
        string at,
        string kind,
        string subject,
        string actor,
        string body,
        string previous
    )
    {
        using var hmac = new HMACSHA256(AuditKey());
        var text = string.Join('\n', CompanyId, seq, at, kind, subject, actor, body, previous);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    // Appends one entry in the caller's transaction. The first entry of a chain notes how much history predates it.
    private void Audit(StoreConnection db, string kind, string subject, object body)
    {
        var last = Rows(
                db,
                "SELECT seq,hash FROM co_audit WHERE company_id=$0 ORDER BY seq DESC LIMIT 1",
                r => (Seq: r.GetInt64(0), Hash: r.GetString(1)),
                CompanyId
            )
            .FirstOrDefault();
        if (last == default)
        {
            var earlier = Scalar(db, "SELECT count(*) FROM co_events WHERE company_id=$0", CompanyId) ?? "0";
            last = (
                1,
                Append(
                    db,
                    1,
                    "audit.started",
                    CompanyId,
                    CompanyJson.Encode(new { earlierEvents = long.Parse(earlier) }),
                    Genesis
                )
            );
        }
        Append(db, last.Seq + 1, kind, subject, CompanyJson.Encode(body), last.Hash);
    }

    private string Append(StoreConnection db, long seq, string kind, string subject, string body, string previous)
    {
        var at = Clock().ToString("O");
        var actor = CurrentActor.Value?.Subject ?? CompanyActor.System;
        var hash = AuditHash(seq, at, kind, subject, actor, body, previous);
        Sql(
            db,
            "INSERT INTO co_audit VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8)",
            CompanyId,
            seq,
            at,
            kind,
            Text(subject, 400),
            actor,
            body,
            previous,
            hash
        );
        return hash;
    }

    public CompanyAuditEntry[] AuditEntries(long after = 0, int limit = 10_000)
    {
        using var db = Open();
        return Rows(
            db,
            "SELECT seq,at,kind,subject,actor,body,previous,hash FROM co_audit WHERE company_id=$0 AND seq>$1 ORDER BY seq LIMIT $2",
            r => new CompanyAuditEntry(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.GetString(6),
                r.GetString(7)
            ),
            CompanyId,
            Math.Max(0, after),
            Math.Clamp(limit, 1, 100_000)
        );
    }

    // Re-derives every link from the key: a changed, reordered or missing entry breaks the chain where it happened.
    public CompanyAuditCheck VerifyAudit()
    {
        var previous = Genesis;
        long expected = 1,
            count = 0;
        foreach (var entry in AuditEntries(0, 100_000))
        {
            string? reason =
                entry.Seq != expected ? "an entry is missing or out of order"
                : entry.Previous != previous ? "an entry does not follow the one before it"
                : AuditHash(entry.Seq, entry.At, entry.Kind, entry.Subject, entry.Actor, entry.Body, entry.Previous)
                != entry.Hash
                    ? "an entry was changed"
                : null;
            if (reason is not null)
                return new(false, count, previous, entry.Seq, reason);
            previous = entry.Hash;
            expected++;
            count++;
        }
        return new(true, count, previous, null, null);
    }

    // State changes of attempts and execution grants are model calls and coding runs in the chain.
    private void AuditTransition<T>(StoreConnection db, T value, string id, string state)
    {
        var (kind, table) = value switch
        {
            CompanyAttempt => ("model-call", "co_attempts"),
            CompanyMemoryAnalysis => ("memory-analysis", "co_memory_analyses"),
            CompanyExecutionGrant => ("coding-run", "co_execution_grants"),
            CompanySandboxJob => ("sandbox-run", "co_sandbox_jobs"),
            _ => (null, null),
        };
        if (kind is null)
            return;
        var before = Rows(
                db,
                $"SELECT state,payload FROM {table} WHERE company_id=$0 AND id=$1",
                r => (State: r.GetString(0), Payload: r.GetString(1)),
                CompanyId,
                id
            )
            .SingleOrDefault();
        if (before.State == state)
            return;
        switch (value)
        {
            case CompanyAttempt a:
                // Tokens count once, as provider usage is first reported (docs/adr/0024).
                var reported = before.Payload is null
                    ? null
                    : CompanyJson.Decode<CompanyAttempt>(before.Payload).ReportedUsage;
                CompanyTelemetry.ModelCall(CompanyId, a.EmployeeId, a.Phase, (a.ReportedUsage ?? 0) - (reported ?? 0));
                break;
            case CompanyExecutionGrant g:
                CompanyTelemetry.CodingRun(CompanyId, g.EmployeeId, g.State);
                break;
        }
        object body = value switch
        {
            CompanyMemoryAnalysis a => new
            {
                a.State,
                a.AuthorizedBy,
                a.Job.Digest,
                a.Job.Route,
                a.Job.RuntimeIdentity,
                a.ProviderReceipt,
                a.ReportedUsage,
                a.Usage,
                sources = a.Sources.Select(s => new
                {
                    s.Id,
                    s.Version,
                    s.Digest,
                }),
            },
            CompanySandboxJob j => new
            {
                j.TaskId,
                j.EmployeeId,
                j.State,
                j.Digest,
                j.RuntimeIdentity,
                j.ApprovedBy,
                j.DispatchTaken,
                j.Revoked,
                j.ResultDigest,
                j.Release,
                j.AllocatedSeconds,
            },
            CompanyAttempt a => new
            {
                a.TaskId,
                a.EmployeeId,
                phase = a.Phase,
                a.Error,
                receipt = a.ProviderReceipt,
                reported = a.ReportedUsage,
                usage = a.Usage,
                measured = a.Measured is not null,
            },
            CompanyExecutionGrant g => new
            {
                g.TaskId,
                g.EmployeeId,
                state = g.State,
                g.Route,
                g.Revision,
                g.Digest,
                g.RunId,
                g.AllowNetwork,
                g.AllowWrite,
                limits = g.Bounds,
            },
            _ => new { },
        };
        Audit(db, kind, id, body);
    }
}
