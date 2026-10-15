using Microsoft.Data.Sqlite;

namespace HireZero.Core;

// All company mutations, their operation receipt and feed events commit together. This module does not migrate legacy tables.
public sealed partial class CompanyStore
{
    private readonly string _path;
    private readonly CompanyRole[] _roles;
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
    public const string CompanyId = "local-company";
    public CompanyRole[] Roles => _roles;
    public CompanyModelRouting ModelRouting {get;}
    public string? MeasuredRuntimeIdentity {get;}
    public SemaphoreSlim ProviderGate {get;}=new(1,1);
    private static readonly Dictionary<Type, string> Tables = new()
    {
        [typeof(CompanyDepartment)]="co_departments", [typeof(CompanyEmployee)]="co_employees", [typeof(CompanyKnowledge)]="co_knowledge",
        [typeof(CompanyThread)]="co_threads", [typeof(CompanyMessage)]="co_messages", [typeof(CompanyNotes)]="co_notes",
        [typeof(CompanyTask)]="co_tasks", [typeof(CompanyAttempt)]="co_attempts", [typeof(CompanyContext)]="co_contexts",
        [typeof(CompanyArtifact)]="co_artifacts", [typeof(CompanyHandoff)]="co_handoffs", [typeof(CompanyBudget)]="co_budgets",
        [typeof(CompanyReservation)]="co_reservations", [typeof(CompanyResourceClaim)]="co_resource_claims",
        [typeof(CompanyPlan)]="co_plans", [typeof(CompanyMeeting)]="co_meetings",[typeof(CompanyExecutionGrant)]="co_execution_grants"
    };
    private static readonly Dictionary<string, string> ParentTables = new()
    {
        ["co_employees"]="co_departments", ["co_knowledge"]="co_departments", ["co_messages"]="co_threads",
        ["co_tasks"]="co_threads", ["co_attempts"]="co_tasks", ["co_contexts"]="co_attempts", ["co_artifacts"]="co_tasks",
        ["co_handoffs"]="co_artifacts", ["co_reservations"]="co_budgets"
    };
    public CompanyStore(string path, string owner, CompanyRole[] roles,CompanyModelRouting? routing=null,string? measuredRuntimeIdentity=null)
    {
        _path = path; _roles = roles;ModelRouting=routing??new();ModelRouting.Validate();MeasuredRuntimeIdentity=measuredRuntimeIdentity;
        if (roles.Select(r=>r.Id).Distinct().Count()!=roles.Length) throw new InvalidOperationException("Duplicate role definition.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var db = Open();
        Transaction(db, () =>
        {
            Sql(db,"CREATE TABLE IF NOT EXISTS company_schema_migrations(version INTEGER PRIMARY KEY, checksum TEXT NOT NULL)");
            var migrations=Rows(db,"SELECT version,checksum FROM company_schema_migrations",r=>(r.GetInt32(0),r.GetString(1)));
            string[] schemas=[Schema(),"CREATE TABLE co_execution_grants(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,parent_id) REFERENCES co_tasks(company_id,id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));"];
            if(migrations.Any(m=>m.Item1<1||m.Item1>schemas.Length||m.Item2!=EmployeeStore.Hash(schemas[m.Item1-1]))||migrations.OrderBy(m=>m.Item1).Where((m,i)=>m.Item1!=i+1).Any())throw new WorkspaceException("Company schema is newer or its migration checksum changed. No work started.");
            for(var i=migrations.Length;i<schemas.Length;i++){Sql(db,schemas[i]);Sql(db,"INSERT INTO company_schema_migrations VALUES($0,$1)",i+1,EmployeeStore.Hash(schemas[i]));}
            Sql(db,"INSERT OR IGNORE INTO co_company VALUES($0,$1)",CompanyId,owner);
            if(Scalar(db,"SELECT owner FROM co_company WHERE id=$0",CompanyId)!=owner) throw new WorkspaceException("This company belongs to another owner.",403);
            return true;
        });
    }
    private static string Schema()
    {
        var statements=new List<string>{"CREATE TABLE IF NOT EXISTS co_company(id TEXT PRIMARY KEY, owner TEXT NOT NULL)"};
        foreach(var table in Tables.Values.Where(table=>table!="co_execution_grants"))
        {
            var parent=ParentTables.TryGetValue(table,out var target)?$",FOREIGN KEY(company_id,parent_id) REFERENCES {target}(company_id,id)":"";
            var employee=table is "co_threads" or "co_messages" or "co_tasks" or "co_attempts" or "co_artifacts" or "co_handoffs" or "co_notes" or "co_contexts"?",FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id)":"";
            statements.Add($"CREATE TABLE IF NOT EXISTS {table}(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id){parent}{employee})");
        }
        statements.Add("CREATE UNIQUE INDEX IF NOT EXISTS co_one_active_attempt ON co_attempts(company_id,employee_id) WHERE state IN ('claimed','reserved','dispatching')");
        statements.Add("CREATE UNIQUE INDEX IF NOT EXISTS co_thread_sequence ON co_messages(company_id,parent_id,json_extract(payload,'$.sequence'))");
        statements.Add("CREATE TABLE IF NOT EXISTS co_operations(company_id TEXT NOT NULL,id TEXT NOT NULL,digest TEXT NOT NULL,result TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id))");
        statements.Add("CREATE TABLE IF NOT EXISTS co_events(seq INTEGER PRIMARY KEY AUTOINCREMENT,company_id TEXT NOT NULL,payload TEXT NOT NULL,FOREIGN KEY(company_id) REFERENCES co_company(id))");
        return string.Join(";\n",statements)+";";
    }
    private SqliteConnection Open()
    {
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=_path,Pooling=false,ForeignKeys=true,DefaultTimeout=5}.ToString()); db.Open(); return db;
    }
    private static SqliteCommand Command(SqliteConnection db,string sql,params object?[] values)
    {
        var command=db.CreateCommand(); command.CommandText=sql;
        for(var i=0;i<values.Length;i++)command.Parameters.AddWithValue("$"+i,values[i]??DBNull.Value);return command;
    }
    private static void Sql(SqliteConnection db,string sql,params object?[] values){using var c=Command(db,sql,values);c.ExecuteNonQuery();}
    private static string? Scalar(SqliteConnection db,string sql,params object?[] values){using var c=Command(db,sql,values);return c.ExecuteScalar()?.ToString();}
    private static T[] Rows<T>(SqliteConnection db,string sql,Func<SqliteDataReader,T> read,params object?[] values)
    {using var c=Command(db,sql,values);using var r=c.ExecuteReader();var rows=new List<T>();while(r.Read())rows.Add(read(r));return [..rows];}
    private static T Transaction<T>(SqliteConnection db,Func<T> action)
    {Sql(db,"BEGIN IMMEDIATE");try{var result=action();Sql(db,"COMMIT");return result;}catch{Sql(db,"ROLLBACK");throw;}}
    private static T[] All<T>(SqliteConnection db) => Rows(db,$"SELECT payload FROM {Tables[typeof(T)]} WHERE company_id=$0 ORDER BY rowid",r=>CompanyJson.Decode<T>(r.GetString(0)),CompanyId);
    private static T? Find<T>(SqliteConnection db,string? id) where T:class => id is null?null:Rows(db,$"SELECT payload FROM {Tables[typeof(T)]} WHERE company_id=$0 AND id=$1",r=>CompanyJson.Decode<T>(r.GetString(0)),CompanyId,id).SingleOrDefault();
    private static T Need<T>(SqliteConnection db,string? id) where T:class => Find<T>(db,id)??throw new WorkspaceException("Record not found in this company.",404);
    private static void Save<T>(SqliteConnection db,T value,string id,string? parent=null,string? employee=null,string state="saved",int version=1)
    {Sql(db,$"INSERT INTO {Tables[typeof(T)]} VALUES($0,$1,$2,$3,$4,$5,$6) ON CONFLICT(company_id,id) DO UPDATE SET parent_id=excluded.parent_id,employee_id=excluded.employee_id,state=excluded.state,revision=excluded.revision,payload=excluded.payload",id,CompanyId,parent,employee,state,version,CompanyJson.Encode(value));}
    private void Event(SqliteConnection db,string type,string id,string? employee,string text)
    {Sql(db,"INSERT INTO co_events(company_id,payload) VALUES($0,$1)",CompanyId,CompanyJson.Encode(new CompanyEvent(0,type,id,employee,text,Clock())));}
    private static string Id()=>Guid.NewGuid().ToString("N");
    private static string Text(string? text,int max=4000)=>EmployeeStore.Text(text,max);
    private static void Version(int actual,int expected){if(actual!=expected)throw new WorkspaceException("This record changed. Refresh before applying the command.");}
    private CompanyRole Role(string? id)=>_roles.SingleOrDefault(r=>r.Id==id)??throw new WorkspaceException("Unknown role definition.",422);
    private static void Put(SqliteConnection db,CompanyEmployee e)=>Save(db,e,e.Id,e.DepartmentId,state:e.State,version:e.Version);
    private static void Put(SqliteConnection db,CompanyTask t)=>Save(db,t,t.Id,t.ThreadId,t.EmployeeId,t.State,t.Version);
    private static void Put(SqliteConnection db,CompanyAttempt a)=>Save(db,a,a.Id,a.TaskId,a.EmployeeId,a.Phase,a.Version);
    private static void Put(SqliteConnection db,CompanyMeeting m)=>Save(db,m,m.Id,state:m.State,version:m.Version);
    private static void Put(SqliteConnection db,CompanyPlan p)=>Save(db,p,p.Id,state:p.State,version:p.Version);
    public object Dashboard()
    {
        using var db=Open(); return Transaction(db,()=>new{departments=All<CompanyDepartment>(db),employees=All<CompanyEmployee>(db),knowledge=All<CompanyKnowledge>(db),
            threads=All<CompanyThread>(db),tasks=All<CompanyTask>(db),artifacts=All<CompanyArtifact>(db),plans=All<CompanyPlan>(db),meetings=All<CompanyMeeting>(db),handoffs=All<CompanyHandoff>(db),budgets=All<CompanyBudget>(db),executions=All<CompanyExecutionGrant>(db),
            measured=All<CompanyAttempt>(db).Where(a=>a.Measured is not null).Select(a=>new{a.Id,a.TaskId,a.Version,a.Phase,a.Error,a.Usage,a.ReportedUsage,job=a.Measured}).ToArray(),
            roles=_roles.Select(r=>new{r.Id,r.Name,r.Skills,r.OnboardingFields,r.MeasuredSkills,r.ExecutionSkills}),cursor=long.Parse(Scalar(db,"SELECT COALESCE(MAX(seq),0) FROM co_events WHERE company_id=$0",CompanyId)!)});
    }
    public CompanyEvent[] Feed(long after,int limit=100)
    {using var db=Open();return Rows(db,"SELECT seq,payload FROM co_events WHERE company_id=$0 AND seq>$1 ORDER BY seq LIMIT $2",r=>CompanyJson.Decode<CompanyEvent>(r.GetString(1)) with{Sequence=r.GetInt64(0)},CompanyId,Math.Max(0,after),Math.Clamp(limit,1,100));}
    public object Thread(string employee,string thread)
    {using var db=Open();Need<CompanyEmployee>(db,employee);var t=Need<CompanyThread>(db,thread);if(t.EmployeeId!=employee)throw new WorkspaceException("Thread not found.",404);return new{thread=t,messages=All<CompanyMessage>(db).Where(m=>m.ThreadId==thread).ToArray(),notes=Find<CompanyNotes>(db,employee)??new(employee,"")};}
    public object TaskDetail(string id){using var db=Open();var attempts=All<CompanyAttempt>(db).Where(a=>a.TaskId==id).ToArray();return new{task=Need<CompanyTask>(db,id),attempts,handoffs=All<CompanyHandoff>(db).Where(h=>h.TargetTaskId==id).ToArray(),executions=All<CompanyExecutionGrant>(db).Where(g=>g.TaskId==id).ToArray(),contexts=All<CompanyContext>(db).Where(c=>attempts.Any(a=>a.ContextId==c.Id)).Select(c=>new{c.Id,digest=CompanyJson.Digest(c),c.EmployeeId,c.RoleId,historyCount=c.History.Length,evidence=c.Evidence.Select(e=>new{e.Id,e.Digest})}).ToArray()};}
    public string? Operation(string id){using var db=Open();return Scalar(db,"SELECT result FROM co_operations WHERE company_id=$0 AND id=$1",CompanyId,id);}
    public string Apply(CompanyCommand command)=>Apply(command,out _);
    public string Apply(CompanyCommand command,out bool newlyApplied)
    {
        if(!Guid.TryParseExact(command.RequestId,"D",out _))throw new WorkspaceException("A UUID operation ID is required.",422);
        if(command.Evidence?.Any(string.IsNullOrWhiteSpace)==true||command.Participants?.Any(string.IsNullOrWhiteSpace)==true||command.Tasks?.Any(s=>s is null||s.Evidence is null||s.DependsOn is null||string.IsNullOrWhiteSpace(s.EmployeeId)||string.IsNullOrWhiteSpace(s.ThreadId)||string.IsNullOrWhiteSpace(s.Skill)||string.IsNullOrWhiteSpace(s.Input))==true)
            throw new WorkspaceException("Task and evidence fields must be complete.",422);
        var fresh=false;using var db=Open();var receipt=Transaction(db,()=>
        {
            var fingerprint=CompanyJson.Digest(command);var prior=Rows(db,"SELECT digest,result FROM co_operations WHERE company_id=$0 AND id=$1",r=>(r.GetString(0),r.GetString(1)),CompanyId,command.RequestId).SingleOrDefault();
            if(prior!=default){if(prior.Item1!=fingerprint)throw new WorkspaceException("Operation ID was already used for different work.");return prior.Item2;}
            var result=CompanyJson.Encode(Apply(db,command));
            Sql(db,"INSERT INTO co_operations VALUES($0,$1,$2,$3)",CompanyId,command.RequestId,fingerprint,result);fresh=true;return result;
        });
        newlyApplied=fresh;return receipt;
    }
}
