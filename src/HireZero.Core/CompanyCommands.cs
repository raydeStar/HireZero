namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private object Apply(StoreConnection db, CompanyCommand c)
    {
        switch (c.Action)
        {
            case "conversation.prepare":
                return PrepareConversation(db, c);
            case "context.draft":
                return PrepareContextDraft(db, c);
            case "measured.prepare":
                return PrepareMeasured(db, c);
            case "measured.run":
                return StartMeasured(db, c);
            case "department.create":
            {
                var d = new CompanyDepartment(Id(), Text(c.Name, 100), null, null);
                Save(db, d, d.Id);
                Event(db, "department.created", d.Id, null, "Department created: " + d.Name);
                return d;
            }
            case "employee.hire":
            {
                var d = Need<CompanyDepartment>(db, c.DepartmentId);
                var role = Role(c.RoleId);
                if (All<CompanyEmployee>(db).Count(e => e.State != "retired") >= 20)
                    throw new WorkspaceException("The local company supports at most 20 active employees.");
                var e = new CompanyEmployee(
                    Id(),
                    d.Id,
                    role.Id,
                    Text(c.Name, 100),
                    Text(c.Responsibility, 1000),
                    d.Knowledge is null ? "onboarding" : "active"
                );
                Put(db, e);
                var initial = new CompanyThread(Id(), e.Id, "Private conversation");
                Save(db, initial, initial.Id, employee: e.Id);
                if (d.Guide is null)
                {
                    d = d with { Guide = e.Id, Version = d.Version + 1 };
                    Save(db, d, d.Id, version: d.Version);
                }
                Event(db, "employee.hired", e.Id, e.Id, "Hired " + e.Name + " in " + d.Name);
                return e;
            }
            case "employee.update":
            case "employee.lifecycle":
            {
                var e = Need<CompanyEmployee>(db, c.Id);
                Version(e.Version, c.ExpectedVersion);
                if (e.State == "retired")
                    throw new WorkspaceException("Retired employees remain read-only.");
                var state =
                    c.Action == "employee.update"
                        ? e.State
                        : c.Decision switch
                        {
                            "pause" => "paused",
                            "retire" => "retired",
                            "resume" => Need<CompanyDepartment>(db, e.DepartmentId).Knowledge is null
                                ? "onboarding"
                                : "active",
                            _ => throw new WorkspaceException("Choose pause, resume or retire.", 422),
                        };
                e = e with
                {
                    Name = c.Name is null ? e.Name : Text(c.Name, 100),
                    Responsibility = c.Responsibility is null ? e.Responsibility : Text(c.Responsibility, 1000),
                    State = state,
                    Version = e.Version + 1,
                    Generation = e.Generation + 1,
                };
                Put(db, e);
                foreach (
                    var t in All<CompanyTask>(db)
                        .Where(t => t.EmployeeId == e.Id && t.State is "queued" or "blocked" or "running")
                )
                    Stop(db, t, "canceled", "Employee authority changed.");
                Event(db, "employee.changed", e.Id, e.Id, "Employee updated: " + e.Name + " (" + state + ")");
                return e;
            }
            case "knowledge.save":
            {
                CompanyDepartment? d = c.DepartmentId is null ? null : Need<CompanyDepartment>(db, c.DepartmentId);
                var prior = c.Id is null ? null : Need<CompanyKnowledge>(db, c.Id);
                if (prior is not null)
                {
                    Version(prior.Version, c.ExpectedVersion);
                    if (prior.State != "draft" || prior.DepartmentId != d?.Id)
                        throw new WorkspaceException("Create a new knowledge draft.");
                }
                var body = Text(c.Body, 8000);
                if (d?.Guide is { } guide)
                {
                    using var json = System.Text.Json.JsonDocument.Parse(body);
                    var role = Role(Need<CompanyEmployee>(db, guide).RoleId);
                    foreach (var field in role.OnboardingFields)
                        if (
                            json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                            || !json.RootElement.TryGetProperty(field, out var v)
                            || v.ValueKind != System.Text.Json.JsonValueKind.String
                            || string.IsNullOrWhiteSpace(v.GetString())
                        )
                            throw new WorkspaceException("Complete onboarding field: " + field, 422);
                }
                var k = new CompanyKnowledge(
                    prior?.Id ?? Id(),
                    d?.Id,
                    body,
                    ContentGuard.Digest(body),
                    "draft",
                    (prior?.Version ?? 0) + 1
                );
                Save(db, k, k.Id, d?.Id, state: k.State, version: k.Version);
                return k;
            }
            case "knowledge.review":
            {
                var k = Need<CompanyKnowledge>(db, c.Id);
                Version(k.Version, c.ExpectedVersion);
                if (k.Digest != c.Digest)
                    throw new WorkspaceException("Review the exact knowledge digest.");
                if (c.Decision is not ("approve" or "revoke"))
                    throw new WorkspaceException("Choose approve or revoke.", 422);
                if (c.Decision == "approve" && k.State != "draft")
                    throw new WorkspaceException("Only a draft can be approved.");
                k = k with { State = c.Decision == "approve" ? "approved" : "revoked", Version = k.Version + 1 };
                Save(db, k, k.Id, k.DepartmentId, state: k.State, version: k.Version);
                if (k.DepartmentId is { } department)
                {
                    var d = Need<CompanyDepartment>(db, department);
                    d = d with
                    {
                        Knowledge =
                            k.State == "approved" ? k.Id
                            : d.Knowledge == k.Id ? null
                            : d.Knowledge,
                        Version = d.Version + 1,
                    };
                    Save(db, d, d.Id, version: d.Version);
                    foreach (
                        var e in All<CompanyEmployee>(db)
                            .Where(e => e.DepartmentId == d.Id && e.State == "onboarding" && k.State == "approved")
                    )
                        Put(db, e with { State = "active", Version = e.Version + 1 });
                }
                Event(db, "knowledge." + k.State, k.Id, null, "Knowledge " + k.State + " (version " + k.Version + ")");
                return k;
            }
            case "thread.create":
            {
                var e = Need<CompanyEmployee>(db, c.EmployeeId);
                if (e.State == "retired")
                    throw new WorkspaceException("Employee is retired.");
                var t = new CompanyThread(Id(), e.Id, Text(c.Name, 160));
                Save(db, t, t.Id, employee: e.Id);
                return t;
            }
            case "notes.save":
            {
                var e = Need<CompanyEmployee>(db, c.EmployeeId);
                var old = Find<CompanyNotes>(db, e.Id);
                Version(old?.Version ?? 0, c.ExpectedVersion);
                if (e.State == "retired")
                    throw new WorkspaceException("Retired employee notes are read-only.");
                var n = new CompanyNotes(e.Id, c.Body == "" ? "" : Text(c.Body, 8000), (old?.Version ?? 0) + 1);
                Save(db, n, n.Id, employee: e.Id, version: n.Version);
                return n;
            }
            case "message.send":
            {
                var t = Need<CompanyThread>(db, c.ThreadId);
                if (t.EmployeeId != c.EmployeeId)
                    throw new WorkspaceException("Thread not found.", 404);
                if (Need<CompanyEmployee>(db, t.EmployeeId).State == "retired")
                    throw new WorkspaceException("Retired employee history is read-only.");
                var body = Text(c.Body);
                var message = Message(db, t, "owner", body);
                return new
                {
                    message,
                    task = c.Skill is null
                        ? null
                        : NewTask(db, t.EmployeeId, t.Id, c.Skill, body, c.Evidence ?? [], [], null, null, null),
                };
            }
            case "company.message":
            {
                var body = Text(c.Body);
                if (c.EmployeeId is null)
                {
                    Event(db, "owner.note", Id(), null, body);
                    return new { saved = true };
                }
                var target = Need<CompanyEmployee>(db, c.EmployeeId);
                var t = c.ThreadId is null
                    ? All<CompanyThread>(db).FirstOrDefault(t => t.EmployeeId == target.Id && t.Scope == "employee")
                    : Need<CompanyThread>(db, c.ThreadId);
                if (t is null)
                {
                    t = new CompanyThread(Id(), target.Id, "Company conversation");
                    Save(db, t, t.Id, employee: target.Id);
                }
                if (t.EmployeeId != c.EmployeeId || t.Scope != "employee")
                    throw new WorkspaceException("Choose the target employee's private thread.", 404);
                Message(db, t, "owner", body);
                var task = NewTask(
                    db,
                    t.EmployeeId,
                    t.Id,
                    PlatformSkills.Chat,
                    body,
                    c.Evidence ?? [],
                    [],
                    null,
                    null,
                    null
                );
                Event(
                    db,
                    "owner.message",
                    task.Id,
                    t.EmployeeId,
                    "Message assigned to " + Need<CompanyEmployee>(db, t.EmployeeId).Name
                );
                return task;
            }
            case "action.start":
                return StartAction(db, c);
            case "task.create":
                return NewTask(
                    db,
                    Text(c.EmployeeId, 64),
                    Text(c.ThreadId, 64),
                    Text(c.Skill, 80),
                    Text(c.Body),
                    c.Evidence ?? [],
                    [],
                    null,
                    null,
                    null
                );
            case "execution.review":
                return ReviewExecution(db, c);
            case "owner.mark":
                return MarkNeed(db, c);
            case "member.invite":
                return InviteMember(db, c);
            case "member.role":
                return ChangeMember(db, c, revoke: false);
            case "member.revoke":
                return ChangeMember(db, c, revoke: true);
            case "publication.review":
                return ReviewPublication(db, c);
            case "budget.configure":
            {
                var id = Text(c.Id, 100);
                long ceiling;
                if (id == "company-total")
                    ceiling = CompanyBudgetCeilings.Company;
                else if (id.StartsWith("department:", StringComparison.Ordinal))
                {
                    Need<CompanyDepartment>(db, id[11..]);
                    ceiling = CompanyBudgetCeilings.Department;
                }
                else if (id.StartsWith("employee:", StringComparison.Ordinal))
                {
                    Need<CompanyEmployee>(db, id[9..]);
                    ceiling = CompanyBudgetCeilings.Employee;
                }
                else
                    throw new WorkspaceException("Root plan allowances require a new exact plan approval.", 403);
                var old = Find<CompanyBudget>(db, id);
                Version(old?.Version ?? 0, c.ExpectedVersion);
                if (c.Budget < 0 || c.Budget > ceiling || c.Budget < (old?.Used ?? 0) + (old?.Reserved ?? 0))
                    throw new WorkspaceException(
                        "Choose a limit within the server ceiling and above committed/uncertain usage.",
                        422
                    );
                var b = new CompanyBudget(id, c.Budget, old?.Used ?? 0, old?.Reserved ?? 0, (old?.Version ?? 0) + 1);
                Save(db, b, b.Id, version: b.Version);
                Event(db, "budget.changed", id, null, "Owner updated a company-module budget limit.");
                return b;
            }
            case "workspace.grant":
            {
                var t = Need<CompanyTask>(db, c.Id);
                Version(t.Version, c.ExpectedVersion);
                var e = Need<CompanyEmployee>(db, t.EmployeeId);
                if (!Role(e.RoleId).WorkspaceClaims || t.State is not ("queued" or "blocked"))
                    throw new WorkspaceException(
                        "Only an eligible, undispatched task can receive a workspace claim scope.",
                        403
                    );
                var workspace = CanonicalWorkspace(Text(c.Body, 1000));
                t = t with
                {
                    Resource = workspace,
                    AllowNetwork = c.AllowNetwork,
                    Version = t.Version + 1,
                    Generation = t.Generation + 1,
                };
                Put(db, t);
                Event(
                    db,
                    "workspace.scoped",
                    t.Id,
                    t.EmployeeId,
                    "Owner scoped a checkout for this work"
                        + (c.AllowNetwork ? ", with network access for its run." : ", without network access.")
                );
                return t;
            }
            case "task.cancel":
            case "task.retry":
            case "task.reassign":
            case "task.complete":
            {
                var task = Need<CompanyTask>(db, c.Id);
                Version(task.Version, c.ExpectedVersion);
                if (task.Kind == "conversation" && c.Action != "task.cancel")
                    throw new WorkspaceException(
                        "A conversational turn cannot become autonomous work or be resumed. Prepare a fresh separately authorized reply."
                    );
                // An action is never model work: rerun it from the employee's actions instead of requeuing it.
                if (task.Kind == ActionKind && c.Action is "task.retry" or "task.reassign")
                    throw new WorkspaceException("Run the action again instead of retrying or reassigning it.");
                if (c.Action == "task.complete")
                {
                    if (task.State is not ("awaiting_review" or "blocked"))
                        throw new WorkspaceException("Review work before completion.");
                    var artifact = All<CompanyArtifact>(db).Any(a => a.TaskId == task.Id && a.State == "approved");
                    if (!artifact)
                        throw new WorkspaceException("Approve an artifact before completing this task.");
                    task = task with { State = "completed", Reason = null, Version = task.Version + 1 };
                    Put(db, task);
                    Event(db, "task.completed", task.Id, task.EmployeeId, "Task completed after owner review.");
                    if (task.PlanId is { } planId)
                    {
                        var p = Need<CompanyPlan>(db, planId);
                        if (
                            p.State == "executing"
                            && p.TaskIds.All(id => Need<CompanyTask>(db, id).State == "completed")
                        )
                        {
                            Put(db, p with { State = "completed", Version = p.Version + 1 });
                            Event(
                                db,
                                "plan.completed",
                                p.Id,
                                p.CeoId,
                                "Delegation plan completed after owner reviews."
                            );
                        }
                    }
                    return task;
                }
                if (c.Action == "task.cancel")
                {
                    Stop(db, task, "canceled", "Owner canceled; any late output is withheld.");
                    return Need<CompanyTask>(db, task.Id);
                }
                if (All<CompanyHandoff>(db).Any(h => h.TargetTaskId == task.Id))
                    throw new WorkspaceException(
                        "Create a fresh reviewed handoff instead of retrying or reassigning shared work."
                    );
                if (task.PlanId is not null || task.MeetingId is not null)
                    throw new WorkspaceException(
                        "Revise the parent plan or meeting instead of changing its assigned task."
                    );
                if (c.Action == "task.retry" && task.State is not ("failed" or "blocked" or "unknown"))
                    throw new WorkspaceException("Only blocked, failed or unknown work can be retried.");
                if (task.State == "unknown" && c.Decision != "acknowledge-uncertain")
                    throw new WorkspaceException(
                        "Reconcile the uncertain attempt first, or explicitly acknowledge possible duplicate usage. Its reservation remains held."
                    );
                Stop(db, task, "superseded", "Owner requested fresh admission.");
                var e = Need<CompanyEmployee>(db, c.EmployeeId ?? task.EmployeeId);
                var thread = Need<CompanyThread>(db, c.ThreadId ?? task.ThreadId);
                if (thread.EmployeeId != e.Id)
                    throw new WorkspaceException("Reassignment needs a thread owned by the target employee.");
                return NewTask(
                    db,
                    e.Id,
                    thread.Id,
                    task.Skill,
                    task.Input,
                    task.Evidence,
                    task.Dependencies,
                    task.RootId,
                    null,
                    null
                );
            }
            case "artifact.create":
            {
                var task = Need<CompanyTask>(db, c.Id);
                Version(task.Version, c.ExpectedVersion);
                if (
                    task.Kind is "conversation" or ActionKind
                    || task.State is "running" or "canceled" or "superseded" or "completed"
                )
                    throw new WorkspaceException("This task cannot accept a manual artifact.");
                var body = Text(c.Body, 12000);
                var a = new CompanyArtifact(
                    Id(),
                    task.Id,
                    task.EmployeeId,
                    body,
                    ContentGuard.Digest(body),
                    "needs_review",
                    "owner-authored",
                    null
                );
                Save(db, a, a.Id, a.TaskId, a.EmployeeId, a.State, a.Version);
                Put(db, task with { State = "awaiting_review", Version = task.Version + 1, Reason = null });
                Event(db, "artifact.ready", a.Id, a.EmployeeId, "An owner-authored artifact is ready for review.");
                return a;
            }
            case "artifact.review":
            {
                var a = Need<CompanyArtifact>(db, c.Id);
                Version(a.Version, c.ExpectedVersion);
                if (a.Digest != c.Digest)
                    throw new WorkspaceException("Review the exact artifact digest.");
                if (c.Decision is not ("approve" or "changes" or "revoke"))
                    throw new WorkspaceException("Choose approve, changes or revoke.", 422);
                var task = Need<CompanyTask>(db, a.TaskId);
                if (task.State is "canceled" or "superseded")
                    throw new WorkspaceException("This work was canceled or superseded.");
                if (a.ContextId is not null && !KnowledgeCurrent(db, Need<CompanyContext>(db, a.ContextId), task))
                    throw new WorkspaceException("Knowledge used by this artifact was revoked.");
                a = a with
                {
                    State =
                        c.Decision == "approve" ? "approved"
                        : c.Decision == "revoke" ? "revoked"
                        : "changes_requested",
                    Version = a.Version + 1,
                };
                Save(db, a, a.Id, a.TaskId, a.EmployeeId, a.State, a.Version);
                Event(db, "artifact.reviewed", a.Id, a.EmployeeId, "Artifact review: " + a.State);
                return a;
            }
            case "handoff.assign":
                return AssignHandoff(db, c);
            case "handoff.revoke":
                return RevokeHandoff(db, c);
            case "handoff.create":
            {
                var a = Need<CompanyArtifact>(db, c.Id);
                Version(a.Version, c.ExpectedVersion);
                if (a.State != "approved" || a.Digest != c.Digest)
                    throw new WorkspaceException("Share the exact approved artifact.");
                var t = Need<CompanyThread>(db, c.ThreadId);
                if (t.EmployeeId != c.EmployeeId || t.Scope != "employee")
                    throw new WorkspaceException("Target private thread not found.", 404);
                if (Need<CompanyEmployee>(db, t.EmployeeId).State != "active")
                    throw new WorkspaceException("Choose an active recipient.");
                var h = new CompanyHandoff(Id(), a.Id, t.EmployeeId, t.Id, a.Body, a.Digest);
                Save(db, h, h.Id, a.Id, t.EmployeeId);
                Event(
                    db,
                    "handoff.shared",
                    h.Id,
                    t.EmployeeId,
                    "An approved artifact was shared to an employee thread."
                );
                return h;
            }
            case "plan.create":
                return CreatePlan(db, c);
            case "plan.from-artifact":
            {
                var a = Need<CompanyArtifact>(db, c.Id);
                Version(a.Version, c.ExpectedVersion);
                if (
                    a.State != "approved"
                    || a.Digest != c.Digest
                    || !Role(Need<CompanyEmployee>(db, a.EmployeeId).RoleId).CompanyOverview
                )
                    throw new WorkspaceException("Select an exact approved facilitator proposal.");
                var proposal = CompanyJson.Decode<CompanyPlanProposal>(a.Body);
                return CreatePlan(
                    db,
                    c with
                    {
                        EmployeeId = a.EmployeeId,
                        Body = proposal.Objective,
                        Tasks = proposal.Tasks,
                        Budget = proposal.Budget,
                    }
                );
            }
            case "plan.approve":
            case "plan.cancel":
                return ChangePlan(db, c);
            case "meeting.create":
                return CreateMeeting(db, c);
            case "meeting.start":
            case "meeting.next":
            case "meeting.pause":
            case "meeting.cancel":
            case "meeting.outcome":
            case "meeting.close":
                return ChangeMeeting(db, c);
            default:
                throw new WorkspaceException("Unknown company command.", 422);
        }
    }

    private CompanyMessage Message(StoreConnection db, CompanyThread t, string author, string body)
    {
        var seq =
            All<CompanyMessage>(db).Where(m => m.ThreadId == t.Id).Select(m => m.Sequence).DefaultIfEmpty(0).Max() + 1;
        var m = new CompanyMessage(Id(), t.Id, t.EmployeeId, author, body, seq);
        Save(db, m, m.Id, t.Id, t.EmployeeId);
        return m;
    }

    private void Evidence(StoreConnection db, string[] ids)
    {
        if (ids.Length > 12 || ids.Distinct().Count() != ids.Length)
            throw new WorkspaceException("Select at most 12 distinct artifacts.", 422);
        foreach (var id in ids)
            if (Need<CompanyArtifact>(db, id).State != "approved")
                throw new WorkspaceException("Selected evidence must be approved.");
    }

    private CompanyTask NewTask(
        StoreConnection db,
        string employee,
        string thread,
        string skill,
        string input,
        string[] evidence,
        string[] dependencies,
        string? root,
        string? plan,
        string? meeting,
        string kind = "work"
    )
    {
        var e = Need<CompanyEmployee>(db, employee);
        if (e.State != "active" && !(e.State == "onboarding" && kind == ContextDraftKind))
            throw new WorkspaceException("This employee must be active with approved department onboarding.");
        var t = Need<CompanyThread>(db, thread);
        if (t.EmployeeId != employee)
            throw new WorkspaceException("Thread not found.", 404);
        if (t.Scope == "company" && (kind != "conversation" || skill != PlatformSkills.Chat))
            throw new WorkspaceException(
                "Company conversations admit one explicitly approved conversational reply only."
            );
        if (kind == ActionKind)
        {
            if (Role(e.RoleId).Manifest?.Actions.Any(a => a.Id == skill) != true)
                throw new WorkspaceException("The selected role does not have this action.", 403);
        }
        else if (!Role(e.RoleId).Skills.Contains(skill))
            throw new WorkspaceException("The selected role does not have this skill.", 403);
        else
            input = Role(e.RoleId).ValidateInput?.Invoke(skill, input) ?? input;
        if (All<CompanyTask>(db).Count(j => j.EmployeeId == employee && j.State is "queued" or "blocked") >= 10)
            throw new WorkspaceException("Employee queue is full.");
        Evidence(db, evidence);
        var id = Id();
        root ??= id;
        if (Find<CompanyBudget>(db, root) is null)
            Save(db, new CompanyBudget(root, 5000), root);
        var d = Need<CompanyDepartment>(db, e.DepartmentId);
        var task = new CompanyTask(
            id,
            employee,
            thread,
            skill,
            kind == ContextDraftKind ? ContextDraftInput(input) : Text(input),
            "queued",
            null,
            root,
            evidence,
            dependencies,
            plan,
            meeting,
            Watermark: All<CompanyMessage>(db)
                .Where(m => m.ThreadId == thread)
                .Select(m => m.Sequence)
                .DefaultIfEmpty(0)
                .Max(),
            KnowledgeId: d.Knowledge,
            CompanyKnowledgeId: All<CompanyKnowledge>(db)
                .LastOrDefault(k => k.DepartmentId is null && k.State == "approved")
                ?.Id
        );
        var execution = Role(e.RoleId).ExecutionSkills.Contains(skill);
        // Actions never call a model, so they carry no route.
        task =
            kind == ActionKind
                ? task with
                {
                    Kind = kind,
                }
                : task with
                {
                    ModelRoute = ModelRouting.For(execution),
                    Runtime = ModelRouting.Runtime,
                    Kind = kind,
                };
        if (execution)
            task = task with { State = "blocked", Reason = CompanyReasons.ExecutionUnavailable };
        Put(db, task);
        Event(
            db,
            kind == ContextDraftKind ? "context.requested"
                : kind == "conversation" ? "conversation.requested"
                : "task.assigned",
            task.Id,
            e.Id,
            kind == ContextDraftKind ? "Sources read for " + e.Name + "'s context draft; nothing is authorized yet."
                : kind == "conversation"
                    ? "Conversation message prepared for " + e.Name + "; no reply is authorized yet."
                : "Task assigned to " + e.Name + ": " + Role(e.RoleId).WorkName(skill)
        );
        return task;
    }

    private void Stop(StoreConnection db, CompanyTask task, string state, string reason)
    {
        if (task.State is "completed" or "canceled" or "superseded")
            throw new WorkspaceException("The task is already terminal.");
        Put(
            db,
            task with
            {
                State = state,
                Reason = reason,
                Version = task.Version + 1,
                Generation = task.Generation + 1,
            }
        );
        foreach (
            var a in All<CompanyAttempt>(db)
                .Where(a => a.TaskId == task.Id && a.Phase is "claimed" or "reserved" or "dispatching")
        )
        {
            var phase = a.Phase == "dispatching" ? "unknown" : "not_sent";
            Put(
                db,
                a with
                {
                    Phase = phase,
                    Error = reason,
                    Measured = a.Measured is { } job
                        ? job with
                        {
                            State = phase == "unknown" ? "unknown" : "canceled",
                        }
                        : null,
                    Version = a.Version + 1,
                }
            );
            if (phase == "not_sent")
                Release(db, a.Id);
        }
        Event(db, "task." + state, task.Id, task.EmployeeId, reason);
    }
}
