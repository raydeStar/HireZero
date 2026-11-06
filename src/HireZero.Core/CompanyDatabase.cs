using System.Data.Common;

namespace HireZero.Core;

// All company mutations, their operation receipt and feed events commit together. This module does not migrate legacy tables.
public sealed partial class CompanyStore
{
    private readonly StoreDatabase _database;
    private readonly CompanyRole[] _roles;
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    // The company a data root starts with; more can be created beside it (docs/adr/0021-companies-per-data-root.md).
    public const string DefaultCompanyId = "local-company";

    // Every query is scoped to this company; other companies' rows in the same database are invisible.
    public string CompanyId { get; }
    public CompanyRole[] Roles => _roles;
    private readonly CompanyModelRouting _modelRouting;
    private readonly string? _measuredRuntimeIdentity;
    public Func<CompanyModelRouting>? CurrentModelRouting { get; init; }
    public Func<string?>? CurrentMeasuredRuntimeIdentity { get; init; }
    public CompanyModelRouting ModelRouting => CurrentModelRouting?.Invoke() ?? _modelRouting;
    public string? MeasuredRuntimeIdentity =>
        CurrentMeasuredRuntimeIdentity is { } current ? current() : _measuredRuntimeIdentity;
    public SemaphoreSlim ProviderGate { get; } = new(1, 1);

    // The limits proposed with each execution grant unless the module asks for others; the owner approves them.
    public CompanyExecutionLimits ExecutionDefaults { get; init; } = CompanyExecutionLimits.Default;
    private static readonly Dictionary<Type, string> Tables = new()
    {
        [typeof(CompanyDepartment)] = "co_departments",
        [typeof(CompanyEmployee)] = "co_employees",
        [typeof(CompanyKnowledge)] = "co_knowledge",
        [typeof(CompanyThread)] = "co_threads",
        [typeof(CompanyMessage)] = "co_messages",
        [typeof(CompanyNotes)] = "co_notes",
        [typeof(CompanyTask)] = "co_tasks",
        [typeof(CompanyAttempt)] = "co_attempts",
        [typeof(CompanyContext)] = "co_contexts",
        [typeof(CompanyArtifact)] = "co_artifacts",
        [typeof(CompanyHandoff)] = "co_handoffs",
        [typeof(CompanyBudget)] = "co_budgets",
        [typeof(CompanyReservation)] = "co_reservations",
        [typeof(CompanyResourceClaim)] = "co_resource_claims",
        [typeof(CompanyPlan)] = "co_plans",
        [typeof(CompanyMeeting)] = "co_meetings",
        [typeof(CompanyExecutionGrant)] = "co_execution_grants",
        [typeof(CompanyPublication)] = "co_publications",
        [typeof(CompanyOwnerMark)] = "co_owner_marks",
        [typeof(CompanyMember)] = "co_members",
        [typeof(CompanyConnectionRequest)] = "co_connections",
        [typeof(CompanyBusinessIdentity)] = "co_business",
        [typeof(CompanyBusinessSource)] = "co_business_sources",
        [typeof(CompanyBusinessProfile)] = "co_business_profiles",
        [typeof(CompanyBusinessDraft)] = "co_business_drafts",
        [typeof(CompanyBusinessLookup)] = "co_business_lookups",
        [typeof(CompanyProfileRevision)] = "co_business_history",
        [typeof(CompanyDocument)] = "co_documents",
        [typeof(CompanySourceRefresh)] = "co_business_refreshes",
        [typeof(CompanySourceCheck)] = "co_business_checks",
        [typeof(CompanyFactReview)] = "co_business_fact_reviews",
        [typeof(CompanySchedule)] = "co_schedules",
        [typeof(CompanyScheduleRun)] = "co_schedule_runs",
        [typeof(CompanyNotice)] = "co_notices",
        [typeof(CompanyNoticePreferences)] = "co_notice_preferences",
        [typeof(CompanyNoticeDelivery)] = "co_notice_deliveries",
        [typeof(CompanyVerifiedEmail)] = "co_verified_emails",
        [typeof(CompanyMemoryItem)] = "co_memory",
        [typeof(CompanyMemoryAnalysis)] = "co_memory_analyses",
        [typeof(CompanyDecision)] = "co_decisions",
        [typeof(CompanyReading)] = "co_readings",
        [typeof(CompanyTaskCostReview)] = "co_task_cost_reviews",
        [typeof(CompanyUsagePolicy)] = "co_usage_policy",
        [typeof(CompanyUsageEntry)] = "co_usage_entries",
        [typeof(CompanyBillingCheckout)] = "co_billing_checkouts",
        [typeof(CompanyBillingState)] = "co_billing_state",
        [typeof(CompanyBillingPortal)] = "co_billing_portals",
        [typeof(CompanySandboxJob)] = "co_sandbox_jobs",
        [typeof(CompanyBrowserJob)] = "co_browser_jobs",
        [typeof(CompanyBrowserRetention)] = "co_browser_retention",
        [typeof(CompanyComputePolicy)] = "co_compute_policy",
        [typeof(CompanyDocumentRetention)] = "co_document_retention",
    };
    private static readonly Dictionary<string, string> ParentTables = new()
    {
        ["co_employees"] = "co_departments",
        ["co_knowledge"] = "co_departments",
        ["co_messages"] = "co_threads",
        ["co_tasks"] = "co_threads",
        ["co_attempts"] = "co_tasks",
        ["co_contexts"] = "co_attempts",
        ["co_artifacts"] = "co_tasks",
        ["co_handoffs"] = "co_artifacts",
        ["co_reservations"] = "co_budgets",
    };

    public CompanyStore(
        string path,
        string owner,
        CompanyRole[] roles,
        CompanyModelRouting? routing = null,
        string? measuredRuntimeIdentity = null,
        string companyId = DefaultCompanyId
    )
        : this(new SqliteStoreDatabase(path), owner, roles, routing, measuredRuntimeIdentity, companyId) { }

    public CompanyStore(
        StoreDatabase database,
        string owner,
        CompanyRole[] roles,
        CompanyModelRouting? routing = null,
        string? measuredRuntimeIdentity = null,
        string companyId = DefaultCompanyId
    )
    {
        if (!CompanyIdPattern().IsMatch(companyId))
            throw new ArgumentException("A company id is lowercase letters, digits and dashes.");
        CompanyId = companyId;
        _database = database;
        _owner = owner;
        _roles = roles;
        _modelRouting = routing ?? new();
        ModelRouting.Validate();
        _measuredRuntimeIdentity = measuredRuntimeIdentity;
        if (roles.Select(r => r.Id).Distinct().Count() != roles.Length)
            throw new InvalidOperationException("Duplicate role definition.");
        using var db = Open();
        // Schema changes exclude every writer of every company in the database.
        db.Transaction(
            "schema",
            () =>
            {
                // Builds before the one migration framework kept the company's steps in their own ledger.
                if (_database.TableExists(db, "company_schema_migrations"))
                {
                    SchemaMigrations.EnsureLedger(db);
                    Sql(
                        db,
                        "INSERT INTO module_schema_migrations(owner,version,checksum) SELECT 'company',version,checksum FROM company_schema_migrations"
                    );
                    Sql(db, "DROP TABLE company_schema_migrations");
                }
                string[] schemas =
                [
                    Schema(),
                    "CREATE TABLE co_execution_grants(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_tasks(company_id,id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));",
                    ToolCallSchema,
                    "CREATE TABLE co_publications(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_tasks(company_id,id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));",
                    "CREATE TABLE co_owner_marks(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id));",
                    "CREATE TABLE co_members(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id));",
                    "ALTER TABLE co_company ADD COLUMN name TEXT;",
                    AuditSchema,
                    // Connect cards (docs/adr/0046-connect-cards.md).
                    "CREATE TABLE co_connections(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id));",
                    BusinessContextSchema,
                    BusinessDraftSchema,
                    BusinessLookupSchema,
                    BusinessHistorySchema,
                    DocumentSchema,
                    BusinessFreshnessSchema,
                    ScheduleSchema,
                    NotificationSchema,
                    MemorySchema,
                    UsageSchema,
                    WorkspaceLeaseSchema,
                    SandboxSchema,
                    DocumentRetentionSchema,
                    BrowserSchema,
                    BrowserRetentionSchema,
                    BillingSchema,
                    BillingDeliverySchema,
                    MemoryAnalysisSchema,
                    TaskCostSchema,
                    DecisionSchema,
                    ReadingSchema,
                    SignupSchema,
                    InvitationSchema,
                ];
                try
                {
                    SchemaMigrations.Apply(db, "company", schemas);
                }
                catch (InvalidOperationException)
                {
                    throw new WorkspaceException(
                        "Company schema is newer or its migration checksum changed. No work started."
                    );
                }
                Sql(db, "INSERT INTO co_company(id,owner) VALUES($0,$1) ON CONFLICT(id) DO NOTHING", CompanyId, owner);
                if (Scalar(db, "SELECT owner FROM co_company WHERE id=$0", CompanyId) != owner)
                    throw new WorkspaceException("This company belongs to another owner.", 403);
                // The company's first owner is the identity that created it (docs/adr/0020).
                if (All<CompanyMember>(db).Length == 0)
                {
                    var first = new CompanyMember(
                        owner,
                        owner.Contains('@') ? owner : "",
                        "Owner",
                        CompanyRoles.Owner,
                        "active"
                    );
                    Save(db, first, first.Id, state: first.State, version: first.Version);
                }
                return true;
            }
        );
    }

    private static string Schema()
    {
        var statements = new List<string>
        {
            "CREATE TABLE IF NOT EXISTS co_company(id TEXT PRIMARY KEY, owner TEXT NOT NULL)",
        };
        // Step 1's text is frozen by its checksum: tables added later are created by their own steps.
        foreach (
            var table in Tables.Values.Where(table =>
                table
                    is not (
                        "co_execution_grants"
                        or "co_publications"
                        or "co_owner_marks"
                        or "co_members"
                        or "co_connections"
                        or "co_business"
                        or "co_business_sources"
                        or "co_business_profiles"
                        or "co_business_drafts"
                        or "co_business_lookups"
                        or "co_business_history"
                        or "co_documents"
                        or "co_business_refreshes"
                        or "co_business_checks"
                        or "co_business_fact_reviews"
                        or "co_schedules"
                        or "co_schedule_runs"
                        or "co_notices"
                        or "co_notice_preferences"
                        or "co_notice_deliveries"
                        or "co_verified_emails"
                        or "co_memory"
                        or "co_memory_analyses"
                        or "co_task_cost_reviews"
                        or "co_decisions"
                        or "co_usage_policy"
                        or "co_usage_entries"
                        or "co_billing_checkouts"
                        or "co_billing_state"
                        or "co_billing_portals"
                        or "co_sandbox_jobs"
                        or "co_browser_jobs"
                        or "co_browser_retention"
                        or "co_compute_policy"
                        or "co_document_retention"
                        or "co_readings"
                    )
            )
        )
        {
            var parent = ParentTables.TryGetValue(table, out var target)
                ? $",FOREIGN KEY(company_id,parent_id) REFERENCES {target}(company_id,id)"
                : "";
            var employee = table
                is "co_threads"
                    or "co_messages"
                    or "co_tasks"
                    or "co_attempts"
                    or "co_artifacts"
                    or "co_handoffs"
                    or "co_notes"
                    or "co_contexts"
                ? ",FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id)"
                : "";
            statements.Add(
                $"CREATE TABLE IF NOT EXISTS {table}(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id){parent}{employee})"
            );
        }
        statements.Add(
            "CREATE UNIQUE INDEX IF NOT EXISTS co_one_active_attempt ON co_attempts(company_id,employee_id) WHERE state IN ('claimed','reserved','dispatching')"
        );
        statements.Add(
            "CREATE UNIQUE INDEX IF NOT EXISTS co_thread_sequence ON co_messages(company_id,parent_id,json_extract(payload,'$.sequence'))"
        );
        statements.Add(
            "CREATE TABLE IF NOT EXISTS co_operations(company_id TEXT NOT NULL,id TEXT NOT NULL,digest TEXT NOT NULL,result TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id))"
        );
        statements.Add(
            "CREATE TABLE IF NOT EXISTS co_events(seq INTEGER PRIMARY KEY AUTOINCREMENT,company_id TEXT NOT NULL,payload TEXT NOT NULL,FOREIGN KEY(company_id) REFERENCES co_company(id))"
        );
        return string.Join(";\n", statements) + ";";
    }

    public StoreDatabase Database => _database;

    private StoreConnection Open() => new(_database.Open(), _database);

    private static void Sql(StoreConnection db, string sql, params object?[] values) => db.Execute(sql, values);

    private static string? Scalar(StoreConnection db, string sql, params object?[] values) => db.Scalar(sql, values);

    private static T[] Rows<T>(StoreConnection db, string sql, Func<DbDataReader, T> read, params object?[] values) =>
        db.Rows(sql, read, values);

    // Writes to one company exclude each other; other companies in the same database are not held up.
    private T Transaction<T>(StoreConnection db, Func<T> action) => db.Transaction(CompanyId, action);

    private T[] All<T>(StoreConnection db) =>
        Rows(
            db,
            $"SELECT payload FROM {Tables[typeof(T)]} WHERE company_id=$0 ORDER BY rowid",
            r => CompanyJson.Decode<T>(r.GetString(0)),
            CompanyId
        );

    private T? Find<T>(StoreConnection db, string? id)
        where T : class =>
        id is null
            ? null
            : Rows(
                    db,
                    $"SELECT payload FROM {Tables[typeof(T)]} WHERE company_id=$0 AND id=$1",
                    r => CompanyJson.Decode<T>(r.GetString(0)),
                    CompanyId,
                    id
                )
                .SingleOrDefault();

    private T Need<T>(StoreConnection db, string? id)
        where T : class => Find<T>(db, id) ?? throw new WorkspaceException("Record not found in this company.", 404);

    private void Save<T>(
        StoreConnection db,
        T value,
        string id,
        string? parent = null,
        string? employee = null,
        string state = "saved",
        int version = 1
    )
    {
        AuditTransition(db, value, id, state);
        Sql(
            db,
            $"INSERT INTO {Tables[typeof(T)]}(id,company_id,parent_id,employee_id,state,revision,payload) VALUES($0,$1,$2,$3,$4,$5,$6) ON CONFLICT(company_id,id) DO UPDATE SET parent_id=excluded.parent_id,employee_id=excluded.employee_id,state=excluded.state,revision=excluded.revision,payload=excluded.payload",
            id,
            CompanyId,
            parent,
            employee,
            state,
            version,
            CompanyJson.Encode(value)
        );
    }

    private void Event(StoreConnection db, string type, string id, string? employee, string text)
    {
        Sql(
            db,
            "INSERT INTO co_events(company_id,payload) VALUES($0,$1)",
            CompanyId,
            CompanyJson.Encode(
                new CompanyEvent(0, type, id, employee, text, Clock())
                {
                    Actor = CurrentActor.Value?.Subject ?? CompanyActor.System,
                }
            )
        );
        Audit(
            db,
            "event",
            id,
            new
            {
                type,
                employee,
                text,
            }
        );
        NoticeFromEvent(db, type, id, text);
    }

    private static string Id() => Guid.NewGuid().ToString("N");

    private static string Text(string? text, int max = 4000) => ContentGuard.Bounded(text, max);

    private static void Version(int actual, int expected)
    {
        if (actual != expected)
            throw new WorkspaceException("This record changed. Refresh before applying the command.");
    }

    private CompanyRole Role(string? id) =>
        _roles.SingleOrDefault(r => r.Id == id) ?? throw new WorkspaceException("Unknown role definition.", 422);

    private void Put(StoreConnection db, CompanyEmployee e) =>
        Save(db, e, e.Id, e.DepartmentId, state: e.State, version: e.Version);

    private void Put(StoreConnection db, CompanyTask t) =>
        Save(db, t, t.Id, t.ThreadId, t.EmployeeId, t.State, t.Version);

    private void Put(StoreConnection db, CompanyAttempt a)
    {
        Save(db, a, a.Id, a.TaskId, a.EmployeeId, a.Phase, a.Version);
        SyncUsage(db, a.Id);
    }

    private void Put(StoreConnection db, CompanyMeeting m) => Save(db, m, m.Id, state: m.State, version: m.Version);

    private void Put(StoreConnection db, CompanyPlan p) => Save(db, p, p.Id, state: p.State, version: p.Version);

    public object Dashboard()
    {
        RefreshModelReviews();
        using var db = Open();
        return Transaction(
            db,
            () =>
                new
                {
                    workControl = WorkControl(db),
                    departments = All<CompanyDepartment>(db),
                    employees = All<CompanyEmployee>(db),
                    knowledge = All<CompanyKnowledge>(db),
                    business = Find<CompanyBusinessIdentity>(db, BusinessIdentityId),
                    businessProfiles = All<CompanyBusinessProfile>(db),
                    businessDrafts = All<CompanyBusinessDraft>(db)
                        .Select(d => new
                        {
                            d.Id,
                            d.BusinessVersion,
                            d.SourceIds,
                            d.Job,
                            d.State,
                            d.Version,
                            d.UpdatedAt,
                            d.ProfileId,
                            d.Error,
                            d.Usage,
                            d.ReportedUsage,
                            d.ProviderReceipt,
                            d.LocalRefusalReceipt,
                            d.RejectedResponse,
                        }),
                    businessLookups = All<CompanyBusinessLookup>(db),
                    businessSources = All<CompanyBusinessSource>(db)
                        .Select(s => new
                        {
                            s.Id,
                            s.BusinessVersion,
                            s.Url,
                            s.Title,
                            s.Digest,
                            s.RetrievedAt,
                            s.Clipped,
                        }),
                    threads = All<CompanyThread>(db),
                    tasks = All<CompanyTask>(db),
                    sandboxTaskIds = Rows(
                        db,
                        "SELECT DISTINCT parent_id FROM co_sandbox_jobs WHERE company_id=$0 AND parent_id IS NOT NULL",
                        r => r.GetString(0),
                        CompanyId
                    ),
                    browserTaskIds = Rows(
                        db,
                        "SELECT DISTINCT parent_id FROM co_browser_jobs WHERE company_id=$0 AND parent_id IS NOT NULL",
                        r => r.GetString(0),
                        CompanyId
                    ),
                    browserRunningTaskIds = Rows(
                        db,
                        "SELECT DISTINCT parent_id FROM co_browser_jobs WHERE company_id=$0 AND state='running' AND parent_id IS NOT NULL",
                        r => r.GetString(0),
                        CompanyId
                    ),
                    artifacts = All<CompanyArtifact>(db),
                    plans = All<CompanyPlan>(db),
                    meetings = All<CompanyMeeting>(db),
                    handoffs = All<CompanyHandoff>(db),
                    budgets = All<CompanyBudget>(db),
                    // What each budget is for, by id (owner walkthrough, 2026-10-19).
                    budgetLabels = BudgetLabels(db),
                    spend = Spend(
                        All<CompanyAttempt>(db),
                        ModelRoutes.Price,
                        All<CompanyBusinessDraft>(db),
                        All<CompanyUsageEntry>(db)
                    ),
                    executions = All<CompanyExecutionGrant>(db),
                    measured = All<CompanyAttempt>(db)
                        .Where(a => a.Measured is not null)
                        .Select(a => new
                        {
                            a.Id,
                            a.TaskId,
                            a.Version,
                            a.Phase,
                            a.Error,
                            a.Usage,
                            a.ReportedUsage,
                            job = a.Measured,
                        })
                        .ToArray(),
                    roles = _roles.Select(r => new
                    {
                        r.Id,
                        r.Name,
                        r.UnavailableReason,
                        r.Skills,
                        r.OnboardingFields,
                        r.MeasuredSkills,
                        r.ExecutionSkills,
                        r.PublishingSkills,
                        r.ShiftSkills,
                        skillNames = r.Skills.ToDictionary(s => s, r.WorkName),
                        summary = r.Manifest?.Summary,
                        onboarding = r.Manifest is { Onboarding: var o }
                            ? new
                            {
                                department = o.Department,
                                name = o.DefaultName,
                                responsibility = o.Responsibility,
                                help = o.Fields.ToDictionary(f => f.Id, f => f.Help),
                                optional = o.Fields.Where(f => f.Optional).Select(f => f.Id),
                                boundaries = o.Boundaries,
                            }
                            : null,
                        tools = r.Manifest?.Tools.Select(t => new
                        {
                            t.Id,
                            risk = t.Risk.ToString().ToLowerInvariant(),
                            t.Purpose,
                        }),
                        kpis = r.Manifest?.Kpis,
                        uiSlots = r.Manifest?.UiSlots ?? [],
                        actions = r.Manifest?.Actions ?? [],
                    }),
                    taskTimes = TaskTimes(db),
                    // Connect cards, open and closed (docs/adr/0046-connect-cards.md).
                    connections = All<CompanyConnectionRequest>(db),
                    // Weekly scorecard numbers (docs/adr/0170-goal-ladder-and-weekly-numbers.md).
                    readings = RecentReadings(db),
                    // The company-wide fact fields and their help, in order (real-use batch, 2026-10-20).
                    companyFields = CompanyFactFields.Select(f => new { id = f.Id, help = f.Help }),
                    cursor = long.Parse(
                        Scalar(db, "SELECT COALESCE(MAX(seq),0) FROM co_events WHERE company_id=$0", CompanyId)!
                    ),
                }
        );
    }

    public CompanyEvent[] Feed(long after, int limit = 100)
    {
        using var db = Open();
        return Rows(
            db,
            "SELECT seq,payload FROM co_events WHERE company_id=$0 AND seq>$1 ORDER BY seq LIMIT $2",
            r => CompanyJson.Decode<CompanyEvent>(r.GetString(1)) with { Sequence = r.GetInt64(0) },
            CompanyId,
            Math.Max(0, after),
            Math.Clamp(limit, 1, 100)
        );
    }

    public object Thread(string employee, string thread)
    {
        using var db = Open();
        Need<CompanyEmployee>(db, employee);
        var t = Need<CompanyThread>(db, thread);
        if (t.EmployeeId != employee)
            throw new WorkspaceException("Thread not found.", 404);
        return new
        {
            thread = t,
            messages = All<CompanyMessage>(db).Where(m => m.ThreadId == thread).ToArray(),
            notes = t.Scope == "company"
                ? new CompanyNotes(employee, "", 0)
                : Find<CompanyNotes>(db, employee) ?? new(employee, "", 0),
        };
    }

    public object TaskDetail(string id)
    {
        using var db = Open();
        var attempts = All<CompanyAttempt>(db).Where(a => a.TaskId == id).ToArray();
        return new
        {
            task = Need<CompanyTask>(db, id),
            attempts,
            handoffs = All<CompanyHandoff>(db).Where(h => h.TargetTaskId == id).ToArray(),
            executions = All<CompanyExecutionGrant>(db).Where(g => g.TaskId == id).ToArray(),
            publications = All<CompanyPublication>(db).Where(p => p.TaskId == id).ToArray(),
            sandboxes = All<CompanySandboxJob>(db).Where(j => j.TaskId == id).ToArray(),
            browsers = All<CompanyBrowserJob>(db).Where(j => j.TaskId == id).ToArray(),
            contexts = All<CompanyContext>(db)
                .Where(c => attempts.Any(a => a.ContextId == c.Id))
                .Select(c => new
                {
                    c.Id,
                    digest = CompanyJson.Digest(c),
                    c.EmployeeId,
                    c.RoleId,
                    historyCount = c.History.Length,
                    evidence = c.Evidence.Select(e => new { e.Id, e.Digest }),
                })
                .ToArray(),
        };
    }

    public string? Operation(string id)
    {
        using var db = Open();
        return Scalar(db, "SELECT result FROM co_operations WHERE company_id=$0 AND id=$1", CompanyId, id);
    }

    public string Apply(CompanyCommand command) => Apply(command, out _);

    // The company's first owner, acting directly (local owner mode and internal callers).
    public string Apply(CompanyCommand command, out bool newlyApplied) => Apply(command, _owner, out newlyApplied);

    // A member acting: their role comes from their membership, never from the request (docs/adr/0020).
    public string Apply(CompanyCommand command, string subject, out bool newlyApplied)
    {
        var actor = Actor(subject);
        CompanyAccess.Demand(actor, CompanyAccess.Required(command.Action ?? ""), "run " + command.Action);
        var previous = CurrentActor.Value;
        CurrentActor.Value = actor;
        try
        {
            return ApplyAs(command, out newlyApplied);
        }
        finally
        {
            CurrentActor.Value = previous;
        }
    }

    private string ApplyAs(CompanyCommand command, out bool newlyApplied)
    {
        if (!Guid.TryParseExact(command.RequestId, "D", out _))
            throw new WorkspaceException("A UUID operation ID is required.", 422);
        if (
            command.Evidence?.Any(string.IsNullOrWhiteSpace) == true
            || command.Participants?.Any(string.IsNullOrWhiteSpace) == true
            || command.Tasks?.Any(s =>
                s is null
                || s.Evidence is null
                || s.DependsOn is null
                || string.IsNullOrWhiteSpace(s.EmployeeId)
                || string.IsNullOrWhiteSpace(s.ThreadId)
                || string.IsNullOrWhiteSpace(s.Skill)
                || string.IsNullOrWhiteSpace(s.Input)
            ) == true
        )
            throw new WorkspaceException("Task and evidence fields must be complete.", 422);
        var fresh = false;
        using var db = Open();
        var receipt = Transaction(
            db,
            () =>
            {
                var fingerprint = CompanyJson.Digest(command);
                var prior = Rows(
                        db,
                        "SELECT digest,result FROM co_operations WHERE company_id=$0 AND id=$1",
                        r => (r.GetString(0), r.GetString(1)),
                        CompanyId,
                        command.RequestId
                    )
                    .SingleOrDefault();
                if (prior != default)
                {
                    if (prior.Item1 != fingerprint)
                        throw new WorkspaceException("Operation ID was already used for different work.");
                    return prior.Item2;
                }
                var result = CompanyJson.Encode(Apply(db, command));
                Sql(
                    db,
                    "INSERT INTO co_operations VALUES($0,$1,$2,$3)",
                    CompanyId,
                    command.RequestId,
                    fingerprint,
                    result
                );
                fresh = true;
                return result;
            }
        );
        newlyApplied = fresh;
        return receipt;
    }
}
