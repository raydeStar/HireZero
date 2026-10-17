using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

static class MeasuredQueueChecks
{
    public static readonly string Fake=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../HireZero.FakeCodex/bin/Debug/net10.0/HireZero.FakeCodex.exe"));
    public static string Brief=>File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../tests/fixtures/one-job-marketing-brief.json")));
    public static CompanyCodexConnection Model(string root,string mode)=>new(new(Fake,CompanyCodingFactory.Identity(Fake),Path.Combine(root,"native-scratch"),SyntheticMode:mode));
    public static CompanyCommand C(string action)=>new(Guid.NewGuid().ToString("D"),action);
    public static T[] Items<T>(CompanyStore store,string key)=>CompanyJson.Decode<T[]>(JsonSerializer.SerializeToElement(store.Dashboard(),CompanyJson.Options).GetProperty(key).GetRawText());
    public static CompanyTask Seed(CompanyStore store)
    {
        T A<T>(CompanyCommand c)=>CompanyJson.Decode<T>(store.Apply(c));
        var department=A<CompanyDepartment>(C("department.create") with{Name="Fictional Paper Studio"});
        var employee=A<CompanyEmployee>(C("employee.hire") with{DepartmentId=department.Id,RoleId="marketing",Name="Draft fixture",Responsibility="Prepare internal copy from approved fictional facts"});
        var fields=new{purpose="Fictional paper crane kit",audience="Adult hobbyists",positioning="Paper craft kit",voice="Clear and warm",claims="Contains paper and a printed folding guide",constraints="No prices, proof, performance claims, publishing or outreach"};
        var knowledge=A<CompanyKnowledge>(C("knowledge.save") with{DepartmentId=department.Id,Body=CompanyJson.Encode(fields)});
        store.Apply(C("knowledge.review") with{Id=knowledge.Id,ExpectedVersion=knowledge.Version,Digest=knowledge.Digest,Decision="approve"});
        var thread=Items<CompanyThread>(store,"threads").Single(t=>t.EmployeeId==employee.Id);
        return A<CompanyTask>(C("task.create") with{EmployeeId=employee.Id,ThreadId=thread.Id,Skill="marketing-draft",Body=Brief});
    }
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        await using(var f=await Fixture.Create(root,"success","company-measured-success"))
        {
            var proposal=await f.Prepare();check(f.Starts==0&&!f.Model.Qualified&&f.Store.Work(f.Task.Id).State=="running","Preparing a measured packet does not call a model or enable the strict automatic provider.");
            var detail=await f.Client.GetFromJsonAsync<JsonElement>("/api/company/measured/"+proposal.Id);
            check(proposal.Measured!.Policy is {AdmissionKind:"measured-oauth",PlanningTokens:8000,NativeRequests:1,ProviderHardTokenCap:false,ProviderHardCreditCap:false}&&proposal.Measured.ApplicationBytes<=4096&&proposal.Measured.TaskVersion==f.Store.Work(f.Task.Id).Version&&detail.GetProperty("call").GetProperty("context").GetProperty("input").GetString()==f.Task.Input,"The owner reviews the exact brief, context, runtime identity and explicitly soft policy.");
            check(!f.Store.AuthorizeDispatch(proposal.Id),"The strict dispatch path cannot bypass measured authorization.");
            var run=RunCommand(proposal);
            await f.Refused(run with{RequestId=Guid.NewGuid().ToString("D"),Digest="wrong"});await f.Refused(run with{RequestId=Guid.NewGuid().ToString("D"),Decision=null});
            check(f.Starts==0,"A changed digest or absent soft-limit acknowledgement starts no runtime.");
            var receipt=await f.Post(run);await f.Post(run);
            var saved=await f.Client.GetStringAsync("/api/company/operations/"+run.RequestId);check(saved==receipt,"The exact durable run receipt is available for lost-response reconciliation without resubmitting native work.");
            var done=f.Store.Attempt(proposal.Id);var artifact=Items<CompanyArtifact>(f.Store,"artifacts").Single(a=>a.TaskId==f.Task.Id);
            check(f.Starts==1&&done.Measured?.State=="completed"&&done.Usage==new CompanyTokenUsage(4800,150,5)&&Budget(f.Store,f.Task) is {Used:4950,Reserved:0,Limit:8000}&&artifact.State=="needs_review","The real HTTP queue entry runs one fake-native Marketing job above the old input default, accounts for all runtime usage and saves a review-required draft.");
            await f.Refused(RunCommand(done));check(f.Starts==1,"A spent grant and repeated operation never create a second native request.");
            artifact=CompanyJson.Decode<CompanyArtifact>(await f.Post(C("artifact.review") with{Id=artifact.Id,ExpectedVersion=artifact.Version,Digest=artifact.Digest,Decision="approve"}));
            await f.Post(C("task.complete") with{Id=f.Task.Id,ExpectedVersion=f.Store.Work(f.Task.Id).Version});
            check(f.Store.Work(f.Task.Id).State=="completed"&&artifact.State=="approved","Measured Marketing reaches explicit content review and completion without publication.");
        }
        await using(var f=await Fixture.Create(root,"no-reasoning","company-measured-no-reasoning"))
        {
            var proposal=await f.Prepare();await f.Post(RunCommand(proposal));var done=f.Store.Attempt(proposal.Id);
            check(done.Usage==new CompanyTokenUsage(4800,150)&&Budget(f.Store,f.Task) is {Used:4950,Reserved:0}&&Items<CompanyArtifact>(f.Store,"artifacts").Length==1,"A missing reasoning sub-breakdown remains unknown while complete input/output usage settles in full.");
        }
        await using(var f=await Fixture.Create(root,"bytes","company-measured-success"))
        {
            var note=Items<CompanyEmployee>(f.Store,"employees").Single();
            await f.Post(C("notes.save") with{EmployeeId=note.Id,ExpectedVersion=0,Body=new string('x',4096)});await f.Refused(C("measured.prepare") with{Id=f.Task.Id,ExpectedVersion=f.Store.Work(f.Task.Id).Version,CreditAllowance=1});
            check(f.Starts==0&&Items<JsonElement>(f.Store,"measured").Length==0&&Budget(f.Store,f.Task).Reserved==0,"Oversized application instructions plus context are rejected atomically before preparing a native request.");
        }
        await using(var f=await Fixture.Create(root,"changed-task-version","company-measured-success"))
        {
            var proposal=await f.Prepare();using var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+Path.Combine(f.Root,"data","workspace.sqlite")+";Pooling=False");db.Open();using var update=db.CreateCommand();
            update.CommandText="UPDATE co_tasks SET payload=json_set(payload,'$.version',revision+1),revision=revision+1 WHERE id=$id";update.Parameters.AddWithValue("$id",f.Task.Id);update.ExecuteNonQuery();
            await f.Refused(RunCommand(proposal));check(f.Starts==0&&Budget(f.Store,f.Task).Reserved==0,"A changed task version invalidates the sealed proposal even when its task generation is unchanged.");
        }
        // Restart with a pending proposal: it stays inactive until the exact owner command.
        var restartRoot=Path.Combine(root,"measured-restart");CompanyAttempt pending;CompanyTask restartTask;
        await using(var f=await Fixture.CreateAt(restartRoot,"company-measured-success",true)){pending=await f.Prepare();restartTask=f.Task;}
        await using(var f=await Fixture.CreateAt(restartRoot,"company-measured-success",false,restartTask))
        {check(f.Starts==0&&f.Store.Attempt(pending.Id).Measured?.State=="proposed","Restart never automatically runs a pending measured proposal.");await f.Post(RunCommand(pending));check(f.Starts==1,"A still-current proposal may be authorized exactly once after restart.");}

        await using(var f=await Fixture.Create(root,"crash","company-measured-success"))
        {
            var proposal=await f.Prepare();var run=RunCommand(proposal);f.Store.Apply(run);f.Store.Recover();await f.Post(run);
            check(f.Starts==0&&f.Store.Attempt(proposal.Id).Measured?.State=="unknown"&&Budget(f.Store,f.Task).Reserved==8000,"A crash after durable arming and before runtime submission is unknown and cannot replay, even when the request was never sent.");
        }
        foreach(var mode in new[]{"company-measured-overrun","company-measured-output","company-measured-characters","company-measured-inconsistent","company-postbilling-refusal","company-missing-usage","company-tool","company-nontext","company-drain","company-credit-change","company-start-refusal"})
        {
            await using var f=await Fixture.Create(root,mode,mode);var proposal=await f.Prepare();var run=RunCommand(proposal);await f.Post(run);await f.Post(run);
            var result=f.Store.Attempt(proposal.Id);var budget=Budget(f.Store,f.Task);
            check(f.Starts==1&&Items<CompanyArtifact>(f.Store,"artifacts").Length==0&&result.Error=="provider-accounting-unconfirmed","Measured "+mode+" withholds output, retains its single-use outcome and holds future provider dispatch.");
            if(mode=="company-measured-overrun")check(result.ReportedUsage==9150&&result.Usage==new CompanyTokenUsage(9000,150,5)&&budget.Reserved==8000,"A token tripwire overrun retains full actual usage without clamping it to the planning reservation.");
            else if(mode=="company-measured-output")check(result.ReportedUsage==6000&&budget is {Used:6000,Reserved:0},"An observed output tripwire settles complete usage while withholding content.");
            else if(mode=="company-measured-characters")check(result.ReportedUsage==4950&&budget is {Used:4950,Reserved:0},"A visible-character soft stop withholds content but retains and settles a drained complete receipt.");
            else if(mode=="company-measured-inconsistent")check(result.ReportedUsage==4951&&result.Usage==new CompanyTokenUsage(4800,150,5)&&budget.Reserved==8000,"Inconsistent provider numbers remain visible without settling an untrusted total or releasing the reservation.");
            else if(mode is "company-drain" or "company-credit-change")check(result.ReportedUsage==120&&budget is {Used:120,Reserved:0},"Quota and credit-balance anomalies preserve full usage and hold continuation.");
            else if(mode=="company-nontext")check(result.ReportedUsage==120&&budget is {Used:120,Reserved:0},"A forbidden runtime item with later complete usage settles billing without creating content.");
            else if(mode=="company-postbilling-refusal")check(result.ReportedUsage==120&&budget is {Used:120,Reserved:0},"A failed final account read preserves complete native usage.");
            else check(result.ReportedUsage is null&&budget.Reserved==8000,"Missing final usage never becomes zero or releases its planning hold.");
            var successor=CompanyJson.Decode<CompanyTask>(await f.Post(C("task.create") with{EmployeeId=f.Task.EmployeeId,ThreadId=f.Task.ThreadId,Skill="marketing-draft",Body=Brief}));
            await f.Refused(C("measured.prepare") with{Id=successor.Id,ExpectedVersion=f.Store.Work(successor.Id).Version,CreditAllowance=1});
            check(f.Starts==1,"A held anomaly prevents a new task from obtaining measured dispatch authority.");
            var reopened=new CompanyStore(Path.Combine(f.Root,"data","workspace.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions(),measuredRuntimeIdentity:f.Model.RuntimeIdentity);reopened.Recover();
            try{reopened.Apply(C("measured.prepare") with{Id=successor.Id,ExpectedVersion=reopened.Work(successor.Id).Version,CreditAllowance=1});check(false,"Restart must preserve provider hold.");}catch(WorkspaceException){check(f.Starts==1,"The provider hold persists independently of the stopped native connection, including after restart.");}
        }
        foreach(var mode in new[]{"company-wait","company-measured-late","company-measured-partial","company-measured-stale-partial","company-measured-queued-partial"})
        {
            await using var f=await Fixture.Create(root,mode,mode);var proposal=await f.Prepare();var command=RunCommand(proposal);var running=f.Post(command);
            for(var i=0;i<250&&!Directory.GetFiles(f.Root,"fixture-ready",SearchOption.AllDirectories).Any();i++)await Task.Delay(20);
            check(Directory.GetFiles(f.Root,"fixture-ready",SearchOption.AllDirectories).Any(),"Fake measured native turn reached its owned wait state.");
            var durable=await f.Client.GetFromJsonAsync<CompanyAttempt>("/api/company/operations/"+command.RequestId);var duplicate=f.Post(command);
            check(durable?.Measured?.State=="dispatched","A lost run response can be reconciled to its consumed durable receipt while native work is still in flight.");
            await f.Post(C("task.cancel") with{Id=f.Task.Id,ExpectedVersion=f.Store.Work(f.Task.Id).Version});await running;await duplicate;
            var result=f.Store.Attempt(proposal.Id);check(f.Starts==1&&result.Error=="provider-accounting-unconfirmed"&&f.Store.Work(f.Task.Id).State=="canceled"&&Items<CompanyArtifact>(f.Store,"artifacts").Length==0,"Owner cancellation reaches the real measured entry point and never publishes late content.");
            if(mode=="company-measured-late")check(result.ReportedUsage==4950&&Budget(f.Store,f.Task) is {Used:4950,Reserved:0},"A confirmed ordinary completion drained after native interruption settles billing without a draft.");
            else if(mode is "company-measured-partial" or "company-measured-stale-partial" or "company-measured-queued-partial")check(result.ReportedUsage is null&&result.Usage==new CompanyTokenUsage(4800,20,5)&&Budget(f.Store,f.Task).Reserved==8000,"Partial native usage survives interruption while final billing stays unknown and reserved.");
            else check(result.ReportedUsage is null&&Budget(f.Store,f.Task).Reserved==8000,"Canceled native work with no usage retains its reservation.");
        }
        await using(var f=await Fixture.Create(root,"before-arm","company-measured-success"))
        {var proposal=await f.Prepare();await f.Post(C("task.cancel") with{Id=f.Task.Id,ExpectedVersion=f.Store.Work(f.Task.Id).Version});await f.Refused(RunCommand(proposal));check(f.Starts==0&&Budget(f.Store,f.Task).Reserved==0,"Cancel before authorization consumes no native request or planning reservation.");}
        // A caller deadline/shutdown must fence even a provider that ignores cancellation.
        var slowRoot=Path.Combine(root,"measured-deadline");var slow=new SlowProvider();var slowStore=new CompanyStore(Path.Combine(slowRoot,"data.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions(),measuredRuntimeIdentity:slow.RuntimeIdentity);var slowTask=Seed(slowStore);
        var slowProposal=CompanyJson.Decode<CompanyAttempt>(slowStore.Apply(C("measured.prepare") with{Id=slowTask.Id,ExpectedVersion=slowTask.Version,CreditAllowance=1}));
        using(var cancel=new CancellationTokenSource())
        {
            var run=new CompanyScheduler(slowStore,slow).RunMeasuredOnceAsync(RunCommand(slowProposal),slow,cancel.Token);await slow.Entered.Task;cancel.Cancel();await slow.Canceled.Task;
            slow.Completion.SetResult(new("Late draft must be withheld",4950,"fictional-deadline-receipt",new(4800,150,5)));await run;
            check(Items<CompanyArtifact>(slowStore,"artifacts").Length==0&&Budget(slowStore,slowTask) is {Used:4950,Reserved:0}&&slowStore.Attempt(slowProposal.Id).Error=="provider-accounting-unconfirmed","A provider that ignores the deadline cannot publish during grace; its complete late usage settles once and continuation stays held.");
        }
        var badReceiptRoot=Path.Combine(root,"measured-bad-receipt");var badProvider=new SlowProvider();var badStore=new CompanyStore(Path.Combine(badReceiptRoot,"data.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions(),measuredRuntimeIdentity:badProvider.RuntimeIdentity);var badTask=Seed(badStore);
        var badProposal=CompanyJson.Decode<CompanyAttempt>(badStore.Apply(C("measured.prepare") with{Id=badTask.Id,ExpectedVersion=badTask.Version,CreditAllowance=1}));
        badProvider.Completion.SetResult(new("Withheld fictional draft",4950,new string('x',200),new(4800,150,5)));await new CompanyScheduler(badStore,badProvider).RunMeasuredOnceAsync(RunCommand(badProposal),badProvider);
        check(Budget(badStore,badTask) is {Used:4950,Reserved:0}&&badStore.Attempt(badProposal.Id).ProviderReceipt?.StartsWith("invalid-receipt:")==true&&Items<CompanyArtifact>(badStore,"artifacts").Length==0,"An invalid receipt identity cannot discard known usage or publish content; its redacted correlation is retained.");
        var clock=DateTimeOffset.UtcNow;var clockRoot=Path.Combine(root,"measured-clock");var clockModel=Model(clockRoot,"company-measured-success");var clockStore=new CompanyStore(Path.Combine(clockRoot,"data.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions(),measuredRuntimeIdentity:clockModel.RuntimeIdentity){Clock=()=>clock};var clockTask=Seed(clockStore);
        var expiring=CompanyJson.Decode<CompanyAttempt>(clockStore.Apply(C("measured.prepare") with{Id=clockTask.Id,ExpectedVersion=clockTask.Version,CreditAllowance=1}));clock=clock.AddMinutes(11);
        try{await new CompanyScheduler(clockStore,clockModel).RunMeasuredOnceAsync(RunCommand(expiring),clockModel);check(false,"Expired packet must not run.");}catch(WorkspaceException){check(!Directory.Exists(Path.Combine(clockRoot,"native-scratch")),"Expired authorization starts no native process.");}
    }
    static CompanyCommand RunCommand(CompanyAttempt proposal)=>C("measured.run") with{Id=proposal.Id,ExpectedVersion=proposal.Version,Digest=proposal.Measured!.Digest,Decision="accept-soft-limits"};
    static CompanyBudget Budget(CompanyStore store,CompanyTask task)=>Items<CompanyBudget>(store,"budgets").Single(b=>b.Id==task.RootId);
    sealed class SlowProvider:ICompanyModel,ICompanyMeasuredModel
    {
        public bool Qualified=>false;public string Status=>"Fictional noncooperative fixture";public string RuntimeIdentity=>"fictional-delayed-runtime";
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Canceled=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CompanyModelReply> Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CompanyInputAccounting CountInput(CompanyModelCall call)=>throw new NotSupportedException();
        public Task<CompanyModelReply> CompleteAsync(CompanyModelCall call,CancellationToken token)=>throw new NotSupportedException();
        public async Task<CompanyModelReply> CompleteMeasuredAsync(CompanyModelCall call,CompanyMeasuredJob job,CancellationToken token)
        {using var registration=token.Register(()=>Canceled.TrySetResult());Entered.TrySetResult();return await Completion.Task;}
    }
    sealed class Fixture:IAsyncDisposable
    {
        public required string Root;public required WebApplication App;public required HttpClient Client;public required CompanyStore Store;public required CompanyCodexConnection Model;public required CompanyTask Task;
        public int Starts=>Directory.GetFiles(Root,"fixture-native-starts",SearchOption.AllDirectories).Sum(p=>File.ReadAllLines(p).Length);
        public static Task<Fixture> Create(string root,string name,string mode)=>CreateAt(Path.Combine(root,"measured-http-"+name),mode,true);
        public static async Task<Fixture> CreateAt(string root,string mode,bool seed,CompanyTask? prior=null)
        {
            Directory.CreateDirectory(root);var model=MeasuredQueueChecks.Model(root,mode);var app=CompanyWorkspace.TryCreate([CompanyWorkspace.Flag],"owner@example.invalid",true,Path.Combine(root,"data"),fixture:model,fixtureUrl:"http://127.0.0.1:0")!;await app.StartAsync();var store=app.Services.GetRequiredService<CompanyStore>();
            var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};client.DefaultRequestHeaders.Host="127.0.0.1:5065";client.DefaultRequestHeaders.Add("Origin","http://127.0.0.1:5065");client.DefaultRequestHeaders.Add("X-Dev-Request","1");
            var task=seed?Seed(store):prior!;for(var i=0;i<100&&store.Work(task.Id).State=="queued";i++)await System.Threading.Tasks.Task.Delay(20);
            return new(){Root=root,App=app,Client=client,Store=store,Model=model,Task=store.Work(task.Id)};
        }
        public async Task<CompanyAttempt> Prepare()=>CompanyJson.Decode<CompanyAttempt>(await Post(C("measured.prepare") with{Id=Task.Id,ExpectedVersion=Store.Work(Task.Id).Version,CreditAllowance=1}));
        public async Task<string> Post(CompanyCommand command){using var response=await Client.PostAsJsonAsync("/api/company/commands",command);var body=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new Exception("Measured fixture request failed: "+body);return body;}
        public async Task Refused(CompanyCommand command){using var response=await Client.PostAsJsonAsync("/api/company/commands",command);if(response.IsSuccessStatusCode)throw new Exception("Measured fixture mutation should be refused.");}
        public async ValueTask DisposeAsync(){Client.Dispose();await App.StopAsync();await App.DisposeAsync();}
    }
}
