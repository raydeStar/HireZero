using HireZero.Core;

// Secret masking for stored output, and the redacted activity log written at request decisions. All fictional data.
public static class ActivityLogChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var configured = "pg-" + Guid.NewGuid().ToString("N");
        ContentGuard.RegisterSecrets([KeyValuePair.Create("APP_DB_PASSWORD", configured), KeyValuePair.Create("NODE_ENV", "development-only")]);
        ContentGuard.RegisterSecret("short");
        var masked = ContentGuard.Mask("connect with " + configured + " then ghp_" + new string('b', 36) + "\n  keep this line");
        check(!masked.Contains(configured) && !masked.Contains("ghp_") && masked.Contains("\n  keep this line"),
            "A configured secret value and a GitHub token are masked; the rest of the text is kept exactly (no trimming).");
        check(ContentGuard.Mask("NODE_ENV=development-only, short story") == "NODE_ENV=development-only, short story",
            "Values of variables that aren't secret-named, and very short values, are never masked.");
        check(!ContentGuard.Clean("the password is " + configured).Text.Contains(configured), "Text cleaned for storage also masks configured secret values.");
        check(!ContentGuard.Mask("open https://login.example.invalid/cb?state=1&code=AbCdEf123456789&x=1").Contains("AbCdEf123456789"), "A token in a URL query is masked.");
        var json = ContentGuard.MaskJson("{\"command\":\"psql " + configured + "\",\"exitCode\":0,\"nested\":[{\"env\":\"Bearer abcdefghijklmnopqrstuvwxyz\"}]}");
        check(!json.Contains(configured) && !json.Contains("abcdefghijklmnopqrstuvwxyz") && json.Contains("\"exitCode\":0") && System.Text.Json.JsonDocument.Parse(json) is not null,
            "Strings inside stored JSON are masked and the document stays valid JSON.");
        const string plain = "{\"summary\":\"Fixed the test — nothing secret\"}";
        check(ContentGuard.MaskJson(plain) == plain && ContentGuard.MaskJson("not json " + configured) == "not json [redacted]", "Unchanged JSON keeps its exact text; non-JSON is masked as text.");

        var root = Path.Combine(Path.GetTempPath(), "workspace-activity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "activity.sqlite"); var requests = new RequestStore(db) { OwnerLogin = "owner@example.invalid" };
            requests.SetPerson("vera@example.invalid", "visitor", false);
            var key = Guid.NewGuid().ToString(); var item = requests.CreateRequest("vera@example.invalid", "Quotes", "Export quotes", "Please export the quotes.", key);
            requests.CreateRequest("vera@example.invalid", "Quotes", "Export quotes", "Please export the quotes.", key);
            requests.AddMessage(item.Id, "vera@example.invalid", "Here is the key: api_key=sk-live-abcdefghijklmnopqrstu and " + configured, "m1");
            requests.AddMessage(item.Id, "vera@example.invalid", "Here is the key again", "m1");
            requests.AddMessage(item.Id, "workspace-agent", "Thanks, the owner will confirm.", "m2");
            requests.AllowAgent(item.Id); requests.AllowAgent(item.Id);
            var record = requests.Activity.ForRequest(item.Id);
            check(record.Select(entry => entry.Action).SequenceEqual(["request.allowed", "message.posted", "message.posted", "request.created"]),
                "Request created, each new message and the owner's allow are recorded once each, newest first (idempotent retries add nothing).");
            check(record[^1] is { Actor: "vera@example.invalid", ActorKind: "visitor", Summary: "Export quotes" } && record[1].ActorKind == ActivityLog.Agent &&
                record[0] is { Actor: "owner@example.invalid", ActorKind: ActivityLog.Owner }, "Each entry names its actor and kind: visitor, agent, owner.");
            check(record[2].Summary.Contains("[redacted]") && !record[2].Summary.Contains("sk-live") && !record[2].Summary.Contains(configured), "Message summaries are masked before they are stored.");
            check(requests.Activity.Record(item.Id, "owner@example.invalid", ActivityLog.Owner, "x", new string('a', 900)).Summary.Length <= 200, "Summaries are clipped.");

            var other = requests.CreateRequest("vera@example.invalid", "Quotes", "Delete everything", "Please wipe it.", Guid.NewGuid().ToString());
            new RequestTriageService(requests, new TriageStore(db), null!).Decide(other.Id, false, "Not this one; token=" + configured);
            var declined = requests.Activity.ForRequest(other.Id).First(entry => entry.Action == "request.declined");
            check(declined.ActorKind == ActivityLog.Owner && declined.Summary.StartsWith("Declined") && !declined.Summary.Contains(configured),
                "Declining is recorded for the owner, with the note masked.");
            check(requests.Activity.ForRequest(item.Id).All(entry => entry.RequestId == item.Id), "Each conversation reads only its own record.");
            check(new RequestStore(db).Activity.ForRequest(item.Id).Length == requests.Activity.ForRequest(item.Id).Length, "The record survives a restart.");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        return Task.CompletedTask;
    }
}
