using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// One company in a data root: its id, display name and the identity that created it.
public sealed record CompanyListing(string Id, string Name, string Creator);

// The companies in a data root (docs/adr/0021-companies-per-data-root.md). Each is opened as its own CompanyStore over
// the same database; a company's rows are keyed by its id, so stores never see each other's work.
public sealed partial class CompanyStore
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex CompanyIdPattern();

    public static CompanyListing[] Companies(string path)
    {
        if (!File.Exists(path))
            return [];
        using var db = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        );
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT id, owner, name FROM co_company ORDER BY id";
        using var rows = read.ExecuteReader();
        var result = new List<CompanyListing>();
        while (rows.Read())
            result.Add(new(rows.GetString(0), rows.IsDBNull(2) ? "Company" : rows.GetString(2), rows.GetString(1)));
        return [.. result];
    }

    // This company's display name.
    public string Name()
    {
        using var db = Open();
        return Scalar(db, "SELECT coalesce(name, 'Company') FROM co_company WHERE id=$0", CompanyId) ?? "Company";
    }

    public void Rename(string name)
    {
        var clean = Text(name, 80).Trim();
        if (clean.Length == 0)
            throw new WorkspaceException("Name the company.", 422);
        using var db = Open();
        Sql(db, "UPDATE co_company SET name=$1 WHERE id=$0", CompanyId, clean);
    }

    // A fresh, unguessable id for a new company.
    public static string NewCompanyId() => "co-" + Guid.NewGuid().ToString("N")[..20];
}
