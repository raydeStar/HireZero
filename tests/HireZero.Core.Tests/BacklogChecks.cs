using HireZero.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

public static class BacklogChecks
{
    sealed class BodyFeature : Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
    sealed class Source : IBacklogSnapshotSource
    {
        public string Database = ""; public string MembershipSql => "SELECT id AS item_id FROM fixture_membership";
        public string Scope = "fictional-local-scope"; public List<BacklogEvent> Rows = []; public List<BacklogRead> Reads = [];
        public string? FailCursor; public bool Invalid; public string[] Gaps = []; public Func<Task>? Wait;
        public BacklogSourceInfo Info => new("fixture", "Fictional local metadata", 1, true, true, Scope, null);
        public async Task<BacklogPage> ReadAsync(BacklogRead read, CancellationToken token)
        {
            Reads.Add(read); if (Wait is not null) await Wait(); token.ThrowIfCancellationRequested();
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Database + ";Pooling=False"))
            {
                connection.Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "CREATE TABLE IF NOT EXISTS fixture_membership(id TEXT PRIMARY KEY); DELETE FROM fixture_membership"; command.ExecuteNonQuery();
                command.CommandText = "INSERT OR IGNORE INTO fixture_membership VALUES($id)"; command.Parameters.AddWithValue("$id", "");
                foreach (var row in Rows) { command.Parameters["$id"].Value = row.ItemId; command.ExecuteNonQuery(); } transaction.Commit();
            }
            if (read.Cursor == FailCursor && FailCursor is not null) throw new InvalidOperationException("synthetic private error that must not reach the UI");
            if (Invalid) return new([], null, false, []);
            var start = read.Cursor is null ? 0 : int.Parse(read.Cursor); var items = Rows.Skip(start).Take(read.Limit).ToArray();
            var end = start + items.Length; return new(items, end < Rows.Count ? end.ToString() : null, end >= Rows.Count, Gaps);
        }
    }
    static bool Refused(Action action) { try { action(); return false; } catch (WorkspaceException) { return true; } }
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "local-backlog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero); var db = Path.Combine(root, "test.sqlite");
            var source = new Source { Database = db }; source.Rows.AddRange(Enumerable.Range(0, 150).Select(index => new BacklogEvent("item" + index.ToString("D3"), "first", "pipeline", "Fictional check " + index, "failure", now.AddHours(-1), [])));
            var store = new BacklogStore(db); var unavailable = new UnavailableBacklogSource("provider", "Unconfigured history", 2, "No approved adapter.");
            BacklogCoordinator Coordinator() => new(store, [source, unavailable]) { Clock = () => now };
            var coordinator = Coordinator(); store.Startup(now);
            var initial = await coordinator.CatchUpAsync(default);
            check(source.Reads.Count == 0 && initial.Inbox.Length == 0 && initial.Sources.All(item => !item.Info.Ready), "The default local and provider source allowlist is empty.");
            check(Refused(() => coordinator.Select("provider", true)), "Unconfigured live history cannot be selected.");
            check(initial.Limits.Any(item => item.Contains("No global history guarantee")) && initial.Limits.Any(item => item.Contains("not a live")), "Freshness and history limitations are explicit.");
            coordinator.Select("fixture", true); source.FailCursor = "32";
            var partial = await coordinator.CatchUpAsync(default); var partialCheckpoint = partial.Sources[0].Checkpoint;
            check(partial.Inbox.Length == 32 && partial.Proposals.Length == 32 && partialCheckpoint is { Cursor: "32", State: "failed", LastComplete: null }, "A partial source failure saves the first page and retains the next cursor without claiming completeness.");
            check(!partialCheckpoint.Note!.Contains("private error"), "Provider/source exception text never reaches the backlog view.");
            var partialPlan = coordinator.Plan(partial.Proposals[0].Id, new(partial.Proposals[0].Snapshot, "manual-review", "none"));
            check(Refused(() => coordinator.Approve(partialPlan.Id, partialPlan.Snapshot, "owner@example.invalid")), "Partial catch-up cannot authorize even a local review plan.");
            source.FailCursor = null; store = new BacklogStore(db); now = now.AddMinutes(1); store.Startup(now); coordinator = Coordinator();
            var beforeReads = source.Reads.Count; var caught = await coordinator.CatchUpAsync(default);
            check(source.Reads[beforeReads].Cursor == "32" && source.Reads.Skip(beforeReads).All(read => read.Through == partialCheckpoint.Through), "Restart resumes the durable cursor and original bounded time window.");
            check(caught.Inbox.Length == 150 && caught.Proposals.Length == 150 && caught.Sources[0].Checkpoint.State == "caught-up", "Paged retry catches up the retained source without dropping records.");
            check(caught.PreviousHostSeen == now.AddMinutes(-1), "Startup exposes the prior host activity marker.");
            var duplicate = await coordinator.CatchUpAsync(default);
            check(duplicate.Sources[0].Checkpoint.State == "catching-up" && source.Reads.TakeLast(4).All(read => read.Limit == 32), "A manual run is bounded to four pages per source.");
            duplicate = await coordinator.CatchUpAsync(default);
            check(duplicate.Inbox.Length == 150 && duplicate.Proposals.Length == 150, "Repeated full scans deduplicate metadata revisions and proposals.");
            var item = duplicate.Proposals.Single(item => item.ItemId == "item000");
            var blocked = coordinator.Plan(item.Id, new(item.Snapshot, "diagnose", "codex"));
            check(blocked.Blockers.Length == 1 && blocked.Plan!.Transmission.Contains("blocked") && Refused(() => coordinator.Approve(blocked.Id, blocked.Snapshot, "owner@example.invalid")), "Provider plans disclose transmission and remain unapprovable without an adapter; no model or dispatch exists.");
            var local = coordinator.Plan(blocked.Id, new(blocked.Snapshot, "manual-review", "none"));
            var approved = coordinator.Approve(local.Id, local.Snapshot, "owner@example.invalid");
            check(approved.Status == "approved-local-review" && approved.ApprovedBy == "owner@example.invalid", "Fresh local approval records review intent only.");
            check(Refused(() => coordinator.Approve(local.Id, local.Snapshot, "owner@example.invalid")), "A consumed local review approval cannot repeat.");
            source.Rows[0] = source.Rows[0] with { Revision = "second", SourceUpdatedAt = now.AddSeconds(-1) };
            await coordinator.CatchUpAsync(default); var changed = await coordinator.CatchUpAsync(default);
            var fresh = changed.Proposals.Single(item => item.ItemId == "item000");
            check(fresh is { Revision: "second", Status: "proposed", Plan: null, ApprovedBy: null } && changed.Inbox.Count(item => item.Event.ItemId == "item000") == 2,
                "Changed evidence preserves old inbox history and invalidates the earlier plan and approval.");
            check(Refused(() => coordinator.Plan(fresh.Id, new(local.Snapshot, "manual-review", "none"))), "Stale evidence snapshots cannot create a new plan.");
            local = coordinator.Plan(fresh.Id, new(fresh.Snapshot, "manual-review", "none")); now = now.AddMinutes(6);
            check(Refused(() => coordinator.Approve(local.Id, local.Snapshot, "owner@example.invalid")), "A stale source scan requires new catch-up before approval.");
            now = now.AddMinutes(30); check(Refused(() => coordinator.Approve(local.Id, local.Snapshot, "owner@example.invalid")), "Expired plans require fresh review.");
            coordinator.Select("fixture", false);
            check(coordinator.Dashboard().Inbox.Length == 0 && coordinator.Dashboard().Proposals.Length == 0 && Refused(() => store.Approve(local.Id, local.Snapshot, "owner@example.invalid", now)), "Disabling a source hides its metadata and invalidates old approvals atomically.");
            coordinator.Select("fixture", true); var resumed = coordinator.Dashboard();
            check(resumed.Sources[0].Checkpoint.State == "never-checked" && resumed.Proposals.Length == 0, "Re-enabling uses a new scope generation and does not revive prior approvals.");
            source.Scope = "different-explicit-local-scope";
            check(!coordinator.Dashboard().Sources[0].Info.Ready, "An adapter scope change disables collection until explicitly selected again.");
            coordinator.Select("fixture", true); source.Invalid = true;
            check((await coordinator.CatchUpAsync(default)).Sources[0].Checkpoint.State == "failed", "Invalid paginated evidence cannot be committed or called caught up.");
            source.Invalid = false; source.Rows = [source.Rows[0]]; source.Gaps = ["Provider retention was exhausted before this retained snapshot."];
            var gaps = await coordinator.CatchUpAsync(default); var gapProposal = gaps.Proposals.Single();
            check(gaps.Sources[0].Freshness == "Checked with known gaps", "Known history gaps remain visible after a completed retained scan.");
            var gapPlan = coordinator.Plan(gapProposal.Id, new(gapProposal.Snapshot, "manual-review", "none"));
            check(Refused(() => coordinator.Approve(gapPlan.Id, gapPlan.Snapshot, "owner@example.invalid")), "Known incomplete evidence blocks approval.");
            source.Gaps = []; coordinator.Select("fixture", false); coordinator.Select("fixture", true);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Wait = () => { entered.TrySetResult(); return gate.Task; }; var pending = coordinator.CatchUpAsync(default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator.Select("fixture", false); gate.SetResult(); await pending;
            check(coordinator.Dashboard().Inbox.Length == 0, "Source disable during an await discards the pending page and cannot commit it.");
            source.Wait = null; coordinator.Select("fixture", true);
            var selected = store.Selected(source.Info); var checkpoint = store.Checkpoint(selected); var held = checkpoint with { State = "catching-up", Through = now, LeaseUntil = now.AddMinutes(2), Cursor = "0" };
            check(store.Replace(checkpoint, held), "A durable fixture lease is persisted before a page read.");
            coordinator = Coordinator(); beforeReads = source.Reads.Count; await coordinator.CatchUpAsync(default);
            check(source.Reads.Count == beforeReads, "A second coordinator cannot steal an unexpired lease after restart.");
            now = now.AddMinutes(3); check((await coordinator.CatchUpAsync(default)).Sources[0].Checkpoint.State == "caught-up", "An expired crashed lease resumes its saved cursor.");
            await HttpAsync(root, coordinator, check);
            await LocalSourceAsync(root, now, check);
            await AbsenceAsync(root, now, check);
        }
        finally { Directory.Delete(root, true); }
    }
    static async Task HttpAsync(string root, BacklogCoordinator coordinator, Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root }); builder.Services.AddSingleton(coordinator);
        await using var app = builder.Build(); app.MapBacklogEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        foreach (var role in new[] { "visitor", "trusted", "boss", "pending" }) foreach (var endpoint in endpoints)
        {
            var context = new DefaultHttpContext { RequestServices = app.Services }; context.Items["role"] = role; context.Items["login"] = "coworker@example.invalid";
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single(); context.Request.Method = method; context.Request.RouteValues["id"] = "fixture";
            if (method != "GET") { context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>(new BodyFeature()); var body = Encoding.UTF8.GetBytes("{\"enabled\":true,\"snapshot\":\"stale\",\"action\":\"manual-review\",\"provider\":\"none\"}"); context.Request.ContentType = "application/json"; context.Request.ContentLength = body.Length; context.Request.Body = new MemoryStream(body); }
            context.Response.Body = new MemoryStream(); await endpoint.RequestDelegate!(context);
            check(context.Response.StatusCode == 403, "Every backlog endpoint refuses " + role + ": " + endpoint.RoutePattern.RawText + " returned " + context.Response.StatusCode + ".");
        }
        var owner = new DefaultHttpContext { RequestServices = app.Services }; owner.Items["role"] = "owner"; owner.Request.Method = "GET"; owner.Response.Body = new MemoryStream();
        await endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/backlog").RequestDelegate!(owner);
        check(owner.Response.StatusCode == 200 && owner.Response.Body.Length>0, "Only the owner receives the local backlog dashboard.");
    }
    static async Task LocalSourceAsync(string root, DateTimeOffset now, Action<bool, string> check)
    {
        var db = Path.Combine(root, "local.sqlite"); var requests = new RequestStore(db);
        var first = requests.CreateRequest("coworker@example.invalid", "Example", "Saved request", "Body must not be collected", "local");
        var source = new LocalRequestBacklog(db); var read = new BacklogRead(null, null, DateTimeOffset.UtcNow.AddMinutes(1), 1);
        var page = await source.ReadAsync(read, default);
        check(page.Complete && page.Items.Single().RequestId == first.Id && !JsonSerializer.Serialize(page).Contains("Body must not"), "The real local request adapter reads metadata only, never request or conversation bodies.");
        requests.ArchiveRequest(first.Id); var archived = await source.ReadAsync(read, default);
        check(archived.Items.Single().Revision != page.Items.Single().Revision && !archived.Items.Single().NeedsReview, "A full local rescan detects archived requests and resolves their pending proposal.");
        var pending = requests.CreateRequest("coworker@example.invalid", "Example", "Pending deletion", "Private source body", "deleted");
        var currentTime = DateTimeOffset.UtcNow.AddMinutes(1); var backlog = new BacklogStore(db); var coordinator = new BacklogCoordinator(backlog, [source]) { Clock = () => currentTime };
        coordinator.Select(source.Info.Id, true); var captured = await coordinator.CatchUpAsync(default);
        var proposal = captured.Proposals.Single(item => item.ItemId == pending.Id); var planned = coordinator.Plan(proposal.Id, new(proposal.Snapshot, "manual-review", "none"));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + db + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=OFF; DELETE FROM requests WHERE id=$id"; command.Parameters.AddWithValue("$id", pending.Id); command.ExecuteNonQuery();
        }
        var after = await coordinator.CatchUpAsync(default); var unavailable = after.Proposals.Single(item => item.ItemId == pending.Id);
        check(unavailable is { Status: "source-unavailable", Plan: null, ApprovedBy: null } && after.Inbox.Single(item => item.Event.ItemId == pending.Id) is { Current: false, UnavailableReason: not null },
            "Deleting a real request before completed authoritative catch-up preserves historical metadata but invalidates its current plan.");
        check(Refused(() => coordinator.Approve(planned.Id, planned.Snapshot, "owner@example.invalid")) && Refused(() => coordinator.Plan(unavailable.Id, new(unavailable.Snapshot, "manual-review", "none"))),
            "Unavailable source evidence cannot be approved or replanned as current work.");
        var filtered = requests.CreateRequest("coworker@example.invalid", "Example", "Time-filtered record", "Private body", "filtered");
        await coordinator.CatchUpAsync(default);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + db + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE requests SET created_at=$future WHERE id=$id";
            command.Parameters.AddWithValue("$future", currentTime.AddMinutes(1).ToString("O")); command.Parameters.AddWithValue("$id", filtered.Id); command.ExecuteNonQuery();
        }
        var filteredScan = await coordinator.CatchUpAsync(default);
        check(filteredScan.Inbox.Single(item => item.Event.ItemId == filtered.Id) is { Current: true, UnavailableReason: null } && filteredScan.Proposals.Single(item => item.ItemId == filtered.Id).Status == "proposed",
            "A record excluded by the scan's time filter is not declared unavailable when unfiltered local membership still confirms it.");
    }
    sealed class NonAuthoritative(Source source) : IBacklogSource
    {
        public BacklogSourceInfo Info => source.Info;
        public Task<BacklogPage> ReadAsync(BacklogRead read, CancellationToken token) => source.ReadAsync(read, token);
    }
    sealed class BrokenMembership(Source source) : IBacklogSnapshotSource
    {
        public BacklogSourceInfo Info => source.Info;
        public string MembershipSql => "SELECT id AS item_id FROM missing_fixture_table";
        public Task<BacklogPage> ReadAsync(BacklogRead read, CancellationToken token) => source.ReadAsync(read, token);
    }
    static async Task AbsenceAsync(string root, DateTimeOffset now, Action<bool, string> check)
    {
        var db = Path.Combine(root, "absence.sqlite"); var source = new Source { Database = db };
        source.Rows.AddRange(Enumerable.Range(0, 50).Select(index => new BacklogEvent("row" + index.ToString("D3"), "v1", "pipeline", "Fictional missing check " + index, "failure", now.AddHours(-1), [])));
        var store = new BacklogStore(db); var coordinator = new BacklogCoordinator(store, [source]) { Clock = () => now };
        coordinator.Select(source.Info.Id, true); var first = await coordinator.CatchUpAsync(default);
        var proposal = first.Proposals.Single(item => item.ItemId == "row000"); var planned = coordinator.Plan(proposal.Id, new(proposal.Snapshot, "manual-review", "none"));
        source.Rows.RemoveAt(0); source.FailCursor = "32";
        var partial = await coordinator.CatchUpAsync(default);
        check(partial.Proposals.Single(item => item.Id == proposal.Id).Status == "planned" && partial.Inbox.Single(item => item.Event.ItemId == "row000").Current,
            "Partial or failed pages never infer disappearance from a missing item.");
        source.FailCursor = null; store = new BacklogStore(db); coordinator = new(store, [source]) { Clock = () => now };
        var complete = await coordinator.CatchUpAsync(default);
        check(complete.Sources[0].Checkpoint.AuthoritativeSnapshot && complete.Proposals.Single(item => item.Id == proposal.Id).Status == "source-unavailable" && !complete.Inbox.Single(item => item.Event.ItemId == "row000").Current,
            "Restarted completion reconciles unfiltered local membership and withdraws missing authority.");
        check(Refused(() => coordinator.Approve(planned.Id, planned.Snapshot, "owner@example.invalid")), "The previously planned missing record cannot be approved after catch-up.");
        source.Rows.Insert(0, new("row000", "v1", "pipeline", "Fictional missing check 0", "failure", now.AddHours(-1), []));
        var returned = await coordinator.CatchUpAsync(default); var fresh = returned.Proposals.Single(item => item.Id == proposal.Id);
        check(fresh is { Status: "proposed", Plan: null } && returned.Inbox.Single(item => item.Event.ItemId == "row000") is { Current: true, UnavailableReason: null },
            "An identical record returning later still needs a fresh plan; history is not duplicated or auto-approved.");
        var freshPlan = coordinator.Plan(fresh.Id, new(fresh.Snapshot, "manual-review", "none"));
        source.Rows.RemoveAt(0); var uncertain = new BacklogCoordinator(store, [new NonAuthoritative(source)]) { Clock = () => now };
        var unverified = await uncertain.CatchUpAsync(default);
        check(unverified.Proposals.Single(item => item.Id == proposal.Id).Status == "planned" && unverified.Inbox.Single(item => item.Event.ItemId == "row000").Current && !unverified.Sources[0].Checkpoint.AuthoritativeSnapshot,
            "An unverified or filtered adapter cannot assert absence without authoritative membership.");
        check(Refused(() => uncertain.Approve(freshPlan.Id, freshPlan.Snapshot, "owner@example.invalid")), "Unverified completion cannot authorize a current plan.");
        source.Rows[^1] = source.Rows[^1] with { Revision = "after-reconciliation-failure" };
        var broken = new BacklogCoordinator(store, [new BrokenMembership(source)]) { Clock = () => now };
        var failed = await broken.CatchUpAsync(default);
        check(failed.Sources[0].Checkpoint is { State: "failed", Cursor: "32", AuthoritativeSnapshot: false } && failed.Inbox.Single(item => item.Event.ItemId == "row049").Event.Revision == "v1" && failed.Inbox.Single(item => item.Event.ItemId == "row000").Current,
            "A failing authoritative membership check rolls back its completing page, disappearance and cursor together.");
        var retried = await coordinator.CatchUpAsync(default);
        check(retried.Sources[0].Checkpoint.AuthoritativeSnapshot && retried.Inbox.Single(item => item.Event.ItemId == "row049" && item.Current).Event.Revision == "after-reconciliation-failure" && retried.Proposals.Single(item => item.ItemId == "row000").Status == "source-unavailable",
            "Retry safely completes the preserved page and reconciles source availability exactly once.");
    }
}
