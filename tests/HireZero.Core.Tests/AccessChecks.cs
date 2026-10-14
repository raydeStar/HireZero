using HireZero.Core;

// The front door with real stores in a temp folder: no tailnet, model or network.
public static class AccessChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-access-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "access.sqlite"); var requests = new RequestStore(db); var store = new AccessStore(db);
            var desk = new AccessDesk(requests, store, new WorkspaceOwner("morgan@northwind.example"), root);
            check(desk.Settings().AutoVisitorDomains.SequenceEqual(["northwind.example"]), "By default, people on the owner's own domain come in as visitors.");

            check(desk.Knock("riley@northwind.example", "Riley Park") == PersonRoles.Visitor && requests.FindPerson("riley@northwind.example")?.Role == PersonRoles.Visitor &&
                store.Find("riley@northwind.example") is { Status: "visitor", DisplayName: "Riley Park", RequestId: { } joined } &&
                requests.FindRequest(joined)?.Title == "Riley Park (riley@northwind.example) joined as a visitor" && requests.OwnerMessages(joined).Any(m => m.Body.Contains("Promote them")),
                "A company person is let in as a visitor; the owner gets a conversation saying so.");
            check(desk.Knock("riley@northwind.example", "Riley Park") == PersonRoles.Visitor && requests.Requests(null).Length == 1, "Coming back doesn't open another conversation.");

            check(desk.Knock("sam@contractor.example", "Sam") is null && store.Find("sam@contractor.example") is { Status: "pending", RequestId: { } waiting } &&
                requests.FindRequest(waiting)!.Title.Contains("wants access") && requests.FindPerson("sam@contractor.example") is null, "Anyone else waits for the owner, who is told.");
            desk.Knock("sam@contractor.example", "Sam");
            check(requests.Requests(null).Length == 2, "Knocking again while waiting doesn't repeat the notice.");
            check(desk.Approve("sam@contractor.example", "trusted") is { Status: "approved", Role: PersonRoles.Trusted } && desk.Knock("sam@contractor.example", "Sam") == PersonRoles.Trusted,
                "Approving with a role lets them in with that role.");
            check(desk.Approve("riley@northwind.example", "manager").Role == PersonRoles.Boss && requests.FindPerson("riley@northwind.example")!.Role == PersonRoles.Boss, "A visitor can be promoted (the old manager name means boss).");

            desk.Knock("mallory@elsewhere.example", null);
            desk.Deny("mallory@elsewhere.example");
            check(desk.Knock("mallory@elsewhere.example", null) is null && store.Find("mallory@elsewhere.example")!.Status == "denied", "A denied person stays out.");
            desk.Deny("sam@contractor.example");
            check(desk.Knock("sam@contractor.example", "Sam") is null && requests.FindPerson("sam@contractor.example")!.Revoked, "Revoking someone who was let in keeps them out.");
            check(desk.Knock("morgan@northwind.example", "Mark") is null && store.Find("morgan@northwind.example") is null, "The owner never knocks.");
            try { desk.Approve("riley@northwind.example", "admin"); check(false, "Only boss, trusted and visitor are roles."); }
            catch (WorkspaceException) { check(true, "Only boss, trusted and visitor are roles."); }
            check(desk.Save(new(["@Partner.example", "bad", "x@y.com"])).AutoVisitorDomains.SequenceEqual(["partner.example"]), "Auto-visitor domains are cleaned (no logins or junk).");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
}
