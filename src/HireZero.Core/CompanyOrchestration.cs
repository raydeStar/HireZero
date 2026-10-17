using Microsoft.Data.Sqlite;

namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private CompanyPlan CreatePlan(SqliteConnection db, CompanyCommand c)
    {
        var ceo = Need<CompanyEmployee>(db, c.EmployeeId);
        if (!Role(ceo.RoleId).Skills.Contains("company-plan") || ceo.State != "active")
            throw new WorkspaceException("Select an active employee qualified to propose company plans.");
        var specs = c.Tasks ?? [];
        if (specs.Length is < 1 or > 4 || c.Budget is < 5000 or > 40000)
            throw new WorkspaceException("A plan needs 1–4 tasks and a bounded allowance of 5,000–40,000 tokens.", 422);
        if (c.Budget < specs.Length * 5000)
            throw new WorkspaceException("The plan allowance must cover every initial dispatch reservation.");
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            var e = Need<CompanyEmployee>(db, spec.EmployeeId);
            var thread = Need<CompanyThread>(db, spec.ThreadId);
            if (spec.Evidence is null || spec.DependsOn is null)
                throw new WorkspaceException("Every proposed task requires evidence and dependency lists.", 422);
            if (
                e.State != "active"
                || thread.EmployeeId != e.Id
                || thread.Scope != "employee"
                || !Role(e.RoleId).Skills.Contains(spec.Skill)
                || spec.Skill is "company-plan" or "meeting-chair" or "meeting-turn"
            )
                throw new WorkspaceException("Delegation must target active employees and ordinary internal skills.");
            if (
                e.Id == ceo.Id
                || spec.DependsOn.Any(d => d < 0 || d >= i)
                || spec.DependsOn.Distinct().Count() != spec.DependsOn.Length
            )
                throw new WorkspaceException("Delegation cannot target itself or create cyclic/forward dependencies.");
            Evidence(db, spec.Evidence);
            Text(spec.Input);
            Role(e.RoleId).ValidateInput?.Invoke(spec.Skill, spec.Input);
            if (
                !fingerprints.Add(
                    e.Id + ":" + spec.Skill + ":" + EmployeeStore.Hash(spec.Input.Trim().ToLowerInvariant())
                )
            )
                throw new WorkspaceException("Duplicate delegated tasks are not allowed.");
        }
        var id = Id();
        var objective = Text(c.Body, 2000);
        var digest = CompanyJson.Digest(
            new
            {
                objective,
                tasks = specs,
                budget = c.Budget,
            }
        );
        var p = new CompanyPlan(
            id,
            ceo.Id,
            objective,
            specs,
            digest,
            "proposed",
            id,
            c.Budget,
            [],
            Clock().AddMinutes(30)
        );
        Put(db, p);
        Event(db, "plan.proposed", id, ceo.Id, "A bounded delegation plan is ready for owner approval.");
        return p;
    }

    private CompanyPlan ChangePlan(SqliteConnection db, CompanyCommand c)
    {
        var p = Need<CompanyPlan>(db, c.Id);
        Version(p.Version, c.ExpectedVersion);
        if (c.Action == "plan.cancel")
        {
            if (p.State is "canceled" or "completed")
                throw new WorkspaceException("Plan is already terminal.");
            p = p with { State = "canceled", Version = p.Version + 1 };
            Put(db, p);
            foreach (
                var t in All<CompanyTask>(db)
                    .Where(t => t.PlanId == p.Id && t.State is not ("completed" or "canceled" or "superseded"))
            )
                Stop(db, t, "canceled", "Owner canceled the parent plan.");
        }
        else
        {
            if (p.State != "proposed" || c.Digest != p.Digest || Clock() >= p.Deadline)
                throw new WorkspaceException("Approve the exact current, unexpired proposed plan.");
            if (Need<CompanyEmployee>(db, p.CeoId).State != "active")
                throw new WorkspaceException("The plan facilitator is not active.");
            Save(db, new CompanyBudget(p.RootId, p.Budget), p.RootId);
            var ids = new List<string>();
            foreach (var s in p.Tasks)
                ids.Add(
                    NewTask(
                        db,
                        s.EmployeeId,
                        s.ThreadId,
                        s.Skill,
                        s.Input,
                        s.Evidence,
                        s.DependsOn.Select(index => ids[index]).ToArray(),
                        p.RootId,
                        p.Id,
                        null
                    ).Id
                );
            p = p with { State = "executing", TaskIds = [.. ids], Version = p.Version + 1 };
            Put(db, p);
        }
        Event(db, "plan." + p.State, p.Id, p.CeoId, "Delegation plan: " + p.State);
        return p;
    }

    private CompanyMeeting CreateMeeting(SqliteConnection db, CompanyCommand c)
    {
        var participants = c.Participants ?? [];
        if (
            participants.Length is < 1 or > 5
            || participants.Distinct().Count() != participants.Length
            || !participants.Contains(c.ChairId)
            || c.Budget is < 5000 or > 50000
        )
            throw new WorkspaceException(
                "Choose 1–5 distinct employees including a chair and a 5,000–50,000 token allowance.",
                422
            );
        foreach (var id in participants)
        {
            var e = Need<CompanyEmployee>(db, id);
            if (e.State != "active" || !Role(e.RoleId).Skills.Contains("meeting-turn"))
                throw new WorkspaceException("All participants must be active meeting contributors.");
        }
        var chair = Need<CompanyEmployee>(db, c.ChairId);
        if (!Role(chair.RoleId).Skills.Contains("meeting-chair"))
            throw new WorkspaceException("Choose a qualified meeting chair.");
        Evidence(db, c.Evidence ?? []);
        var idNew = Id();
        var m = new CompanyMeeting(
            idNew,
            Text(c.Name, 160),
            Text(c.Body, 2000),
            participants,
            c.ChairId!,
            c.Evidence ?? [],
            "draft",
            idNew,
            c.Budget,
            Clock().AddMinutes(15),
            [],
            null,
            null,
            null
        );
        Put(db, m);
        return m;
    }

    private CompanyMeeting ChangeMeeting(SqliteConnection db, CompanyCommand c)
    {
        var m = Need<CompanyMeeting>(db, c.Id);
        Version(m.Version, c.ExpectedVersion);
        switch (c.Action)
        {
            case "meeting.start":
                if (m.State != "draft")
                    throw new WorkspaceException("Only a draft can be opened.");
                Evidence(db, m.Evidence);
                Save(db, new CompanyBudget(m.RootId, m.Budget), m.RootId);
                m = m with { State = "in_progress", Deadline = Clock().AddMinutes(15), Version = m.Version + 1 };
                Put(db, m);
                m = QueueMeetingTurn(db, m);
                break;
            case "meeting.next":
                if (
                    m.State is not ("in_progress" or "paused" or "blocked")
                    || m.ActiveTask is not null
                    || Clock() >= m.Deadline
                )
                    throw new WorkspaceException("Meeting cannot advance. Resolve any active or uncertain turn first.");
                var meetingTasks = All<CompanyTask>(db).Where(t => t.MeetingId == m.Id).Select(t => t.Id).ToHashSet();
                if (All<CompanyAttempt>(db).Any(a => meetingTasks.Contains(a.TaskId) && a.Phase == "unknown"))
                    throw new WorkspaceException("An uncertain turn requires reconciliation; it is never replayed.");
                m = m with { State = "in_progress", Version = m.Version + 1 };
                Put(db, m);
                m = QueueMeetingTurn(db, m);
                break;
            case "meeting.pause":
            case "meeting.cancel":
                if (m.State is "closed" or "canceled")
                    throw new WorkspaceException("Meeting is terminal.");
                if (m.ActiveTask is { } active)
                {
                    var t = Need<CompanyTask>(db, active);
                    if (t.State is not ("completed" or "canceled" or "superseded"))
                        Stop(db, t, "canceled", "Owner stopped this meeting turn.");
                }
                m = m with
                {
                    State = c.Action == "meeting.cancel" ? "canceled" : "paused",
                    ActiveTask = null,
                    Version = m.Version + 1,
                };
                Put(db, m);
                break;
            case "meeting.outcome":
                if (m.State != "outcome_proposed" || m.ActiveTask is not null)
                    throw new WorkspaceException("Complete the bounded discussion before proposing outcomes.");
                var body = Text(c.Body, 8000);
                m = m with
                {
                    Outcome = body,
                    OutcomeDigest = EmployeeStore.Hash(body),
                    State = "awaiting_owner",
                    Version = m.Version + 1,
                };
                Put(db, m);
                break;
            case "meeting.close":
                if (m.State != "awaiting_owner" || m.OutcomeDigest != c.Digest)
                    throw new WorkspaceException("Accept the exact proposed minutes.");
                m = m with { State = "closed", Version = m.Version + 1 };
                Put(db, m);
                break;
        }
        Event(
            db,
            "meeting." + m.State,
            m.Id,
            m.ChairId,
            "Board Meeting: " + m.State + ". Minutes do not authorize follow-up actions."
        );
        return m;
    }

    private CompanyMeeting QueueMeetingTurn(SqliteConnection db, CompanyMeeting m)
    {
        if (m.Turns.Length >= m.Participants.Length * 2)
        {
            m = m with { State = "outcome_proposed", Version = m.Version + 1 };
            Put(db, m);
            return m;
        }
        var order = m.Participants.Where(id => id != m.ChairId).Append(m.ChairId).ToArray();
        var employee = order[m.Turns.Length % order.Length];
        var t = All<CompanyThread>(db)
            .FirstOrDefault(t =>
                t.EmployeeId == employee && t.Scope == "employee" && t.Title == "Board Meeting " + m.Id
            );
        if (t is null)
        {
            t = new CompanyThread(Id(), employee, "Board Meeting " + m.Id);
            Save(db, t, t.Id, employee: employee);
        }
        var final = m.Turns.Length == m.Participants.Length * 2 - 1;
        var task = NewTask(
            db,
            employee,
            t.Id,
            "meeting-turn",
            final
                ? "Synthesize decisions, dissent, open questions and proposed actions. Cite selected evidence. Do not execute actions."
                : "Contribute to the agenda from the shared meeting evidence. Distinguish facts, assumptions and recommendations.",
            m.Evidence,
            [],
            m.RootId,
            null,
            m.Id
        );
        m = m with { ActiveTask = task.Id, Version = m.Version + 1 };
        Put(db, m);
        return m;
    }
}
