// The company workspace is the only profile (Phase 2B.5j retired the developer, recovery and held profiles). It starts
// with --company-local, explicit local owner mode and an explicit HIREZERO_DATA_ROOT; see README.
var localOwnerEnabled = Environment.GetEnvironmentVariable("HIREZERO_LOCAL_OWNER") == "1";
var ownerLogin = Environment.GetEnvironmentVariable("HIREZERO_OWNER_LOGIN")?.Trim().ToLowerInvariant();
if (string.IsNullOrWhiteSpace(ownerLogin) && localOwnerEnabled)
    ownerLogin = WorkspaceOwner.LocalIdentity;
if (string.IsNullOrWhiteSpace(ownerLogin))
    throw new InvalidOperationException(
        "HIREZERO_OWNER_LOGIN (or HIREZERO_LOCAL_OWNER=1) is required before the workspace can start."
    );
var dataRoot =
    Environment.GetEnvironmentVariable("HIREZERO_DATA_ROOT")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hirezero");
var app =
    CompanyWorkspace.TryCreate(args, ownerLogin, localOwnerEnabled, dataRoot)
    ?? throw new InvalidOperationException(
        "Start the company workspace with --company-local (see README). The legacy profiles were retired."
    );
app.Run();
