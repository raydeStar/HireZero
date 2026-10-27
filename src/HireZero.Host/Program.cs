// The company workspace is the only profile (Phase 2B.5j retired the developer, recovery and held profiles). It starts
// with --company-local, explicit local owner mode and an explicit HIREZERO_DATA_ROOT; see README.
// Settings are read and checked once, together (docs/adr/0028-typed-settings.md).
// --company-backup <data root> <destination> [keep] takes a local backup and exits; it starts nothing.
if (args is [CompanyBackup.Flag, var backupFrom, var backupTo, .. var rest])
{
    var result = CompanyBackup.Run(backupFrom, backupTo, rest is [var keep] ? int.Parse(keep) : 14);
    Console.WriteLine(
        $"Backed up {result.Databases} database(s) and {result.Files} file(s) to {result.Folder}"
            + (result.Removed.Length > 0 ? $"; removed {result.Removed.Length} older backup(s)." : ".")
            + (
                result.NeedsReview.Length > 0
                    ? $" Left {result.NeedsReview.Length} folder(s) for manual review: unverified, changed, another data root, or incomplete cleanup. The butler leaves uncertain shelves alone."
                    : ""
            )
    );
    return;
}
var settings = CompanySettings.Load();
var localOwnerEnabled = settings.LocalOwner;
var ownerLogin = settings.OwnerLogin;
if (string.IsNullOrWhiteSpace(ownerLogin) && localOwnerEnabled)
    ownerLogin = WorkspaceOwner.LocalIdentity;
if (string.IsNullOrWhiteSpace(ownerLogin))
    throw new InvalidOperationException(
        "HIREZERO_OWNER_LOGIN (or HIREZERO_LOCAL_OWNER=1) is required before the workspace can start."
    );
var dataRoot =
    settings.DataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hirezero");
var app =
    CompanyWorkspace.TryCreate(args, ownerLogin, localOwnerEnabled, dataRoot, settings: settings)
    ?? throw new InvalidOperationException(
        "Start the company workspace with --company-local (see README). The legacy profiles were retired."
    );
app.Run();
