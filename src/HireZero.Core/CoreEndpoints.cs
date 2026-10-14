using static HireZero.Core.WorkspaceAccess;

namespace HireZero.Core;

// Role-neutral API: session, people, conversations, sharing, the agent queue, shifts and hiring.
public static class CoreEndpoints
{
    static bool IsValid(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= limit;

    // Every new message or request is answered by the host's agent queue, whether or not a browser waits for it.
    static AgentTask? QueueReply(RequestStore store, AgentStore agents, AgentWorker worker, WorkRequest item)
        => RequestAgentWork.Queue(store, agents, worker, item);

    // The agent's reply state for one conversation, including the human-style acknowledgement when it's busy.
    // Collaborators never see another conversation's title.
    static object ReplyStatus(HttpContext context, AgentStore agents, string id)
    {
        var open = agents.OpenTasks(); var mine = open.FirstOrDefault(task => task.RequestId == id && task.Kind == ConversationReplyHandler.ReplyKind);
        var task = mine ?? agents.Latest(id);
        var active = open.FirstOrDefault(item => item.State == "active");
        var ahead = mine is { State: "queued" } ? Array.IndexOf(open, mine) : 0;
        return new { running = mine is not null, executing = mine is { State: "active" } && AgentActivity.IsRunning(mine.Id), state = task?.State ?? "none", ahead, step = mine is { State: "active" } ? AgentActivity.LatestFor(mine.Id)?.Text : null,
            current = mine is { State: "queued" } && active is not null ? (IsOwner(context) ? active.Title : "another request") : null,
            error = task is { State: "failed" } ? task.Detail : null };
    }

    // A steering turn as the UI shows it: state, the latest steps, the cleaned answer and any error.
    static object TurnView(CodingRun run)
    {
        string? summary = null;
        if (run.ResultJson is { } json)
            try { using var document = System.Text.Json.JsonDocument.Parse(json); summary = document.RootElement.TryGetProperty("summary", out var value) ? value.GetString() : null; }
            catch (System.Text.Json.JsonException) { }
        return new { id = run.Id, state = run.State, threadId = run.ThreadId, model = run.ResolvedModel, steps = run.Events.TakeLast(8).Select(item => item.Message),
            summary, error = run.Error, updatedAt = run.UpdatedAt };
    }

    // The owner works on this machine, so the brief follows its clock.
    static TimeZoneInfo BriefZone(AgentStore agents) => agents.Schedule().Enabled ? ShiftClock.Zone(agents.Schedule().TimeZone) : TimeZoneInfo.Local;
    static bool BriefDay(string? day, TimeZoneInfo zone, out string chosen)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        chosen = day ?? today.ToString("yyyy-MM-dd");
        return DateOnly.TryParseExact(chosen, "yyyy-MM-dd", out var parsed) && parsed <= today && parsed > today.AddDays(-14);
    }

    public static void MapCoreEndpoints(this WebApplication app, string ownerLogin, bool localOwnerEnabled)
    {
        app.Services.GetRequiredService<RequestStore>().OwnerLogin = ownerLogin;
        app.MapOwnerTaskEndpoints();
        app.MapGet("/api/runtime", (HttpContext context) =>
        {
            if (!IsOwner(context) || context.Connection.RemoteIpAddress is not { } remote || !System.Net.IPAddress.IsLoopback(remote) ||
                context.Request.Headers.ContainsKey("X-Forwarded-Host") || context.Request.Headers.ContainsKey("X-Forwarded-For"))
                return Results.StatusCode(403);
            return context.RequestServices.GetService<WorkspaceRuntime>() is { } runtime ? Results.Ok(runtime) : Results.StatusCode(503);
        });
        app.MapGet("/api/session", (HttpContext context, IModelGateway model, RoleCatalog roles) => Results.Ok(new
            { login = Login(context), role = Role(context), accessMode = context.RequestServices.GetService<TailnetAccess>()?.Arrived(context) == true ? "tailnet" : context.Connection.RemoteIpAddress is { } remote && System.Net.IPAddress.IsLoopback(remote) && string.IsNullOrEmpty(context.Request.Headers["X-Forwarded-Host"]) ? "local" : "tailscale",
                agentModel = model.DefaultModel, agentReasoning = model.Reasoning, roles = roles.HiredIds(), executionHeld = false }));
        app.MapGet("/api/roles", (HttpContext context, RoleCatalog roles) => !IsOwner(context) ? Results.StatusCode(403) :
            Results.Ok(roles.Available.Select(role => new { role.Info.Id, role.Info.Name, role.Info.Summary, hired = roles.HiredIds().Contains(role.Info.Id) })));
        // Hiring or firing takes effect when the workspace restarts; the role's data is kept either way.
        app.MapPut("/api/roles/{id}", (HttpContext context, RoleCatalog roles, string id, RoleChange input) => !IsOwner(context) ? Results.StatusCode(403) :
            Results.Ok(new { hired = roles.SetHired(id, input.Hired), restartRequired = true }));

        app.MapGet("/api/intake-order", (HttpContext context, RequestStore store) =>
            IsOwner(context) ? Results.Ok(store.IntakeOrder()) : Results.StatusCode(403));
        app.MapPut("/api/intake-order", (HttpContext context, RequestStore store, IntakeOrderChange input) =>
            !IsOwner(context) ? Results.StatusCode(403) :
            input.Ids is null || input.Ids.Length > 1000 ? Results.BadRequest() :
            store.SetIntakeOrder(input.Ids) ? Results.Ok(input.Ids) : Results.Conflict(new { error = "Intake changed. Refresh before reordering." }));
        app.MapGet("/api/people", (HttpContext context, RequestStore store) =>
            IsOwner(context) ? Results.Ok(store.People()) : Results.StatusCode(403));
        app.MapPut("/api/people", (HttpContext context, RequestStore store, IModelGateway model, PersonChange input) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            var login = input.Login?.Trim().ToLowerInvariant();
            if (login is null || login.Length is < 3 or > 320 || !login.Contains('@') || login == ownerLogin ||
                !PersonRoles.Valid(input.Role)) return Results.BadRequest(new { error = "Choose a valid login and a role: boss, trusted or visitor." });
            var person = store.SetPerson(login, PersonRoles.Normalize(input.Role), input.Revoked);
            if (person.Revoked) model.Stop(login);
            else model.Prepare(login);
            return Results.Ok(person);
        });
        // The sidebar: everyone sees only their own conversations; the owner sees all. Optional search.
        app.MapGet("/api/conversations", (HttpContext context, RequestStore store, AgentStore agents, IEnumerable<IOwnerTaskContributor> contributors, WorkspaceOwner owner, string? q) =>
        {
            if (q is { Length: > 200 }) return Results.BadRequest(new { error = "Search for up to 200 characters." });
            var roleTasks = IsOwner(context) ? contributors.SelectMany(source => source.Tasks()).ToArray() : [];
            var shiftOn = agents.Shift().On;
            // Several conversations can cover one PR (a new one per revision); only the newest is current.
            var newest = roleTasks.Where(task => task.Url is not null && task.RequestId is not null).GroupBy(task => task.Url!)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(task => task.UpdatedAt, StringComparer.Ordinal).First().RequestId);
            return Results.Ok(store.Conversations(IsOwner(context) ? null : Login(context), q).Select(item => item with
            {
                WorkState = ConversationWorkStates.For(item, agents.Latest(item.Id), roleTasks.FirstOrDefault(task => task.RequestId == item.Id), shiftOn,
                    needsGoAhead: !item.Home && store.PendingReplyKey(item.Id) is not null && store.FindRequest(item.Id) is { } request && !store.CanUseAgent(request, owner),
                    superseded: roleTasks.FirstOrDefault(task => task.RequestId == item.Id) is { Url: { } url } && newest.TryGetValue(url, out var current) && current != item.Id)
            }));
        });
        app.MapGet("/api/requests", (HttpContext context, RequestStore store) =>
            Results.Ok(store.Requests(IsOwner(context) ? null : Login(context))));
        app.MapPost("/api/requests", (HttpContext context, RequestStore store, AgentStore agents, AgentWorker worker, RequestTriageService triage, NewRequest input) =>
        {
            if (!IsValid(input.Title, 160) || !IsValid(input.Description, 4000) || !IsValid(input.Project, 100))
                return Results.BadRequest(new { error = "A project, title, and description are required." });
            if (!Guid.TryParse(input.ClientRequestId, out _))
                return Results.BadRequest(new { error = "A client request ID is required." });
            var item = store.CreateRequest(Login(context), input.Project.Trim(), input.Title.Trim(), input.Description.Trim(), input.ClientRequestId);
            if (!IsOwner(context)) triage.Open(item, "web");
            QueueReply(store, agents, worker, item);
            return Results.Created($"/api/requests/{item.Id}", item);
        });
        app.MapPost("/api/requests/{id}/archive", (HttpContext context, RequestStore store, AgentStore agents, string id) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (store.FindRequest(id) is null) return Results.NotFound();
            foreach (var task in agents.OpenTasks().Where(task => task.RequestId == id)) agents.Cancel(task.Id);
            return store.ArchiveRequest(id) ? Results.Ok(store.FindRequest(id)) : Results.Conflict(new { error = "Only conversations awaiting review can be archived." });
        });
        // Your Home: the one perpetual conversation with the agent (created on first visit).
        app.MapGet("/api/home", (HttpContext context, RequestStore store) => Results.Ok(store.Home(Login(context))));
        app.MapGet("/api/requests/{id}", (HttpContext context, RequestStore store, string id) =>
        {
            var item = store.FindRequest(id);
            return item is not null && CanRead(context, item)
                ? Results.Ok(new { request = item, home = store.IsHome(id), messages = IsOwner(context) ? store.OwnerMessages(id) : store.Messages(id),
                    requesterRole = IsOwner(context) ? (item.Requester == Login(context) || item.Requester == WorkspaceOwner.LocalIdentity ? "owner" : PersonRoles.Normalize(store.FindPerson(item.Requester)?.Role)) : null,
                    agentAllowed = IsOwner(context) ? store.AgentAllowed(id) : (bool?)null,
                    agentUpgradeReviewRequired = IsOwner(context) ? store.AgentUpgradeReviewRequired(id) : (bool?)null,
                    triage = IsOwner(context) ? context.RequestServices.GetService<TriageStore>()?.Find(id) : null,
                    access = IsOwner(context) ? context.RequestServices.GetService<AccessStore>()?.ForRequest(id) : null }) : Results.NotFound();
        });
        // Triage for a request someone sent: safety read (free), summary of what you would need to do (owner cell), and your decision.
        app.MapPost("/api/requests/{id}/triage", async (HttpContext context, RequestStore store, RequestTriageService triage, string id, CancellationToken token) =>
            !IsOwner(context) ? Results.StatusCode(403) : store.FindRequest(id) is null ? Results.NotFound() : Results.Ok(await triage.SummarizeAsync(id, token))).RequireRateLimiting("agent");
        app.MapPost("/api/requests/{id}/decide", (HttpContext context, RequestStore store, AgentStore agents, AgentWorker worker, RequestTriageService triage, string id, DecisionInput input) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (input.Decision is not ("accept" or "decline")) return Results.BadRequest(new { error = "Accept or decline." });
            return store.Synchronize<IResult>(() =>
            {
            // Promote and accept in one step: set the requester's role first.
            if (triage.Find(id)?.Decision is null && input.Decision == "accept" && input.Role is { Length: > 0 } promote && store.FindRequest(id) is { } item && !item.Requester.Equals(Login(context), StringComparison.OrdinalIgnoreCase))
            {
                if (!PersonRoles.Valid(promote)) return Results.BadRequest(new { error = "Choose boss, trusted or visitor." });
                store.SetPerson(item.Requester, PersonRoles.Normalize(promote), false);
                if (context.RequestServices.GetService<AccessStore>() is { } access && access.Find(item.Requester) is { } knocked)
                    access.Save(knocked with { Status = "approved", Role = PersonRoles.Normalize(promote) });
            }
            return Results.Ok(triage.Decide(id, input.Decision == "accept", input.Note));
            });
        });
        app.MapPost("/api/requests/{id}/allow-agent", (HttpContext context, RequestStore store, AgentStore agents, AgentWorker worker, WorkspaceOwner owner, string id) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            var item = store.FindRequest(id);
            if (item is null) return Results.NotFound();
            RequestAgentWork.Allow(store, agents, worker, item, owner);
            return Results.Ok(new { id, allowed = true });
        });
        app.MapPost("/api/requests/{id}/messages", (HttpContext context, RequestStore store, AgentStore agents, AgentWorker worker, string id, NewMessage input) =>
        {
            var item = store.FindRequest(id);
            if (item is null || !CanRead(context, item)) return Results.NotFound();
            if (!IsValid(input.Body, 4000)) return Results.BadRequest(new { error = "Message must be 1–4000 characters." });
            if (!Guid.TryParse(input.ClientRequestId, out _))
                return Results.BadRequest(new { error = "A client request ID is required." });
            var message = store.AddMessage(id, Login(context), input.Body.Trim(), input.ClientRequestId);
            if (message.RequestId != id) return Results.Conflict();
            QueueReply(store, agents, worker, item);
            return Results.Created($"/api/requests/{id}", message);
        });
        app.MapGet("/api/requests/{id}/draft", (HttpContext context, RequestStore store, string id) =>
        {
            var item = store.FindRequest(id);
            return item is not null && CanRead(context, item) ? Results.Ok(store.Draft(id, Login(context))) : Results.NotFound();
        });
        app.MapPut("/api/requests/{id}/draft", (HttpContext context, RequestStore store, string id, MessageDraft input) =>
        {
            var item = store.FindRequest(id);
            if (item is null || !CanRead(context, item)) return Results.NotFound();
            if (input.Body is null || input.Body.Length > 4000 || !Guid.TryParse(input.ClientRequestId, out _)) return Results.BadRequest();
            store.SaveDraft(id, Login(context), input.Body, input.ClientRequestId);
            return Results.Ok(new { saved = true });
        });
        // What's been happening on one conversation: the agent's recent tasks and any role work (a coding run), steps newest
        // first, plus the durable record of decisions on it. Owner only: the steps can name files and commands.
        app.MapGet("/api/requests/{id}/activity", (HttpContext context, RequestStore store, AgentStore agents, IEnumerable<IConversationActivity> roles, string id) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (store.FindRequest(id) is null) return Results.NotFound();
            var streams = agents.ForRequest(id, 4).Select(task => new ActivityStream(task.Kind switch { "reply" => "Reply", "diagnose-pr" => "Pull request check", _ => task.Title },
                task.State, task.CreatedAt, task.State is "failed" && task.Detail is { } why && AgentActivity.ForTask(task.Id, 80) is { Length: 0 }
                    ? [new(task.UpdatedAt, "Stopped: " + why, task.Id)] : AgentActivity.ForTask(task.Id, 80)));
            var all = streams.Concat(roles.SelectMany(role => role.For(id))).OrderByDescending(stream => stream.State is "active" or "running" or "queued")
                .ThenByDescending(stream => stream.StartedAt, StringComparer.Ordinal).Take(5).ToArray();
            return Results.Ok(new { running = all.Any(stream => stream.State is "active" or "running" or "queued"), streams = all, record = store.Activity.ForRequest(id, 30) });
        });
        // Rename a thread (the owner, or the person who started it). An empty name goes back to the automatic one.
        app.MapPut("/api/requests/{id}/title", (HttpContext context, RequestStore store, string id, TitleInput input) =>
        {
            var item = store.FindRequest(id);
            if (item is null || !CanRead(context, item)) return Results.NotFound();
            if (!IsOwner(context) && item.Requester != Login(context)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(input.Title)) { store.SaveShortTitle(id, ThreadTitles.Readable(item.Title), "rule", replace: true); return Results.Ok(store.FindRequest(id)); }
            if (ThreadTitles.Rename(input.Title) is not { } title) return Results.BadRequest(new { error = "Use a name of up to " + ThreadTitles.Limit + " characters." });
            store.SaveShortTitle(id, title, "owner", replace: true);
            return Results.Ok(store.FindRequest(id));
        });
        app.MapGet("/api/requests/{id}/agent-status", (HttpContext context, RequestStore store, AgentStore agents, string id) =>
        {
            var item = store.FindRequest(id);
            return item is not null && CanRead(context, item) ? Results.Ok(ReplyStatus(context, agents, id)) : Results.NotFound();
        });
        // Ensures a reply is queued (or retries a failed one); the host's agent worker does the answering.
        app.MapPost("/api/requests/{id}/agent-reply", (HttpContext context, RequestStore store, AgentStore agents, AgentWorker worker, string id) =>
        {
            var item = store.FindRequest(id);
            if (item is null || !CanRead(context, item)) return Results.NotFound();
            QueueReply(store, agents, worker, item);
            return Results.Accepted($"/api/requests/{id}/agent-status", ReplyStatus(context, agents, id));
        }).RequireRateLimiting("agent");
        app.MapGet("/api/workload", (HttpContext context, RequestStore store) =>
            Role(context) is PersonRoles.Boss or "owner" ? Results.Ok(store.Workload()) : Results.StatusCode(403));
        app.MapPut("/api/requests/{id}/share", (HttpContext context, RequestStore store, string id, ShareRequest input) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (store.FindRequest(id) is null) return Results.NotFound();
            if (!IsValid(input.Summary, 500) || input.Outcome?.Length > 500 ||
                input.Priority is not ("low" or "normal" or "high") ||
                input.Status is not ("queued" or "active" or "blocked" or "review" or "done"))
                return Results.BadRequest(new { error = "Check summary, priority, status, and outcome." });
            return Results.Ok(store.Publish(id, input.Summary.Trim(), input.Priority, input.Status, input.Outcome?.Trim()));
        });

        app.MapGet("/api/agent", (HttpContext context, AgentStore agents, AgentWorker worker) => !IsOwner(context) ? Results.StatusCode(403) :
            Results.Ok(new { shift = agents.Shift(), schedule = agents.Schedule(), tasks = agents.OpenTasks(), recent = agents.Recent(5), activity = AgentActivity.Recent(6),
                progress = ShiftProgressView.For(agents, worker.Scanning, DateTimeOffset.UtcNow), runningIds = agents.OpenTasks().Where(task => task.State == "active" && AgentActivity.IsRunning(task.Id)).Select(task => task.Id) }));
        app.MapPost("/api/agent/shift", (HttpContext context, AgentStore agents, AgentWorker worker, ShiftChange input) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (input.Action == "stop") { var stopped = worker.Stop(DateTimeOffset.UtcNow, byOwner: true); agents.CancelProactive(); return Results.Ok(stopped); }
            if (input.Action != "start" || input.Hours is not (>= 1 and <= 24)) return Results.BadRequest(new { error = "Start a shift of 1 to 24 hours, or stop the current one." });
            return Results.Ok(worker.Start(TimeSpan.FromHours(input.Hours.Value), "manual", DateTimeOffset.UtcNow));
        });
        app.MapPut("/api/agent/schedule", (HttpContext context, AgentStore agents, AgentWorker worker, ShiftSchedule input) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (input.Days is null || input.Days.Any(day => day is < 0 or > 6) || !ShiftClock.ValidTime(input.Start) || !ShiftClock.ValidTime(input.End) ||
                TimeOnly.ParseExact(input.End, "HH:mm") <= TimeOnly.ParseExact(input.Start, "HH:mm") || input.TimeZone is not { Length: > 0 and <= 64 })
                return Results.BadRequest(new { error = "Choose working days and an end time after the start time." });
            var schedule = input with { Days = [.. input.Days.Distinct().Order()] };
            agents.SaveSchedule(schedule); worker.Wake(); return Results.Ok(schedule);
        });
        app.MapPost("/api/agent/tasks/{id}/cancel", (HttpContext context, AgentStore agents, string id) =>
            !IsOwner(context) ? Results.StatusCode(403) : agents.Cancel(id) ? Results.Ok(agents.Find(id)) :
                Results.Conflict(new { error = "That task already finished." }));
        // Intake: connectors (Outlook, Teams, ...) call the pipeline directly; owners can also hand a message in.
        app.MapGet("/api/intake", (HttpContext context, IntakeStore intake) => !IsOwner(context) ? Results.StatusCode(403) : Results.Ok(intake.Recent(30)));
        app.MapPost("/api/intake", async (HttpContext context, IntakePipeline pipeline, AgentWorker worker, ManualIntake input, CancellationToken token) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (!IsValid(input.From, 320) || input.Subject is not { Length: <= 300 } || !IsValid(input.Body, 8000))
                return Results.BadRequest(new { error = "A sender, a subject of up to 300 characters and a body are required." });
            var record = await pipeline.ProcessAsync(new InboundMessage(input.Source is { Length: > 0 and <= 40 } ? input.Source : "manual", Guid.NewGuid().ToString("N"),
                input.ThreadKey is { Length: > 0 and <= 200 } ? input.ThreadKey : null, input.From.Trim(), input.Subject.Trim(), input.Body.Trim(), DateTimeOffset.UtcNow), token);
            worker.Wake(); return Results.Ok(record);
        }).RequireRateLimiting("agent");
        app.MapPost("/api/intake/{id}/promote", (HttpContext context, IntakePipeline pipeline, AgentWorker worker, string id) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            var record = pipeline.Promote(id); worker.Wake(); return Results.Ok(record);
        });
        app.MapPost("/api/intake/{id}/dismiss", (HttpContext context, IntakePipeline pipeline, string id) =>
            IsOwner(context) ? Results.Ok(pipeline.Dismiss(id)) : Results.StatusCode(403));
        app.MapPut("/api/intake/{id}/priority", (HttpContext context, IntakeStore intake, string id, PriorityChange input) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (input.Priority is not ("low" or "normal" or "high")) return Results.BadRequest(new { error = "Choose low, normal or high." });
            return intake.Find(id) is { } record ? Results.Ok(intake.Save(record with { Priority = input.Priority, PriorityBy = "owner" })) : Results.NotFound();
        });
        // Sessions in other apps (Codex, Claude Code): metadata only.
        app.MapGet("/api/sessions", (HttpContext context, SessionStore sessions, SessionWatcher watcher) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            Dictionary<string, string> health; lock (watcher.Health) health = new(watcher.Health);
            return Results.Ok(new { sessions = sessions.Recent(60), health });
        });
        // Steering: the owner sends into an idle session, or starts one that appears in its app. One driver at a time.
        app.MapPost("/api/sessions/{id}/send", (HttpContext context, SessionSteering steering, string id, SteerInput input) =>
            !IsOwner(context) ? Results.StatusCode(403) : Results.Ok(TurnView(steering.Send(id, new(input.Message ?? "", input.AllowEdits, input.AllowNetwork)))))
            .RequireRateLimiting("agent");
        app.MapGet("/api/sessions/{id}/turn", (HttpContext context, SessionSteering steering, string id) =>
            !IsOwner(context) ? Results.StatusCode(403) : steering.Turn(id) is { } run ? Results.Ok(TurnView(run)) : Results.Ok(new { state = "none" }));
        app.MapPost("/api/sessions/{id}/turn/cancel", (HttpContext context, SessionSteering steering, string id) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            steering.Cancel(id); return Results.Ok(new { stopping = true });
        });
        app.MapPost("/api/sessions/start", (HttpContext context, SessionSteering steering, StartSessionInput input) =>
            !IsOwner(context) ? Results.StatusCode(403) : input.App is not ("codex" or "claude") || input.Folder is not { Length: > 0 and <= 400 }
                ? Results.BadRequest(new { error = "Choose Codex or Claude and a project folder." })
                : Results.Ok(TurnView(steering.Start(input.App, input.Folder, input.Title ?? "", new(input.Message ?? "", input.AllowEdits, input.AllowNetwork)))))
            .RequireRateLimiting("agent");
        app.MapGet("/api/sessions/runs/{app}/{runId}", (HttpContext context, SessionSteering steering, string app, string runId) =>
            !IsOwner(context) ? Results.StatusCode(403) : steering.FindRun(app, runId) is { } run ? Results.Ok(TurnView(run)) : Results.NotFound());
        // Live status from installed hooks. Besides the local-owner checks, the hook proves itself with hook.token from the data root.
        app.MapPost("/api/sessions/events", (HttpContext context, SessionStore sessions, HookToken hook, SessionEvent input) =>
        {
            if (!IsOwner(context) || !hook.Matches(context.Request.Headers["X-HireZero-Hook"].ToString())) return Results.StatusCode(403);
            if (input.App is not ("codex" or "claude") || input.SessionId is not { Length: > 0 and <= 100 } id ||
                !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or ':' or '.') || input.Cwd is { Length: > 400 })
                return Results.BadRequest(new { error = "Unrecognized session event." });
            return Results.Ok(sessions.Apply(input, DateTimeOffset.UtcNow));
        });
        // The daily brief: model-free digest of the day, plus optional same-provider narratives per session.
        app.MapGet("/api/brief", async (HttpContext context, BriefService brief, MemoryService memory, AgentStore agents, string? day, CancellationToken token) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            var zone = BriefZone(agents);
            if (!BriefDay(day, zone, out var chosen)) return Results.BadRequest(new { error = "Choose a day within the last two weeks." });
            var built = await brief.BuildAsync(chosen, zone, token);
            // Once a day, yesterday's brief becomes a memory note so the agent can recall it later.
            var yesterday = DateOnly.ParseExact(chosen, "yyyy-MM-dd").AddDays(-1).ToString("yyyy-MM-dd");
            if (day is null && string.CompareOrdinal(brief.RecordedThrough() ?? "", yesterday) < 0)
            {
                brief.MarkRecorded(yesterday);
                _ = Task.Run(async () =>
                {
                    try { await memory.RecordDayAsync(await brief.BuildAsync(yesterday, zone, CancellationToken.None), CancellationToken.None); AgentActivity.Note("Saved " + yesterday + "'s brief to memory."); }
                    catch (Exception error) when (error is WorkspaceException or ModelUnavailableException or IOException or UnauthorizedAccessException) { AgentActivity.Note("Couldn't save " + yesterday + "'s brief to memory: " + error.Message); }
                });
            }
            return Results.Ok(new { brief = built, writing = brief.Writing });
        });
        app.MapPost("/api/brief/memory", async (HttpContext context, BriefService brief, MemoryService memory, AgentStore agents, BriefDayInput input, CancellationToken token) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            var zone = BriefZone(agents);
            if (!BriefDay(input.Day, zone, out var chosen)) return Results.BadRequest(new { error = "Choose a day within the last two weeks." });
            await memory.RecordDayAsync(await brief.BuildAsync(chosen, zone, token), token);
            return Results.Ok(new { saved = chosen });
        });
        // Memory: OpenClaw's notes and long-term memory, searched and added to by the owner.
        app.MapGet("/api/memory", async (HttpContext context, MemoryService memory, CancellationToken token) =>
            IsOwner(context) ? Results.Ok(await memory.OverviewAsync(token)) : Results.StatusCode(403));
        app.MapGet("/api/memory/search", async (HttpContext context, MemoryService memory, string? q, CancellationToken token) =>
            IsOwner(context) ? Results.Ok(new { results = await memory.SearchAsync(q ?? "", 12, token) }) : Results.StatusCode(403));
        // Suggestions the agent made from intake and finished requests; only accepted ones reach memory.
        app.MapGet("/api/memory/suggestions", (HttpContext context, MemorySuggestionStore suggestions) =>
            IsOwner(context) ? Results.Ok(new { pending = suggestions.Pending(), recent = suggestions.Recent(30).Where(item => item.State != "pending").Take(10) }) : Results.StatusCode(403));
        app.MapPost("/api/memory/suggestions/{id}/accept", async (HttpContext context, MemorySuggestionService suggestions, string id, MemoryInput input, CancellationToken token) =>
            IsOwner(context) ? Results.Ok(await suggestions.AcceptAsync(id, input.Text, token)) : Results.StatusCode(403));
        app.MapPost("/api/memory/suggestions/{id}/dismiss", (HttpContext context, MemorySuggestionService suggestions, string id) =>
            IsOwner(context) ? Results.Ok(suggestions.Dismiss(id)) : Results.StatusCode(403));
        app.MapPost("/api/memory/notes", async (HttpContext context, MemoryService memory, MemoryInput input, CancellationToken token) =>
            IsOwner(context) ? Results.Ok(new { saved = await memory.RememberAsync(input.Text ?? "", "owner", token) }) : Results.StatusCode(403))
            .RequireRateLimiting("agent");
        app.MapPost("/api/brief/narratives", (HttpContext context, BriefService brief, AgentStore agents, BriefDayInput input) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            var zone = BriefZone(agents);
            if (!BriefDay(input.Day, zone, out var chosen)) return Results.BadRequest(new { error = "Choose a day within the last two weeks." });
            var settings = brief.Settings();
            if (!settings.CodexNarratives && !settings.ClaudeNarratives) return Results.BadRequest(new { error = "Turn on summaries for Codex or Claude first." });
            return brief.StartNarratives(chosen, zone) ? Results.Ok(new { writing = true }) : Results.Conflict(new { error = "Summaries are already being written." });
        }).RequireRateLimiting("agent");
        app.MapPut("/api/brief/settings", (HttpContext context, BriefService brief, BriefSettings input) =>
            IsOwner(context) ? Results.Ok(brief.SaveSettings(input)) : Results.StatusCode(403));
        // What HireZero knows about the projects a question mentions, as a reply would receive it (owner only; preview either audience).
        app.MapGet("/api/context", async (HttpContext context, WorkingContext working, string? q, string? audience, CancellationToken token) =>
        {
            if (!IsOwner(context)) return Results.StatusCode(403);
            if (q is not { Length: > 1 and <= 500 }) return Results.BadRequest(new { error = "Ask about a project in 2 to 500 characters." });
            var who = audience switch { "visitor" => Audience.Visitor, "boss" => Audience.Boss, _ => Audience.Owner };
            return Results.Ok(new { projects = working.Match(q).Select(project => new { project.Name, project.Repository, project.LastActive, project.Sessions }),
                audience = who.ToString().ToLowerInvariant(), context = await working.ForAsync(q, who, token) });
        });
        // What the owner did on a day, as a boss sees it (the owner gets the same view as a preview).
        app.MapGet("/api/worklog", async (HttpContext context, WorkLogService worklog, AgentStore agents, string? day, CancellationToken token) =>
        {
            if (!IsOwner(context) && Role(context) != PersonRoles.Boss) return Results.StatusCode(403);
            var zone = BriefZone(agents);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
            var chosen = day ?? today.ToString("yyyy-MM-dd");
            if (!DateOnly.TryParseExact(chosen, "yyyy-MM-dd", out var parsed) || parsed > today || parsed < today.AddDays(-60))
                return Results.BadRequest(new { error = "Choose a day in the last 60 days." });
            return Results.Ok(await worklog.DayAsync(chosen, zone, token));
        });
        app.MapGet("/api/worklog/detail", async (HttpContext context, WorkLogService worklog, AgentStore agents, string? day, string? id, CancellationToken token) =>
        {
            if (!IsOwner(context) && Role(context) != PersonRoles.Boss) return Results.StatusCode(403);
            if (day is null || id is not { Length: > 0 and <= 600 } || !DateOnly.TryParseExact(day, "yyyy-MM-dd", out _)) return Results.BadRequest(new { error = "Choose a day and an entry." });
            return await worklog.DetailAsync(day, BriefZone(agents), id, token) is { } detail ? Results.Ok(detail) : Results.NotFound();
        });
        // Who opened HireZero without an invitation, and the owner's decisions.
        app.MapGet("/api/access", (HttpContext context, AccessDesk desk, AccessStore access) =>
            IsOwner(context) ? Results.Ok(new { settings = desk.Settings(), people = access.All() }) : Results.StatusCode(403));
        app.MapPut("/api/access/settings", (HttpContext context, AccessDesk desk, AccessSettings input) =>
            IsOwner(context) ? Results.Ok(desk.Save(input)) : Results.StatusCode(403));
        app.MapPost("/api/access/approve", (HttpContext context, AccessDesk desk, AccessDecision input) =>
            IsOwner(context) ? Results.Ok(desk.Approve(input.Login ?? "", input.Role ?? "")) : Results.StatusCode(403));
        app.MapPost("/api/access/deny", (HttpContext context, AccessDesk desk, AccessDecision input) =>
            IsOwner(context) ? Results.Ok(desk.Deny(input.Login ?? "")) : Results.StatusCode(403));
        // What may reach a boss: shared organizations and hidden projects (owner only), with the projects that currently qualify.
        app.MapGet("/api/sharing", (HttpContext context, SharingPolicy sharing, WorkingContext working) =>
            IsOwner(context) ? Results.Ok(new { settings = sharing.Settings(), sharedWithBoss = working.SharedWithBoss().Select(project => new { project.Name, project.Repository }) }) : Results.StatusCode(403));
        app.MapPut("/api/sharing", (HttpContext context, SharingPolicy sharing, SharingSettings input) =>
            IsOwner(context) ? Results.Ok(sharing.Save(input)) : Results.StatusCode(403));
        app.MapGet("/api/projects", (HttpContext context, WorkingContext working) =>
            IsOwner(context) ? Results.Ok(working.Projects().Select(project => new { project.Name, project.Repository, project.LastActive, project.Sessions, project.Aliases })) : Results.StatusCode(403));
        app.MapGet("/health", () => Results.Ok(new { status = "online" }));
    }
}

public sealed record BriefDayInput(string? Day);
public sealed record MemoryInput(string? Text);
public sealed record TitleInput(string? Title);
public sealed record DecisionInput(string? Decision, string? Note, string? Role = null);
public sealed record AccessDecision(string? Login, string? Role);
public sealed record PersonChange(string? Login, string? Role, bool Revoked);
public sealed record NewRequest(string Project, string Title, string Description, string ClientRequestId);
public sealed record NewMessage(string Body, string ClientRequestId);
public sealed record ShareRequest(string Summary, string Priority, string Status, string? Outcome);
public sealed record IntakeOrderChange(string[]? Ids);
public sealed record ShiftChange(string? Action, int? Hours);
public sealed record RoleChange(bool Hired);
public sealed record ManualIntake(string? Source, string? ThreadKey, string From, string Subject, string Body);
public sealed record PriorityChange(string? Priority);
public sealed record SteerInput(string? Message, bool AllowEdits, bool AllowNetwork);
public sealed record StartSessionInput(string? App, string? Folder, string? Title, string? Message, bool AllowEdits, bool AllowNetwork);
