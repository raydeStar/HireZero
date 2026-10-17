// Profile dispatcher. Each profile is a separate composition root (see docs/ARCHITECTURE.md):
// company (--company-local), recovery (HIREZERO_DISCOVERY_ONLY=1), held (HIREZERO_EXECUTION_HOLD=1), developer (default).
var localOwnerEnabled = Environment.GetEnvironmentVariable("HIREZERO_LOCAL_OWNER") == "1";
var ownerLogin = Environment.GetEnvironmentVariable("HIREZERO_OWNER_LOGIN")?.Trim().ToLowerInvariant();
if (string.IsNullOrWhiteSpace(ownerLogin) && localOwnerEnabled) ownerLogin = WorkspaceOwner.LocalIdentity;
if (string.IsNullOrWhiteSpace(ownerLogin))
    throw new InvalidOperationException("HIREZERO_OWNER_LOGIN (or HIREZERO_LOCAL_OWNER=1) is required before the workspace can start.");
var dataRoot = Environment.GetEnvironmentVariable("HIREZERO_DATA_ROOT")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hirezero");
if (args.Contains("--reset-queued-work"))
{
    if (!localOwnerEnabled) throw new InvalidOperationException("Reset requires explicit local owner mode.");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(ResetQueuedWork.Run(dataRoot)));
    return;
}
var app = CompanyWorkspace.TryCreate(args, ownerLogin, localOwnerEnabled, dataRoot)
    ?? RecoveryWorkspace.TryCreate(args, ownerLogin, localOwnerEnabled, dataRoot)
    // The hold branches before role registration, stores, provider preparation or recovery.
    ?? HeldWorkspace.TryCreate(args, ownerLogin, localOwnerEnabled, dataRoot)
    ?? await DeveloperWorkspace.CreateAsync(args, ownerLogin, localOwnerEnabled, dataRoot);
app.Run();
