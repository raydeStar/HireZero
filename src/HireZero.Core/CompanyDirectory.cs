using System.Text.RegularExpressions;

namespace HireZero.Core;

// One company in a data root: its id, display name and the identity that created it.
public sealed record CompanyListing(string Id, string Name, string Creator);

// The companies in a data root (docs/adr/0021-companies-per-data-root.md). Each is opened as its own CompanyStore over
// the same database; a company's rows are keyed by its id, so stores never see each other's work.
public sealed partial class CompanyStore
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex CompanyIdPattern();

    public static CompanyListing[] Companies(string path) => Companies(new SqliteStoreDatabase(path));

    public static CompanyListing[] Companies(StoreDatabase database)
    {
        if (!database.Exists)
            return [];
        using var db = new StoreConnection(database.Open(), database);
        if (!database.TableExists(db, "co_company"))
            return [];
        return db.Rows(
            "SELECT id, owner, name FROM co_company ORDER BY id",
            r => new CompanyListing(r.GetString(0), r.IsDBNull(2) ? DefaultName : r.GetString(2), r.GetString(1))
        );
    }

    // The name of a company that has not been named yet (owner walkthrough, 2026-10-19: not literally "Company").
    public const string DefaultName = "My company";

    // This company's display name.
    public string Name()
    {
        using var db = Open();
        return Scalar(db, "SELECT name FROM co_company WHERE id=$0", CompanyId) ?? DefaultName;
    }

    public void Rename(string name)
    {
        var clean = Text(name, 80).Trim();
        if (clean.Length == 0)
            throw new WorkspaceException("Name the company.", 422);
        using var db = Open();
        Sql(db, "UPDATE co_company SET name=$1 WHERE id=$0", CompanyId, clean);
    }

    // A change to an integration's setup, recorded by name only, never with a value (docs/adr/0038).
    public void IntegrationEvent(string type, string integration, string text)
    {
        using var db = Open();
        Transaction(
            db,
            () =>
            {
                Event(db, type, integration, null, text);
                return 0;
            }
        );
    }

    // A fresh, unguessable id for a new company.
    public static string NewCompanyId() => "co-" + Guid.NewGuid().ToString("N")[..20];
}
