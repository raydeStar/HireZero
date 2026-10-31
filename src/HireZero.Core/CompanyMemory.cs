using System.Globalization;
using System.Text;

namespace HireZero.Core;

public sealed record CompanyMemoryInput(
    string Kind,
    string Title,
    string Statement,
    string Rationale,
    string? DepartmentId = null,
    string? EmployeeId = null,
    string? ReviewOn = null,
    string? ArtifactId = null,
    string? Quote = null,
    string? ReplacesId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyMemoryRevisionSource? RevisionSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? MeetingId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyProposalFeedback? ProposalFeedback = null
);

public sealed record CompanyMemoryOrigin(
    string ArtifactId,
    int Version,
    string Digest,
    string TaskId,
    string EmployeeId,
    string Quote
);

public sealed record CompanyMemoryReplacement(string Id, int Version, string Digest);

public sealed record CompanyMemoryMeetingOrigin(string MeetingId, int Version, string Digest, string Quote);

public sealed record CompanyMemoryMeeting(string Id, string Title, int Version, string Digest, string Text);

public sealed record CompanyMemoryItem(
    string Id,
    CompanyMemoryInput Definition,
    CompanyMemoryOrigin? Origin,
    CompanyMemoryReplacement? Replacement,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt = null,
    string? Reason = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyMemoryMeetingOrigin? MeetingOrigin = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? DeletionScope = null
);

public sealed record CompanyMemoryView(
    CompanyMemoryItem Memory,
    bool Usable,
    string? Exclusion,
    bool AnalysisEligible = false,
    bool RevisionCurrent = true
);

public sealed record CompanyMemoryWork(
    string Id,
    string TaskId,
    string EmployeeId,
    string EmployeeName,
    string Skill,
    int Version,
    string Digest,
    string? Text = null
);

public sealed record CompanyMemoryExcerpt(
    string MemoryId,
    int Version,
    string Digest,
    string Kind,
    string Title,
    string Statement,
    string Rationale,
    DateTimeOffset ApprovedAt,
    string? ReviewOn,
    CompanyMemoryOrigin? Origin,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyMemoryMeetingOrigin? MeetingOrigin = null
);

public sealed record CompanyMemoryRetrieval(
    string Query,
    CompanyMemoryExcerpt[] Items,
    string Notice,
    string Method = "lexical-memory-full-request-v2"
);

public sealed partial class CompanyStore
{
    public static readonly string[] MemoryKinds =
    [
        "decision",
        "goal",
        "preference",
        "project",
        "feedback",
        "prior-work",
    ];
    private const string MemorySchema =
        "CREATE TABLE co_memory(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id),FOREIGN KEY(company_id,parent_id) REFERENCES co_departments(company_id,id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));"
        + "CREATE TABLE co_memory_terms(company_id TEXT NOT NULL,memory_id TEXT NOT NULL,term TEXT NOT NULL,weight INTEGER NOT NULL,PRIMARY KEY(company_id,memory_id,term),FOREIGN KEY(company_id,memory_id) REFERENCES co_memory(company_id,id));"
        + "CREATE INDEX co_memory_term_lookup ON co_memory_terms(company_id,term);";

    private static DateTimeOffset? MemoryReviewDate(string? date) =>
        date is null ? null
        : DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        : throw new WorkspaceException("Choose a review date as yyyy-MM-dd, or leave it blank.", 422);

    private bool MemoryOriginCurrent(StoreConnection db, CompanyMemoryOrigin? origin, int depth = 0)
    {
        if (origin is null)
            return true;
        if (depth >= 16)
            return false;
        var artifact = Find<CompanyArtifact>(db, origin.ArtifactId);
        var task = artifact is null ? null : Find<CompanyTask>(db, artifact.TaskId);
        return artifact is { State: "approved" }
            && SandboxArtifactCurrent(db, artifact, depth + 1)
            && artifact.Version == origin.Version
            && artifact.Digest == origin.Digest
            && artifact.TaskId == origin.TaskId
            && artifact.EmployeeId == origin.EmployeeId
            && artifact.Body.Contains(origin.Quote, StringComparison.Ordinal)
            && task is not null
            && task.State is not ("canceled" or "superseded")
            && (
                artifact.ContextId is null
                || Find<CompanyContext>(db, artifact.ContextId) is { } context
                    && KnowledgeCurrent(db, context, task, depth + 1)
            );
    }

    private string? MemoryExclusion(StoreConnection db, CompanyMemoryItem memory, int depth = 0)
    {
        if (memory.State != "approved")
            return memory.Reason ?? "This record is " + memory.State + ".";
        if (MemoryReviewDate(memory.Definition.ReviewOn) is { } due && due <= Clock())
            return "Its review date has arrived. Review a new revision before using it again.";
        if (
            memory.Definition.EmployeeId is { } employee
            && Find<CompanyEmployee>(db, employee) is not { State: "active" }
        )
            return "Its employee audience is not active.";
        if (!MemoryOriginCurrent(db, memory.Origin, depth))
            return "Its cited work or the context behind that work is no longer approved/current.";
        return MemoryMeetingOriginCurrent(db, memory.MeetingOrigin)
            ? null
            : "Its cited meeting minutes are no longer accepted/current.";
    }

    private CompanyMemoryView MemoryView(StoreConnection db, CompanyMemoryItem memory)
    {
        var why = MemoryExclusion(db, memory);
        return new(
            memory,
            why is null,
            why,
            MemoryAnalysisSourceAllowed(db, memory),
            memory.State != "draft" || MemoryRevisionCurrent(db, memory.Definition)
        );
    }

    public CompanyMemoryView[] Memories()
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
                All<CompanyMemoryItem>(db)
                    .Where(m => m.State != "deleted")
                    .OrderByDescending(m => m.UpdatedAt)
                    .Select(m => MemoryView(db, m))
                    .ToArray()
        );
    }

    public CompanyMemoryView Memory(string id)
    {
        using var db = Open();
        return Transaction(db, () => MemoryView(db, Need<CompanyMemoryItem>(db, id)));
    }

    public CompanyMemoryWork[] MemoryWorks()
    {
        using var db = Open();
        return All<CompanyArtifact>(db)
            .Where(a => MemoryWorkCurrent(db, a))
            .TakeLast(100)
            .Reverse()
            .Select(a => MemoryWork(db, a, false))
            .ToArray();
    }

    public CompanyMemoryWork MemoryWork(string id)
    {
        using var db = Open();
        return MemoryWork(db, Need<CompanyArtifact>(db, id), true);
    }

    public CompanyMemoryMeeting MemoryMeeting(string id)
    {
        using var db = Open();
        var meeting = Need<CompanyMeeting>(db, id);
        if (meeting is not { State: "closed", Outcome: not null, OutcomeDigest: not null })
            throw new WorkspaceException(
                "Accept the meeting's exact minutes before using them as a memory source.",
                409
            );
        return new(meeting.Id, meeting.Title, meeting.Version, meeting.OutcomeDigest, meeting.Outcome);
    }

    private bool MemoryMeetingOriginCurrent(StoreConnection db, CompanyMemoryMeetingOrigin? origin) =>
        origin is null
        || Find<CompanyMeeting>(db, origin.MeetingId) is { State: "closed", Outcome: not null } meeting
            && meeting.Version == origin.Version
            && meeting.OutcomeDigest == origin.Digest
            && meeting.Outcome.Contains(origin.Quote, StringComparison.Ordinal);

    private CompanyMemoryWork MemoryWork(StoreConnection db, CompanyArtifact artifact, bool text)
    {
        if (!MemoryWorkCurrent(db, artifact))
            throw new WorkspaceException(
                "This result or its supporting context is no longer current. Choose current approved work before capturing feedback.",
                409
            );
        return new(
            artifact.Id,
            artifact.TaskId,
            artifact.EmployeeId,
            Need<CompanyEmployee>(db, artifact.EmployeeId).Name,
            Need<CompanyTask>(db, artifact.TaskId).Skill,
            artifact.Version,
            artifact.Digest,
            text ? artifact.Body : null
        );
    }

    private bool MemoryWorkCurrent(StoreConnection db, CompanyArtifact artifact) =>
        MemoryOriginCurrent(
            db,
            new(
                artifact.Id,
                artifact.Version,
                artifact.Digest,
                artifact.TaskId,
                artifact.EmployeeId,
                artifact.Body[..Math.Min(600, artifact.Body.Length)]
            )
        );

    private object KeepLesson(StoreConnection db, CompanyCommand command)
    {
        var source = Need<CompanyArtifact>(db, command.Id);
        var lesson = Text(command.Body ?? "", 1200).Trim();
        if (source.Digest != command.Digest)
            throw new WorkspaceException("The report changed. Read the current lesson before keeping it.", 409);
        if (command.Decision != "keep" || lesson.Length == 0 || !FeedbackSourceContains(source, lesson))
            throw new WorkspaceException("Choose the exact lesson from this report to keep.", 422);
        var existing = All<CompanyMemoryItem>(db)
            .FirstOrDefault(m =>
                m.State == "approved"
                && m.Definition.EmployeeId == source.EmployeeId
                && m.Definition.Kind == "feedback"
                && m.Definition.Statement == lesson
                && MemoryExclusion(db, m) is null
            );
        if (existing is not null)
            return existing;
        var saved = (CompanyMemoryItem)SaveMemory(
            db,
            command with
            {
                Id = null,
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(
                    new CompanyMemoryInput(
                        "feedback",
                        "Lesson: " + lesson[..Math.Min(140, lesson.Length)],
                        lesson,
                        "Owner kept this lesson from an employee report; this does not verify the report's other claims.",
                        EmployeeId: source.EmployeeId
                    )
                ),
            }
        );
        // One transaction: a lost reply must never leave half a lesson on the butler's tray.
        return ReviewMemory(
            db,
            command with
            {
                Id = saved.Id,
                ExpectedVersion = saved.Version,
                Digest = saved.Digest,
                Decision = "approve",
            }
        );
    }

    private object SaveMemory(StoreConnection db, CompanyCommand command)
    {
        var input = CompanyJson.Decode<CompanyMemoryInput>(command.Body ?? "null");
        if (input.ProposalFeedback is not null && command.Action != "proposal.feedback")
            throw new WorkspaceException("Save proposal feedback from the exact result being reviewed.", 422);
        var previous = command.Id is null ? null : Need<CompanyMemoryItem>(db, command.Id);
        Version(previous?.Version ?? 0, command.ExpectedVersion);
        if (previous is not null && previous.State != "draft")
            throw new WorkspaceException("Create a revision to change reviewed memory.", 409);
        if (previous?.Definition.RevisionSource is { } provenance && input.RevisionSource != provenance)
            throw new WorkspaceException(
                "Keep this draft's report provenance. Discard it to write an independent revision.",
                409
            );
        if (!MemoryRevisionCurrent(db, input))
            throw new WorkspaceException(
                "The suggested revision's sources changed or its scope differs. Check current memory before creating another revision.",
                409
            );
        if (!MemoryKinds.Contains(input.Kind))
            throw new WorkspaceException(
                "Choose a decision, goal, preference, project, feedback or prior-work record.",
                422
            );
        input = input with
        {
            Title = Text(input.Title, 160).Trim(),
            Statement = Text(input.Statement, 1200).Trim(),
            Rationale = ContentGuard.Clean(input.Rationale ?? "", 600).Text.Trim(),
            ReviewOn = string.IsNullOrWhiteSpace(input.ReviewOn) ? null : input.ReviewOn,
            Quote = string.IsNullOrWhiteSpace(input.Quote) ? null : Text(input.Quote, 600).Trim(),
        };
        if (input.Title.Length == 0 || input.Statement.Length == 0)
            throw new WorkspaceException("Name the memory and write its concise statement.", 422);
        if (input.DepartmentId is not null && input.EmployeeId is not null)
            throw new WorkspaceException("Choose one employee audience.", 422);
        if (input.DepartmentId is { } department)
            Need<CompanyDepartment>(db, department);
        if (input.EmployeeId is { } employee && Need<CompanyEmployee>(db, employee).State == "retired")
            throw new WorkspaceException("Choose an employee who has not retired.", 422);
        if (MemoryReviewDate(input.ReviewOn) is { } due && (due <= Clock() || due > Clock().AddDays(366)))
            throw new WorkspaceException(
                "Choose a future review date within one year, or leave it blank for no automatic expiry.",
                422
            );
        CompanyMemoryOrigin? origin = null;
        CompanyMemoryMeetingOrigin? meetingOrigin = null;
        if (input.ArtifactId is not null && input.MeetingId is not null)
            throw new WorkspaceException("Cite either an approved result or accepted meeting minutes, not both.", 422);
        if (input.ArtifactId is { } artifactId)
        {
            var artifact = Need<CompanyArtifact>(db, artifactId);
            if (input.Quote is null || !artifact.Body.Contains(input.Quote, StringComparison.Ordinal))
                throw new WorkspaceException("Quote up to 600 exact characters from the approved result.", 422);
            origin = new(
                artifact.Id,
                artifact.Version,
                artifact.Digest,
                artifact.TaskId,
                artifact.EmployeeId,
                input.Quote
            );
            if (!MemoryOriginCurrent(db, origin))
                throw new WorkspaceException("The cited work or its supporting context is no longer current.", 409);
        }
        else if (input.MeetingId is { } meetingId)
        {
            var meeting = Need<CompanyMeeting>(db, meetingId);
            if (meeting is not { State: "closed", Outcome: not null, OutcomeDigest: not null })
                throw new WorkspaceException("Only accepted meeting minutes can support a memory draft.", 409);
            if (input.Quote is null || !meeting.Outcome.Contains(input.Quote, StringComparison.Ordinal))
                throw new WorkspaceException("Quote up to 600 exact characters from the accepted minutes.", 422);
            meetingOrigin = new(meeting.Id, meeting.Version, meeting.OutcomeDigest, input.Quote);
        }
        else if (input.Quote is not null || input.Kind == "prior-work")
            throw new WorkspaceException(
                "Cite an approved result or accepted minutes with an exact supporting quote.",
                422
            );
        CompanyMemoryReplacement? replacement = null;
        if (input.ReplacesId is { } oldId)
        {
            var old = Need<CompanyMemoryItem>(db, oldId);
            if (old.Id == previous?.Id || !MemoryReplacementAllowed(old))
                throw new WorkspaceException("Revise a different approved, paused or revoked memory record.", 409);
            replacement = new(old.Id, old.Version, old.Digest);
        }
        var all = All<CompanyMemoryItem>(db).Where(m => m.Id != previous?.Id && m.State != "deleted").ToArray();
        if (
            all.Length >= 200
            || all.Sum(m => Encoding.UTF8.GetByteCount(CompanyJson.Encode(m.Definition)))
                + Encoding.UTF8.GetByteCount(CompanyJson.Encode(input))
                > 1024 * 1024
        )
            throw new WorkspaceException(
                "This company reached its retained memory allowance: 200 records or 1 MiB. Review deletion of unused drafts or revoked/replaced saved records to make room.",
                429
            );
        var memory = new CompanyMemoryItem(
            previous?.Id ?? Id(),
            input,
            origin,
            replacement,
            meetingOrigin is null
                ? CompanyJson.Digest(
                    new
                    {
                        input,
                        origin,
                        replacement,
                    }
                )
                : CompanyJson.Digest(
                    new
                    {
                        input,
                        origin,
                        replacement,
                        meetingOrigin,
                    }
                ),
            "draft",
            (previous?.Version ?? 0) + 1,
            Clock(),
            MeetingOrigin: meetingOrigin
        );
        PutMemory(db, memory);
        Event(
            db,
            "memory.drafted",
            memory.Id,
            input.EmployeeId,
            "A structured memory draft is ready for owner review."
        );
        return memory;
    }

    private void PutMemory(StoreConnection db, CompanyMemoryItem memory) =>
        Save(
            db,
            memory,
            memory.Id,
            memory.Definition.DepartmentId,
            memory.Definition.EmployeeId,
            memory.State,
            memory.Version
        );

    private static bool MemoryReplacementAllowed(CompanyMemoryItem memory) =>
        memory.State is "approved" or "needs_review" or "revoked"
        || memory is { State: "deleted", DeletionScope: "retired-source", ApprovedAt: not null };

    private void ClearMemoryIndex(StoreConnection db, string id) =>
        Sql(db, "DELETE FROM co_memory_terms WHERE company_id=$0 AND memory_id=$1", CompanyId, id);

    private void IndexMemory(StoreConnection db, CompanyMemoryItem memory)
    {
        var title = DocumentTerms(memory.Definition.Title, 24).ToHashSet();
        foreach (
            var term in DocumentTerms(
                    memory.Definition.Statement
                        + " "
                        + memory.Definition.Rationale
                        + " "
                        + memory.Origin?.Quote
                        + " "
                        + memory.MeetingOrigin?.Quote,
                    256
                )
                .Concat(title)
                .Distinct()
        )
            Sql(
                db,
                "INSERT INTO co_memory_terms(company_id,memory_id,term,weight) VALUES($0,$1,$2,$3)",
                CompanyId,
                memory.Id,
                term,
                title.Contains(term) ? 3 : 1
            );
    }

    private object ReviewMemory(StoreConnection db, CompanyCommand command)
    {
        var memory = Need<CompanyMemoryItem>(db, command.Id);
        Version(memory.Version, command.ExpectedVersion);
        if (memory.Digest != command.Digest)
            throw new WorkspaceException("Review the exact memory, source, audience and replacement.", 409);
        var approving =
            command.Decision == (memory.Replacement is null ? "approve" : "approve-and-replace")
            && memory.State == "draft";
        var state = approving
            ? "approved"
            : (command.Decision, memory.State) switch
            {
                ("revoke", "approved" or "needs_review") => "revoked",
                ("discard", "draft") => "discarded",
                _ => throw new WorkspaceException(
                    "Approve the exact draft, explicitly replace its earlier version, revoke reviewed memory or discard a draft.",
                    409
                ),
            };
        if (approving)
        {
            if (!MemoryRevisionCurrent(db, memory.Definition))
                throw new WorkspaceException(
                    "The report's supporting memory changed. Discard this draft and review current records before approving a revision.",
                    409
                );
            if (MemoryExclusion(db, memory with { State = "approved" }) is { } why)
                throw new WorkspaceException(why, 409);
            if (
                All<CompanyMemoryItem>(db)
                    .Any(m =>
                        m.State == "approved"
                        && m.Id != memory.Replacement?.Id
                        && m.Definition.Kind == memory.Definition.Kind
                        && m.Definition.Title.Equals(memory.Definition.Title, StringComparison.OrdinalIgnoreCase)
                        && m.Definition.EmployeeId == memory.Definition.EmployeeId
                        && m.Definition.DepartmentId == memory.Definition.DepartmentId
                    )
            )
                throw new WorkspaceException(
                    "A current record already has this kind, title and audience. Revise it explicitly.",
                    409
                );
            if (memory.Replacement is { } replaces)
            {
                var old = Need<CompanyMemoryItem>(db, replaces.Id);
                if (old.Version != replaces.Version || old.Digest != replaces.Digest || !MemoryReplacementAllowed(old))
                    throw new WorkspaceException(
                        "The earlier memory changed. Save the draft again and review the replacement.",
                        409
                    );
                // A replacement may cite the tombstone; the butler does not restock an erased shelf.
                if (old.State != "deleted")
                    PutMemory(
                        db,
                        old with
                        {
                            State = "superseded",
                            Version = old.Version + 1,
                            UpdatedAt = Clock(),
                            Reason = "Replaced by " + memory.Id + ".",
                        }
                    );
                ClearMemoryIndex(db, old.Id);
                // A citation cannot stand on the decision this very approval retires.
                if (MemoryExclusion(db, memory with { State = "approved" }) is { } replacementReason)
                    throw new WorkspaceException(replacementReason, 409);
            }
        }
        memory = memory with
        {
            State = state,
            Version = memory.Version + 1,
            UpdatedAt = Clock(),
            ApprovedAt = approving ? Clock() : memory.ApprovedAt,
        };
        PutMemory(db, memory);
        ClearMemoryIndex(db, memory.Id);
        if (approving)
            IndexMemory(db, memory);
        Event(
            db,
            "memory." + state,
            memory.Id,
            memory.Definition.EmployeeId,
            state == "discarded"
                ? "An unapproved memory draft was discarded."
                : "Organizational memory " + state + ": " + memory.Definition.Title
        );
        return memory;
    }

    public int MaintainMemory()
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var changed = 0;
                foreach (var memory in All<CompanyMemoryItem>(db).Where(m => m.State == "approved"))
                    if (MemoryExclusion(db, memory) is { } reason)
                    {
                        PutMemory(
                            db,
                            memory with
                            {
                                State = "needs_review",
                                Version = memory.Version + 1,
                                UpdatedAt = Clock(),
                                Reason = reason,
                            }
                        );
                        ClearMemoryIndex(db, memory.Id);
                        Event(
                            db,
                            "memory.review-due",
                            memory.Id,
                            memory.Definition.EmployeeId,
                            "Review memory: " + memory.Definition.Title + ". " + reason
                        );
                        changed++;
                    }
                return changed;
            }
        );
    }

    public CompanyMemoryRetrieval RetrieveMemory(string query, string? employeeId = null, bool companyOnly = false)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
                RetrieveMemory(
                    db,
                    Text(query, 4000),
                    employeeId is null ? null : Need<CompanyEmployee>(db, employeeId),
                    companyOnly
                )
        );
    }

    private CompanyMemoryRetrieval RetrieveMemory(
        StoreConnection db,
        string query,
        CompanyEmployee? employee,
        bool companyOnly
    )
    {
        var terms = DocumentTerms(query, int.MaxValue);
        query = RetrievalQueryPreview(terms);
        if (terms.Length == 0)
            return new(query, [], "Name the topic, decision or goal you need.");
        var args = new List<object?>
        {
            CompanyId,
            companyOnly ? null : employee?.DepartmentId,
            companyOnly ? null : employee?.Id,
        };
        args.AddRange(terms);
        var parameters = string.Join(',', terms.Select((_, i) => "$" + (i + 3)));
        var found = Rows(
            db,
            "SELECT m.payload,SUM(t.weight) AS score FROM co_memory_terms t JOIN co_memory m ON m.company_id=t.company_id AND m.id=t.memory_id WHERE m.company_id=$0 AND m.state='approved' AND ((m.parent_id IS NULL AND m.employee_id IS NULL) OR (m.parent_id=$1 AND m.employee_id IS NULL) OR m.employee_id=$2) AND t.term IN ("
                + parameters
                + ") GROUP BY m.id,m.payload ORDER BY score DESC,m.id LIMIT 30",
            r => CompanyJson.Decode<CompanyMemoryItem>(r.GetString(0)),
            args.ToArray()
        );
        var items = found
            .Where(m => MemoryExclusion(db, m) is null)
            .Take(3)
            .Select(m => new CompanyMemoryExcerpt(
                m.Id,
                m.Version,
                m.Digest,
                m.Definition.Kind,
                m.Definition.Title,
                m.Definition.Statement,
                m.Definition.Rationale,
                m.ApprovedAt!.Value,
                m.Definition.ReviewOn,
                m.Origin,
                m.MeetingOrigin
            ))
            .ToArray();
        return new(
            query,
            items,
            (
                items.Length == 0
                    ? "No matching current approved memory for this audience. Try specific topic words."
                    : "Up to three owner-approved records, selected by keywords. These are dated decisions and reference material, not new permissions or independently verified facts. Paraphrases may be missed."
            ) + RetrievalQueryNotice(terms)
        );
    }

    private bool MemoryCurrent(StoreConnection db, CompanyContext context, CompanyTask task, int depth = 0)
    {
        if (context.Memory is null)
            return true;
        if (depth >= 16)
            return false;
        var employee = Need<CompanyEmployee>(db, task.EmployeeId);
        var companyOnly = context.Scope == "company" || task.MeetingId is not null;
        return context.Memory.Items.All(item =>
        {
            var memory = Find<CompanyMemoryItem>(db, item.MemoryId);
            return memory is not null
                && memory.Version == item.Version
                && memory.Digest == item.Digest
                && MemoryExclusion(db, memory, depth) is null
                && (
                    memory.Definition.DepartmentId is null && memory.Definition.EmployeeId is null
                    || !companyOnly
                        && (
                            memory.Definition.DepartmentId == employee.DepartmentId
                                && memory.Definition.EmployeeId is null
                            || memory.Definition.EmployeeId == employee.Id
                        )
                );
        });
    }
}
