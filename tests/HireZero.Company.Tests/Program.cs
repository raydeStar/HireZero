using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

if(args.FirstOrDefault() is "--codex-inspect" or "--codex-pilot")
{try{await CompanyCodexChecks.InspectInstalledAsync(args);}catch(Exception e) when(e is WorkspaceException or CompanyTransportException){Console.WriteLine("BLOCKED: "+e.Message);Environment.ExitCode=2;}return;}
var count=0;
void Check(bool condition,string text){count++;if(!condition)throw new Exception(text);}
bool Refused(Action action){try{action();return false;}catch(WorkspaceException){return true;}}
var root=Path.Combine(Path.GetTempPath(),"company-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    var path=Path.Combine(root,"company.sqlite");var store=new CompanyStore(path,"owner@example.invalid",CompanyWorkspace.Definitions());
    T Apply<T>(CompanyCommand c)=>CompanyJson.Decode<T>(store.Apply(c));
    CompanyCommand Cmd(string action)=>new(Guid.NewGuid().ToString("D"),action);
    JsonElement State()=>JsonSerializer.SerializeToElement(store.Dashboard(),CompanyJson.Options);
    T[] List<T>(string key)=>CompanyJson.Decode<T[]>(State().GetProperty(key).GetRawText());
    CompanyTask TaskOf(string id)=>List<CompanyTask>("tasks").Single(t=>t.Id==id);
    CompanyEmployee Employee(string id)=>List<CompanyEmployee>("employees").Single(e=>e.Id==id);
    CompanyDepartment Department(string name)=>Apply<CompanyDepartment>(Cmd("department.create") with{Name=name});
    CompanyEmployee Hire(CompanyDepartment d,string role,string name)=>Apply<CompanyEmployee>(Cmd("employee.hire") with{DepartmentId=d.Id,RoleId=role,Name=name,Responsibility="Fictional "+name+" responsibility"});
    CompanyKnowledge Onboard(CompanyDepartment d,CompanyEmployee guide)
    {
        var fields=CompanyWorkspace.Definitions().Single(r=>r.Id==guide.RoleId).OnboardingFields.ToDictionary(f=>f,f=>"Approved fictional "+f);
        var k=Apply<CompanyKnowledge>(Cmd("knowledge.save") with{DepartmentId=d.Id,Body=CompanyJson.Encode(fields)});
        return Apply<CompanyKnowledge>(Cmd("knowledge.review") with{Id=k.Id,ExpectedVersion=k.Version,Digest=k.Digest,Decision="approve"});
    }
    CompanyThread Thread(CompanyEmployee e)=>Apply<CompanyThread>(Cmd("thread.create") with{EmployeeId=e.Id,Name="Private fixture thread"});
    CompanyTask Submit(CompanyEmployee e,CompanyThread t,string body="Fictional request",string[]? evidence=null)=>Apply<CompanyTask>(Cmd("task.create") with{EmployeeId=e.Id,ThreadId=t.Id,Skill="chat",Body=body,Evidence=evidence});
    var marketing=Department("Fictional Marketing");var engineering=Department("Fictional Engineering");var leadership=Department("Fictional Leadership");
    var m1=Hire(marketing,"marketing","Strategy");var m2=Hire(marketing,"marketing","Copy");var d1=Hire(engineering,"developer","Backend");var d2=Hire(engineering,"developer","Frontend");var ceo=Hire(leadership,"ceo","Facilitator");
    Check(new[]{m1.Id,m2.Id,d1.Id,d2.Id,ceo.Id}.Distinct().Count()==5&&m1.Id!=m1.RoleId,"Employee IDs must be distinct from role IDs.");
    Check(List<CompanyDepartment>("departments").Single(d=>d.Id==marketing.Id).Guide==m1.Id,"Only first hire guides onboarding.");
    Check(m2.State=="onboarding","Second hire waits for approval.");var mk=Onboard(marketing,m1);Onboard(engineering,d1);Onboard(leadership,ceo);
    Check(Employee(m1.Id).State=="active"&&Employee(m2.Id).State=="active","Approved knowledge activates all waiting hires.");
    var tm1=Thread(m1);var tm2=Thread(m2);var td1=Thread(d1);var td2=Thread(d2);var tc=Thread(ceo);
    var notes=Cmd("notes.save") with{EmployeeId=m1.Id,Body="PRIVATE_STRATEGY_SENTINEL",ExpectedVersion=0};store.Apply(notes);
    Check(Refused(()=>store.Thread(m2.Id,tm1.Id)),"Cross-employee thread read denied.");
    Check(Refused(()=>store.Apply(notes with{RequestId=Guid.NewGuid().ToString("D"),Body="Stale edit"})),"Stale note revision denied.");
    var operation=Cmd("thread.create") with{EmployeeId=m1.Id,Name="Idempotent thread"};var first=store.Apply(operation);Check(first==store.Apply(operation),"Replay returns identical receipt.");
    Check(Refused(()=>store.Apply(operation with{Name="Changed payload"})),"Changed operation payload denied.");
    var eventsBefore=store.Feed(0).Length;
    Check(Refused(()=>store.Apply(Cmd("task.create") with{EmployeeId=m2.Id,ThreadId=tm1.Id,Skill="chat",Body="wrong thread"})),"Cross-employee task creation denied.");
    Check(store.Feed(0).Length==eventsBefore,"Failed transaction emits no feed event.");
    var model=new FakeModel();var scheduler=new CompanyScheduler(store,model);
    var t1=Submit(m1,tm1,"First employee request");var t2=Submit(m2,tm2,"Second employee request");
    Check(t1.ModelRoute=="openai/gpt-6-luna"&&t1.Runtime=="codex","Task records the selected Codex runtime and Luna conversation route.");
    await Task.WhenAll(scheduler.TickAsync(m1.Id),scheduler.TickAsync(m2.Id));
    Check(model.MaxActive==1,"Different employees share one serialized account gate.");
    Check(model.Calls.Count==2,"Independent queues dispatch exactly once.");
    var calls=model.Calls.ToArray();Check(calls.Single(c=>c.Context.EmployeeId==m1.Id).Context.Notes.Contains("PRIVATE_STRATEGY_SENTINEL"),"Own notes included.");
    Check(!CompanyJson.Encode(calls.Single(c=>c.Context.EmployeeId==m2.Id)).Contains("PRIVATE_STRATEGY_SENTINEL"),"Sibling notes excluded.");
    Check(TaskOf(t1.Id).State=="awaiting_review","Generated content needs human review.");
    var a=List<CompanyArtifact>("artifacts").Single(a=>a.TaskId==t1.Id);
    Check(Refused(()=>store.Apply(Cmd("artifact.review") with{Id=a.Id,ExpectedVersion=a.Version,Digest="stale",Decision="approve"})),"Wrong artifact digest denied.");
    a=Apply<CompanyArtifact>(Cmd("artifact.review") with{Id=a.Id,ExpectedVersion=a.Version,Digest=a.Digest,Decision="approve"});
    Apply<CompanyHandoff>(Cmd("handoff.create") with{Id=a.Id,ExpectedVersion=a.Version,Digest=a.Digest,EmployeeId=m2.Id,ThreadId=tm2.Id});
    var next=Submit(m2,tm2,"Use selected handoff");await scheduler.TickAsync(m2.Id);
    Check(model.Calls.Last().Context.Evidence.Any(e=>e.Id==a.Id),"Exact targeted handoff enters recipient context.");
    var other=Thread(m2);Submit(m2,other,"Unrelated conversation");await scheduler.TickAsync(m2.Id);Check(!model.Calls.Last().Context.Evidence.Any(e=>e.Id==a.Id),"Handoff excluded from unrelated recipient thread.");
    var hold=new TaskCompletionSource<CompanyModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);model.Hold=hold;
    var pending=Submit(d1,td1,"Cancelable output");var running=scheduler.TickAsync(d1.Id);await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(pending.ModelRoute=="openai/gpt-6-luna","Selecting a Developer for conversation still selects Luna.");
    pending=TaskOf(pending.Id);store.Apply(Cmd("task.cancel") with{Id=pending.Id,ExpectedVersion=pending.Version});hold.SetResult(new("LATE_RESPONSE_SENTINEL",10));await running;model.Hold=null;
    Check(TaskOf(pending.Id).State=="canceled"&&!List<CompanyArtifact>("artifacts").Any(a=>a.TaskId==pending.Id),"Canceled late response cannot publish an artifact.");
    var lock1=Submit(d1,td1,"Workspace lease one");var lock2=Submit(d2,td2,"Workspace lease two");
    var workspace=Path.Combine(root,"checkout");Directory.CreateDirectory(Path.Combine(workspace,".git"));Directory.CreateDirectory(Path.Combine(workspace,"subdir"));
    store.Apply(Cmd("workspace.grant") with{Id=lock1.Id,ExpectedVersion=lock1.Version,Body=workspace});store.Apply(Cmd("workspace.grant") with{Id=lock2.Id,ExpectedVersion=lock2.Version,Body=workspace});var attempt1=store.Claim(d1.Id,true)!;var attempt2=store.Claim(d2.Id,true)!;var claim=store.AcquireWorkspace(attempt1.Id,workspace);Check(Refused(()=>store.AcquireWorkspace(attempt2.Id,Path.Combine(workspace,"subdir"))),"Different employees cannot write one checkout through nested path aliases.");
    store.FailAttempt(attempt1.Id,"not-sent-fixture",false);Check(store.AcquireWorkspace(attempt2.Id,workspace).AttemptId==attempt2.Id,"Known not-sent attempt releases workspace claim.");store.FailAttempt(attempt2.Id,"not-sent-fixture",false);
    var restart=Submit(d1,td1,"Interrupted provider");var interrupted=store.Claim(d1.Id,true)!;store.Capture(interrupted.Id);Check(store.AuthorizeDispatch(interrupted.Id),"Reserve before dispatch.");
    store.Recover();Check(TaskOf(restart.Id).State=="unknown","Restart after dispatch marks unknown.");Check(store.Claim(d1.Id,true) is null,"Unknown attempt is not replayed.");
    Check(List<CompanyBudget>("budgets").Single(b=>b.Id==restart.RootId).Reserved==5000,"Unknown usage retains reservation.");
    var missing=Submit(d2,td2,"Missing usage");model.Usage=null;await scheduler.TickAsync(d2.Id);model.Usage=100;
    Check(TaskOf(missing.Id).State=="unknown"&&List<CompanyBudget>("budgets").Single(b=>b.Id==missing.RootId).Reserved==5000,"Missing usage never counts as zero.");
    var unavailable=Submit(m1,tm1,"Unavailable provider");await new CompanyScheduler(store,new UnavailableCompanyModel()).TickAsync(m1.Id);Check(TaskOf(unavailable.Id).Reason=="provider-unavailable","Unavailable provider saves blocked work without dispatch.");
    store.Apply(Cmd("task.cancel") with{Id=unavailable.Id,ExpectedVersion=TaskOf(unavailable.Id).Version});
    var specs=new[]{new CompanyTaskSpec(d1.Id,td1.Id,"developer-advice","Produce fictional feature facts",[],[]),new CompanyTaskSpec(m2.Id,tm2.Id,"chat","Draft from selected facts",[],[0])};
    var plan=Apply<CompanyPlan>(Cmd("plan.create") with{EmployeeId=ceo.Id,Body="Fictional launch",Tasks=specs,Budget=10000});
    Check(Refused(()=>store.Apply(Cmd("plan.approve") with{Id=plan.Id,ExpectedVersion=plan.Version,Digest="wrong"})),"Stale plan approval denied.");
    plan=Apply<CompanyPlan>(Cmd("plan.approve") with{Id=plan.Id,ExpectedVersion=plan.Version,Digest=plan.Digest});Check(plan.TaskIds.Length==2,"Exact plan creates bounded children.");
    await scheduler.TickAsync(m2.Id);Check(TaskOf(plan.TaskIds[1]).Reason=="dependencies","Dependent task waits for owner-reviewed predecessor.");
    Check(Refused(()=>store.Apply(Cmd("plan.create") with{EmployeeId=ceo.Id,Body="Loop",Budget=5000,Tasks=[new(ceo.Id,tc.Id,"company-plan","Delegate again",[],[])]})),"Recursive self-delegation denied.");
    store.Apply(Cmd("plan.cancel") with{Id=plan.Id,ExpectedVersion=plan.Version});Check(plan.TaskIds.All(id=>TaskOf(id).State=="canceled"),"Root cancellation reaches pending children.");
    var ceoWork=Apply<CompanyTask>(Cmd("task.create") with{EmployeeId=ceo.Id,ThreadId=tc.Id,Skill="company-plan",Body="Propose a bounded four-person launch plan."});
    model.ReplyBody=CompanyJson.Encode(new CompanyPlanProposal("Four-person fictional launch",[new(d1.Id,td1.Id,"chat","Prepare backend facts",[],[]),new(d2.Id,td2.Id,"chat","Prepare frontend facts",[],[]),new(m1.Id,tm1.Id,"chat","Prepare strategy",[],[]),new(m2.Id,tm2.Id,"chat","Prepare copy",[],[])],20000));
    await scheduler.TickAsync(ceo.Id);model.ReplyBody=null;var proposalArtifact=List<CompanyArtifact>("artifacts").Single(a=>a.TaskId==ceoWork.Id);
    proposalArtifact=Apply<CompanyArtifact>(Cmd("artifact.review") with{Id=proposalArtifact.Id,ExpectedVersion=proposalArtifact.Version,Digest=proposalArtifact.Digest,Decision="approve"});
    var generatedPlan=Apply<CompanyPlan>(Cmd("plan.from-artifact") with{Id=proposalArtifact.Id,ExpectedVersion=proposalArtifact.Version,Digest=proposalArtifact.Digest});
    Check(generatedPlan.State=="proposed"&&generatedPlan.TaskIds.Length==0,"A reviewed model proposal still cannot dispatch before plan approval.");
    generatedPlan=Apply<CompanyPlan>(Cmd("plan.approve") with{Id=generatedPlan.Id,ExpectedVersion=generatedPlan.Version,Digest=generatedPlan.Digest});Check(generatedPlan.TaskIds.Length==4,"A fake facilitator proposal reaches four distinct employee queues through owner approval.");
    Check(!CompanyJson.Encode(model.Calls.Last()).Contains("PRIVATE_STRATEGY_SENTINEL"),"Company overview exposes metadata without employee private notes.");
    store.Apply(Cmd("plan.cancel") with{Id=generatedPlan.Id,ExpectedVersion=generatedPlan.Version});
    var meeting=Apply<CompanyMeeting>(Cmd("meeting.create") with{Name="Fictional board",Body="Review launch decisions",Participants=[m1.Id,d1.Id,ceo.Id],ChairId=ceo.Id,Evidence=[a.Id],Budget=30000});
    meeting=Apply<CompanyMeeting>(Cmd("meeting.start") with{Id=meeting.Id,ExpectedVersion=meeting.Version});
    for(var i=0;i<6;i++){var active=TaskOf(List<CompanyMeeting>("meetings").Single(m=>m.Id==meeting.Id).ActiveTask!);await scheduler.TickAsync(active.EmployeeId);}
    meeting=List<CompanyMeeting>("meetings").Single(m=>m.Id==meeting.Id);Check(meeting.Turns.Length==6&&meeting.State=="outcome_proposed","Board meeting stops at its finite turn limit, including synthesis.");
    Check(model.Calls.Where(c=>c.Context.MeetingId==meeting.Id).All(c=>c.Context.Notes==""&&c.Context.DepartmentKnowledge==""&&!CompanyJson.Encode(c).Contains("PRIVATE_STRATEGY_SENTINEL")),"Meeting contexts exclude private preparation and department data.");
    var childCount=List<CompanyTask>("tasks").Length;meeting=Apply<CompanyMeeting>(Cmd("meeting.outcome") with{Id=meeting.Id,ExpectedVersion=meeting.Version,Body="Decision: wait. Dissent: timing uncertain. Proposed action: draft a follow-up for separate approval."});
    meeting=Apply<CompanyMeeting>(Cmd("meeting.close") with{Id=meeting.Id,ExpectedVersion=meeting.Version,Digest=meeting.OutcomeDigest});Check(meeting.State=="closed"&&List<CompanyTask>("tasks").Length==childCount,"Accepted minutes do not grant or dispatch follow-up work.");
    var all=store.Feed(0,100);Check(all.Select(e=>e.Sequence).Distinct().Count()==all.Length&&!CompanyJson.Encode(all).Contains("PRIVATE_STRATEGY_SENTINEL"),"Feed is unique and excludes private notes.");
    var reopened=new CompanyStore(path,"owner@example.invalid",CompanyWorkspace.Definitions());Check(JsonSerializer.SerializeToElement(reopened.Dashboard(),CompanyJson.Options).GetProperty("employees").GetArrayLength()==5,"Instance identities persist across store restart.");
    Check(Refused(()=>new CompanyStore(path,"other@example.invalid",CompanyWorkspace.Definitions())),"Another owner cannot open the company.");
    using(var db=new SqliteConnection("Data Source="+path+";Pooling=False")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE company_schema_migrations SET checksum='modified'";q.ExecuteNonQuery();}
    Check(Refused(()=>new CompanyStore(path,"owner@example.invalid",CompanyWorkspace.Definitions())),"Changed migration checksum is refused.");
    await SecurityChecks.RunAsync(root,Check);
    await IntegrationPathChecks.RunAsync(root,Check);
    await CompanyCodexChecks.RunAsync(root,Check);
    await HttpChecks(root,Check);
    Console.WriteLine("PASS "+count+" company identity, isolation, concurrency, fencing, budget, CEO, meeting and HTTP checks");
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
finally
{
    var full=Path.GetFullPath(root);var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
    if(Path.GetDirectoryName(full)!=parent||!Path.GetFileName(full).StartsWith("company-checks-",StringComparison.Ordinal))throw new InvalidOperationException("Refused cleanup outside this test's temporary root.");
    // Git's own object files can be read-only on Windows. Change only this fixture's files before removal.
    foreach(var file in Directory.EnumerateFiles(full,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);
    Directory.Delete(full,true);
}

static async Task HttpChecks(string root,Action<bool,string> check)
{
    var previous=Environment.GetEnvironmentVariable(HeldWorkspace.Variable);Environment.SetEnvironmentVariable(HeldWorkspace.Variable,"0");
    try
    {
        var data=Path.Combine(root,"host");await using var app=CompanyWorkspace.TryCreate([CompanyWorkspace.Flag],"owner@example.invalid",true,data,fixtureUrl:"http://127.0.0.1:0")!;
        check(app.Services.GetService<IModelGateway>() is null&&app.Services.GetService<ICodingRunner>() is null&&app.Services.GetService<IHttpClientFactory>() is null,"Company-local profile registers no legacy/runtime clients or runners.");
        await app.StartAsync();var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri(address)};client.DefaultRequestHeaders.Host="127.0.0.1:5065";
        check((await client.GetAsync("/api/company")).StatusCode==HttpStatusCode.OK,"Company owner can load management state.");
        var payload=new CompanyCommand(Guid.NewGuid().ToString("D"),"department.create",Name:"Fictional HTTP department");
        check((await client.PostAsJsonAsync("/api/company/commands",payload)).StatusCode==HttpStatusCode.Forbidden,"Missing same-origin proof denied.");
        client.DefaultRequestHeaders.Add("Origin","http://127.0.0.1:5065");client.DefaultRequestHeaders.Add("X-Dev-Request","1");
        check((await client.PostAsJsonAsync("/api/company/commands",payload)).StatusCode==HttpStatusCode.OK,"Valid owner command accepted.");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host","attacker.invalid");check((await client.GetAsync("/api/company")).StatusCode==HttpStatusCode.Forbidden,"Spoofed forwarded host denied.");client.DefaultRequestHeaders.Remove("X-Forwarded-Host");
        var injection="{\"requestId\":\""+Guid.NewGuid()+"\",\"action\":\"department.create\",\"name\":\"Fake\",\"owner\":true}";
        check((await client.PostAsync("/api/company/commands",new StringContent(injection,System.Text.Encoding.UTF8,"application/json"))).StatusCode==HttpStatusCode.UnprocessableEntity,"Client-supplied owner authority field denied.");
        check((await client.PostAsJsonAsync("/api/github/workflows/fixture/publish",new{})).StatusCode==HttpStatusCode.ServiceUnavailable,"Legacy publication unavailable in company mode.");
        try{CompanyWorkspace.TryCreate([CompanyWorkspace.Flag],"owner@example.invalid",true,data,fixtureUrl:"http://127.0.0.1:0");check(false,"Second scheduler must be refused.");}catch(IOException){check(true,"Second scheduler cannot own the same data root.");}
        await app.StopAsync();
    }
    finally{Environment.SetEnvironmentVariable(HeldWorkspace.Variable,previous);}
}

sealed class FakeModel : ICompanyModel
{
    public bool Qualified=>true;public string Status=>"Deterministic synthetic provider; no network.";
    public ConcurrentQueue<CompanyModelCall> Calls {get;}=new();public int MaxActive;private int _active;public long? Usage=100;
    public TaskCompletionSource<CompanyModelReply>? Hold;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public string? ReplyBody;
    public int CountTokens(CompanyModelCall call)=>Math.Max(1,CompanyJson.Encode(call).Length/4);
    public async Task<CompanyModelReply> CompleteAsync(CompanyModelCall call,CancellationToken token)
    {var active=Interlocked.Increment(ref _active);MaxActive=Math.Max(MaxActive,active);Calls.Enqueue(call);try{if(Hold is not null){Entered.TrySetResult();return await Hold.Task.WaitAsync(token);}await Task.Yield();return new(ReplyBody??"Synthetic draft from "+call.Context.EmployeeId,Usage,"fictional-receipt");}finally{Interlocked.Decrement(ref _active);}}
}
