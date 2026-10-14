using System.Diagnostics;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.OpenClaw;
using HireZero.Integrations.Codex;
using HireZero.Roles.Developer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

static class IntegrationPathChecks
{
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server=builder.Build();var mode="ok";var received=new List<JsonElement>();var redirectHits=0;
        server.MapPost("/v1/chat/completions",async(HttpContext c)=>
        {
            received.Add(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            if(mode=="redirect"){c.Response.Redirect("/redirected");return;}
            if(mode=="error"){c.Response.StatusCode=503;return;}
            if(mode=="lost"){c.Abort();return;}
            await c.Response.WriteAsJsonAsync(new{id="fixture-receipt",model=mode=="route"?"wrong-route":"fixture-text",usage=mode=="usage"?null:new{prompt_tokens=10,completion_tokens=5,total_tokens=15},
                choices=new[]{new{finish_reason=mode=="tool"?"tool_calls":"stop",message=new{role="assistant",content="Fictional scoped response",tool_calls=mode=="tool"?new[]{new{id="forbidden-tool"}}:null}}}});
        });
        server.MapGet("/redirected",()=>{redirectHits++;return Results.Ok();});await server.StartAsync();
        var address=server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var adapter=new ScopedCompanyText(new Uri(address+"/v1/chat/completions"),"fixture-text",new Counter(),syntheticFixture:true);
        using var closed=new ScopedCompanyText(new Uri(address+"/v1/chat/completions"),"fixture-text",new Counter());
        var store=new CompanyStore(Path.Combine(root,"integration-paths.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions());
        CompanyCommand C(string a)=>new(Guid.NewGuid().ToString("D"),a);T A<T>(CompanyCommand c)=>CompanyJson.Decode<T>(store.Apply(c));
        T[] List<T>(string name)=>CompanyJson.Decode<T[]>(JsonSerializer.SerializeToElement(store.Dashboard(),CompanyJson.Options).GetProperty(name).GetRawText());
        var dep=A<CompanyDepartment>(C("department.create") with{Name="Integration fixture"});
        CompanyEmployee Hire(string name)=>A<CompanyEmployee>(C("employee.hire") with{DepartmentId=dep.Id,RoleId="developer",Name=name,Responsibility="Isolated integration fixture"});
        var first=Hire("First");var second=Hire("Second");var fields=CompanyWorkspace.Definitions().Single(r=>r.Id=="developer").OnboardingFields.ToDictionary(k=>k,k=>"Approved "+k);
        var knowledge=A<CompanyKnowledge>(C("knowledge.save") with{DepartmentId=dep.Id,Body=CompanyJson.Encode(fields)});store.Apply(C("knowledge.review") with{Id=knowledge.Id,ExpectedVersion=knowledge.Version,Digest=knowledge.Digest,Decision="approve"});
        var threads=List<CompanyThread>("threads");var t1=threads.Single(t=>t.EmployeeId==first.Id);var t2=threads.Single(t=>t.EmployeeId==second.Id);
        store.Apply(C("notes.save") with{EmployeeId=first.Id,Body="FIRST_EMPLOYEE_ONLY_SENTINEL"});
        CompanyTask Submit(CompanyEmployee e,CompanyThread thread,string skill="chat")=>A<CompanyTask>(C("task.create") with{EmployeeId=e.Id,ThreadId=thread.Id,Skill=skill,Body="Fictional scoped integration request"});
        var scheduler=new CompanyScheduler(store,adapter);var task=Submit(first,t1);await scheduler.TickAsync(first.Id);Submit(second,t2);await scheduler.TickAsync(second.Id);
        check(received.Count==2&&received.All(r=>r.GetProperty("messages").GetArrayLength()==2),"Actual HTTP transport sends only system and the admitted scoped context.");
        check(received[0].GetRawText().Contains("FIRST_EMPLOYEE_ONLY_SENTINEL")&&!received[1].GetRawText().Contains("FIRST_EMPLOYEE_ONLY_SENTINEL"),"Actual wire requests preserve employee memory isolation.");
        check(received.All(r=>r.GetProperty("store").GetBoolean()==false&&r.GetProperty("tool_choice").GetString()=="none"&&r.GetProperty("tools").GetArrayLength()==0),"Actual wire requests disable storage and tool selection.");
        check(received.Select(r=>r.GetProperty("user").GetString()).Distinct().Count()==2,"Separate dispatches never reuse a provider correlation key.");
        var detail=JsonSerializer.SerializeToElement(store.TaskDetail(task.Id),CompanyJson.Options);check(detail.GetProperty("attempts")[0].GetProperty("providerReceipt").GetString()=="fixture-receipt","Provider receipt persists with attributed usage.");
        var count=received.Count;try{await closed.CompleteAsync(new(Guid.NewGuid().ToString("N"),new("x",first.Id,"developer",t1.Id,"r","","","",[],[],"data"),"s",4000,1000),default);check(false,"Live adapter must remain closed.");}catch(ModelUnavailableException){check(received.Count==count,"Unqualified live route performs no HTTP request.");}
        var caseIndex=0;foreach(var state in new[]{"tool","usage","route","redirect","error","lost"})
        {
            var person=caseIndex++%2==0?first:second;var thread=person.Id==first.Id?t1:t2;
            mode=state;var work=Submit(person,thread);await scheduler.TickAsync(person.Id);var current=store.Work(work.Id);
            check(current.State==(state=="tool"?"failed":"unknown"),"Transport "+state+" cannot produce an accepted artifact.");
            var budget=List<CompanyBudget>("budgets").Single(b=>b.Id==work.RootId);check(state=="tool"?budget.Used==15&&budget.Reserved==0:budget.Reserved==5000,"Transport "+state+" accounts for confirmed or unknown usage conservatively.");
        }
        check(redirectHits==0&&received.Count==8,"Redirects and failures never trigger fallback or automatic HTTP retries.");await server.StopAsync();
        var repo=Path.Combine(root,"temporary-checkout");Directory.CreateDirectory(repo);var git=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Git","cmd","git.exe");
        async Task Git(params string[] args){var start=new ProcessStartInfo(git){WorkingDirectory=repo,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};foreach(var arg in new[]{"-c","user.name=Fictional Fixture","-c","user.email=fixture@example.invalid","-c","commit.gpgsign=false","-c","core.hooksPath="+Path.Combine(root,"empty-hooks")}.Concat(args))start.ArgumentList.Add(arg);using var p=Process.Start(start)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await output;var text=await error;if(p.ExitCode!=0)throw new Exception("Fixture Git failed: "+text);}
        await Git("init");await File.WriteAllTextAsync(Path.Combine(repo,"source.txt"),"Fictional source");await Git("add","source.txt");await Git("commit","-m","Fictional fixture baseline");
        var inspector=new CompanyWorkspaceInspector(git);var fake=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../HireZero.FakeCodex/bin/Debug/net10.0/HireZero.FakeCodex.exe"));
        var identity=CompanyCodingFactory.Identity(fake);var factory=new CompanyCodingFactory(Path.Combine(root,"coding.sqlite"),fake,Path.Combine(root,"scoped-runtime"),identity);var bridge=new CompanyDeveloperBridge(store,inspector,factory);
        async Task<CompanyExecutionGrant> Prepare(CompanyEmployee e,CompanyThread thread)
        {var work=Submit(e,thread,"developer-work");store.Apply(C("workspace.grant") with{Id=work.Id,ExpectedVersion=work.Version,Body=repo});return await bridge.PrepareAsync(work.Id,repo,true);}
        var grant=await Prepare(first,t1);
        check(store.Work(grant.TaskId).ModelRoute=="openai/gpt-6.1-sol"&&grant.Route=="openai/gpt-6.1-sol","Explicit coding task and reviewed proposal record Sol 6.1.");
        check(!store.AuthorizeDispatch(grant.AttemptId),"Text dispatch cannot bypass an execution grant.");
        try{await bridge.ExecuteFixtureAsync(grant.Id);check(false,"Execution without owner review must fail.");}catch(WorkspaceException){check(!File.Exists(Path.Combine(repo,"fixture-change.txt")),"Unapproved execution starts no runtime write.");}
        grant=A<CompanyExecutionGrant>(C("execution.review") with{Id=grant.Id,ExpectedVersion=grant.Version,Digest=grant.Digest,Decision="approve"});
        var previous=Environment.GetEnvironmentVariable("HIREZERO_PARENT_SENTINEL");Environment.SetEnvironmentVariable("HIREZERO_PARENT_SENTINEL","AMBIENT_PARENT_SENTINEL");
        try{await bridge.ExecuteFixtureAsync(grant.Id);}finally{Environment.SetEnvironmentVariable("HIREZERO_PARENT_SENTINEL",previous);}
        check(store.Work(grant.TaskId).State=="awaiting_review"&&File.Exists(Path.Combine(repo,"fixture-change.txt")),"Actual stdio runner executes approved fake coding work in the temporary repository.");
        check(store.Execution(grant.Id).RunId is not null&&store.Execution(grant.Id).State=="completed","Coding receipt and single-use outcome persist.");
        var wire=await File.ReadAllTextAsync(Path.Combine(root,"scoped-runtime",grant.AttemptId,"fixture-wire.jsonl"));check(wire.Contains("workspaceWrite")&&wire.Contains("gpt-6.1-sol")&&!wire.Contains("AMBIENT_PARENT_SENTINEL"),"Actual runtime wire enforces scope and excludes inherited private environment.");
        try{await bridge.ExecuteFixtureAsync(grant.Id);check(false,"Execution cannot replay.");}catch(WorkspaceException){check(true,"Completed grant cannot dispatch twice.");}
        var stale=await Prepare(second,t2);stale=A<CompanyExecutionGrant>(C("execution.review") with{Id=stale.Id,ExpectedVersion=stale.Version,Digest=stale.Digest,Decision="approve"});await File.AppendAllTextAsync(Path.Combine(repo,"source.txt"),"Changed after approval");
        try{await bridge.ExecuteFixtureAsync(stale.Id);check(false,"Stale checkout must fail.");}catch(WorkspaceException){check(store.Execution(stale.Id).RunId is null,"Checkout fingerprint is checked before starting the runtime.");}
        store.Apply(C("task.cancel") with{Id=stale.TaskId,ExpectedVersion=store.Work(stale.TaskId).Version});
        var unknown=await Prepare(second,t2);unknown=A<CompanyExecutionGrant>(C("execution.review") with{Id=unknown.Id,ExpectedVersion=unknown.Version,Digest=unknown.Digest,Decision="approve"});
        var uncertainBridge=new CompanyDeveloperBridge(store,inspector,new CompanyCodingFactory(Path.Combine(root,"coding.sqlite"),fake,Path.Combine(root,"scoped-runtime"),identity,"scoped-missing-usage"));
        try{await uncertainBridge.ExecuteFixtureAsync(unknown.Id);check(false,"Missing coding usage must not complete.");}catch(WorkspaceException){check(store.Work(unknown.TaskId).State=="unknown"&&store.Execution(unknown.Id).State=="unknown","Missing real protocol usage holds task, execution receipt and reservation.");}
        var collision=await Prepare(first,t1);check(Throws(()=>store.AcquireWorkspace(collision.AttemptId,repo)),"Unknown coding outcome retains the checkout claim against other employees.");
        store.Apply(C("task.cancel") with{Id=collision.TaskId,ExpectedVersion=store.Work(collision.TaskId).Version});
        repo=Path.Combine(root,"configuration-checkout");Directory.CreateDirectory(repo);await Git("init");await File.WriteAllTextAsync(Path.Combine(repo,"source.txt"),"Fictional source");await Git("add","source.txt");await Git("commit","-m","Fictional clean baseline");
        Directory.CreateDirectory(Path.Combine(repo,".codex"));await File.WriteAllTextAsync(Path.Combine(repo,".codex","config.toml"),"# Unreviewed fixture configuration");
        var configured=await Prepare(first,t1);configured=A<CompanyExecutionGrant>(C("execution.review") with{Id=configured.Id,ExpectedVersion=configured.Version,Digest=configured.Digest,Decision="approve"});
        try{await bridge.ExecuteFixtureAsync(configured.Id);check(false,"Project runtime configuration must be refused.");}catch(CodingStartRefusedException){check(store.Execution(configured.Id).State=="not_sent"&&List<CompanyBudget>("budgets").Single(b=>b.Id==store.Work(configured.TaskId).RootId).Reserved==0,"Provable pre-start refusal releases its reservation without launching the runtime.");}
        File.Delete(Path.Combine(repo,".codex","config.toml"));Directory.Delete(Path.Combine(repo,".codex"));
        var waiting=await Prepare(first,t1);waiting=A<CompanyExecutionGrant>(C("execution.review") with{Id=waiting.Id,ExpectedVersion=waiting.Version,Digest=waiting.Digest,Decision="approve"});
        var waitingBridge=new CompanyDeveloperBridge(store,inspector,new CompanyCodingFactory(Path.Combine(root,"coding.sqlite"),fake,Path.Combine(root,"scoped-runtime"),identity,"scoped-wait"));
        var active=waitingBridge.ExecuteFixtureAsync(waiting.Id);var ready=Path.Combine(root,"scoped-runtime",waiting.AttemptId,"fixture-ready");
        for(var i=0;i<200&&!File.Exists(ready);i++)await Task.Delay(25);check(File.Exists(ready),"Actual fake runtime reached its active scoped turn.");
        var currentGrant=store.Execution(waiting.Id);store.Apply(C("execution.review") with{Id=waiting.Id,ExpectedVersion=currentGrant.Version,Decision="revoke"});
        try{await active;check(false,"Revoked runtime must not complete the task.");}catch(WorkspaceException){check(store.Work(waiting.TaskId).State=="canceled"&&store.Execution(waiting.Id).State=="revoked","Owner revocation survives late runtime outcomes.");}
        var interruptedWire=await File.ReadAllTextAsync(Path.Combine(root,"scoped-runtime",waiting.AttemptId,"fixture-wire.jsonl"));check(interruptedWire.Contains("turn/interrupt"),"Revocation interrupts only the owned fake runtime through its actual protocol.");
        using(var connection=new SqliteConnection("Data Source="+Path.Combine(root,"integration-paths.sqlite")+";Pooling=False")){connection.Open();using var q=connection.CreateCommand();q.CommandText="DROP TABLE co_execution_grants; DELETE FROM company_schema_migrations WHERE version=2";q.ExecuteNonQuery();}
        var upgraded=new CompanyStore(Path.Combine(root,"integration-paths.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions());check(JsonSerializer.SerializeToElement(upgraded.Dashboard(),CompanyJson.Options).GetProperty("employees").GetArrayLength()==2,"Version-one database upgrades transactionally without losing employees.");
    }
    static bool Throws(Action action){try{action();return false;}catch(WorkspaceException){return true;}}
    sealed class Counter:ICompanyTokenCounter {public string Identity=>"synthetic-counter-v1";public int Count(string system,string user)=>(system.Length+user.Length)/4;}
}
