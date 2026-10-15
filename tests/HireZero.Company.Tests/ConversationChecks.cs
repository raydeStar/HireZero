using System.Net.Http.Json;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Codex;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

static class ConversationChecks
{
    static CompanyCommand C(string action)=>MeasuredQueueChecks.C(action);
    static T[] Items<T>(CompanyStore s,string key)=>MeasuredQueueChecks.Items<T>(s,key);
    sealed record Prepared(CompanyMessage Message,CompanyTask Task,CompanyAttempt Attempt);
    static CompanyCommand Run(Prepared p)=>C("measured.run") with{Id=p.Attempt.Id,ExpectedVersion=p.Attempt.Version,Digest=p.Attempt.Measured!.Digest,Decision="accept-soft-limits"};
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        await using(var f=await Fixture.Create(root,"separation","company-measured-success"))
        {
            var a=f.Seed("marketing","ALPHA");var b=f.Seed("developer","BETA");var ceo=f.Seed("ceo","CHAIR");
            var fact=CompanyJson.Decode<CompanyKnowledge>(f.Store.Apply(C("knowledge.save") with{Body="APPROVED_COMPANY_FACT"}));f.Store.Apply(C("knowledge.review") with{Id=fact.Id,ExpectedVersion=fact.Version,Digest=fact.Digest,Decision="approve"});
            var prepare=C("conversation.prepare") with{EmployeeId=a.Employee.Id,Scope="company",Body="Summarize only the supplied company facts.",CreditAllowance=1};
            var first=CompanyJson.Decode<Prepared>(await f.Post(prepare));var duplicate=await f.Post(prepare);
            check(CompanyJson.Decode<Prepared>(duplicate).Task.Id==first.Task.Id&&f.Starts==0&&Items<CompanyTask>(f.Store,"tasks").Length==1,"Conversation preparation is atomic/idempotent and sends no native request.");
            var companyThread=Items<CompanyThread>(f.Store,"threads").Single(t=>t.Id==first.Task.ThreadId);var call=f.Store.TextCall(first.Attempt.Id);
            check(companyThread.Scope=="company"&&first.Task.Kind=="conversation"&&first.Task.ModelRoute=="openai/gpt-6-luna"&&call.ModelRoute=="openai/gpt-6-luna","Server-owned conversation kind and company scope bind one Luna text reply.");
            check(call.Context.CompanyKnowledge.Contains("APPROVED_COMPANY_FACT")&&call.Context.Scope=="company"&&!call.Context.Responsibility.Contains("ONLY_")&&call.Context.DepartmentKnowledge==""&&call.Context.Notes==""&&call.Context.History.Length==0&&call.Context.Evidence.Length==0&&call.Context.Colleagues is null&&call.Context.CompanyWork is null,"Company packet excludes department facts, private notes/history, handoffs and directory-wide metadata.");
            var companyDetail=JsonSerializer.SerializeToElement(f.Store.Thread(a.Employee.Id,companyThread.Id),CompanyJson.Options);
            check(companyDetail.GetProperty("notes").GetProperty("body").GetString()=="","Company thread reads cannot disclose employee notes.");
            check(!f.Store.AuthorizeDispatch(first.Attempt.Id)&&f.Store.Claim(a.Employee.Id,true,first.Task.Id) is null,"Ordinary strict/worker entry points cannot dispatch a conversation turn.");
            var before=Items<CompanyTask>(f.Store,"tasks").Length;
            await f.Refused(C("conversation.prepare") with{EmployeeId=a.Employee.Id,ThreadId=companyThread.Id,Scope="employee",Body="Wrong scope",CreditAllowance=1});
            await f.Refused(C("task.create") with{EmployeeId=a.Employee.Id,ThreadId=companyThread.Id,Skill="chat",Body="Autonomous work"});
            await f.Refused(C("company.message") with{EmployeeId=a.Employee.Id,ThreadId=companyThread.Id,Body="Wrong entry"});
            check(Items<CompanyTask>(f.Store,"tasks").Length==before,"Wrong scope and generic work paths cannot create a partial or autonomous company turn.");
            var run=Run(first);await Task.WhenAll(f.Post(run),f.Post(run));
            check(f.Starts==1&&f.Store.Work(first.Task.Id).State=="completed"&&Items<CompanyArtifact>(f.Store,"artifacts").Length==0&&Items<CompanyPlan>(f.Store,"plans").Length==0,"Exactly one authorized native fake reply completes only its turn, creating no artifacts, plans or child work.");
            check(f.Store.Attempt(first.Attempt.Id).ReportedUsage==4950&&Items<CompanyBudget>(f.Store,"budgets").Single(x=>x.Id==first.Task.RootId) is {Used:4950,Reserved:0},"A conversational reply retains and settles full measured input/output usage.");
            var native=Directory.GetFiles(f.Root,"fixture-last-context.json",SearchOption.AllDirectories).Select(File.ReadAllText).Last();
            check(native==CompanyJson.Encode(call.Context)&&!native.Contains("ONLY_ALPHA")&&!native.Contains("ONLY_BETA"),"The real stdio entry receives precisely the reviewed company packet.");
            var second=await f.Prepare(a,"employee","What was my private topic?");var privateCall=f.Store.TextCall(second.Attempt.Id);
            check(privateCall.Context.Notes=="ONLY_ALPHA_NOTES"&&privateCall.Context.DepartmentKnowledge.Contains("ONLY_ALPHA_FACT")&&privateCall.Context.History.Any(m=>m.Body=="ONLY_ALPHA_PRIVATE_HISTORY")&&!CompanyJson.Encode(privateCall.Context).Contains("SIBLING")&&!CompanyJson.Encode(privateCall.Context).Contains("ONLY_BETA")&&!CompanyJson.Encode(privateCall.Context).Contains("Summarize only"),"Private replies include only their employee facts, notes and exact thread history, excluding company/sibling/other-employee transcripts.");
            await f.Post(Run(second));
            foreach(var action in new[]{"task.retry","task.reassign","task.complete","artifact.create"})await f.Refused(C(action) with{Id=second.Task.Id,ExpectedVersion=f.Store.Work(second.Task.Id).Version,Body="Cannot promote a conversational reply",EmployeeId=b.Employee.Id,ThreadId=b.Thread.Id});
            var chair=await f.Prepare(ceo,"company","Propose no actions. Who retains final approval?");var chairCall=f.Store.TextCall(chair.Attempt.Id);
            check(chairCall.Context.Colleagues is null&&chairCall.Context.CompanyWork is null&&chairCall.Context.Notes==""&&chairCall.Context.DepartmentKnowledge=="","A CEO company reply cannot inherit overview metadata or private context.");await f.Post(Run(chair));
            var reopened=new CompanyStore(Path.Combine(f.Root,"data","workspace.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions(),measuredRuntimeIdentity:f.Model.RuntimeIdentity);reopened.Recover();
            var history=JsonSerializer.SerializeToElement(reopened.Thread(a.Employee.Id,a.Thread.Id),CompanyJson.Options).GetProperty("messages").GetRawText();
            check(history.Contains("Fictional conversational reply")&&reopened.Work(second.Task.Id).State=="completed"&&f.Starts==3,"Conversation history and terminal receipts survive reopening without replay.");
            var pending=await f.Prepare(b,"employee","Do not run until I explicitly send.");reopened.Recover();await Task.Delay(100);
            check(f.Starts==3&&reopened.Attempt(pending.Attempt.Id).Measured!.State=="proposed","An unconsumed reply proposal survives recovery without automatic resumption.");await f.Cancel(pending);
            for(var i=0;i<12;i++)f.Store.Apply(C("message.send") with{EmployeeId=b.Employee.Id,ThreadId=b.Thread.Id,Body="OPTIONAL_HISTORY_"+i+new string('x',250)});
            var packed=await f.Prepare(b,"employee","A short new question.");var packedCall=f.Store.TextCall(packed.Attempt.Id);
            check(packedCall.Context.History.Length<13&&packedCall.Context.Notes=="ONLY_BETA_NOTES"&&packedCall.Context.DepartmentKnowledge.Contains("ONLY_BETA_FACT")&&packed.Attempt.Measured!.ApplicationBytes<=4096,"Measured conversation packing removes only optional oldest history and retains required context within the hard byte limit.");await f.Cancel(packed);
            var notes=CompanyJson.Decode<CompanyNotes>(JsonSerializer.SerializeToElement(f.Store.Thread(b.Employee.Id,b.Thread.Id),CompanyJson.Options).GetProperty("notes").GetRawText());f.Store.Apply(C("notes.save") with{EmployeeId=b.Employee.Id,ExpectedVersion=notes.Version,Body=new string('n',5000)});
            var count=Items<CompanyTask>(f.Store,"tasks").Length;var messages=JsonSerializer.SerializeToElement(f.Store.Thread(b.Employee.Id,b.Thread.Id),CompanyJson.Options).GetProperty("messages").GetArrayLength();
            await f.Refused(C("conversation.prepare") with{EmployeeId=b.Employee.Id,ThreadId=b.Thread.Id,Scope="employee",Body="Too much required context",CreditAllowance=1});
            check(Items<CompanyTask>(f.Store,"tasks").Length==count&&JsonSerializer.SerializeToElement(f.Store.Thread(b.Employee.Id,b.Thread.Id),CompanyJson.Options).GetProperty("messages").GetArrayLength()==messages&&f.Starts==3,"Oversized required context rolls back message/task/proposal and never spends.");
        }
        foreach(var mode in new[]{"company-quota","company-missing-usage","company-drain","company-measured-inconsistent","company-measured-overrun","company-measured-output","company-nontext","company-start-refusal"})
        {
            await using var f=await Fixture.Create(root,mode,mode);var employee=f.Seed("marketing","ERROR");var p=await f.Prepare(employee,"employee","One factual reply only.");await f.Post(Run(p));var attempt=f.Store.Attempt(p.Attempt.Id);
            check(f.Store.Work(p.Task.Id).State!="completed"&&JsonSerializer.SerializeToElement(f.Store.Thread(employee.Employee.Id,employee.Thread.Id),CompanyJson.Options).GetProperty("messages").EnumerateArray().All(m=>m.GetProperty("author").GetString()=="owner")&&Items<CompanyArtifact>(f.Store,"artifacts").Length==0,"Errors, quota exhaustion and anomalous content never enter conversational history.");
            check(attempt.Error=="provider-accounting-unconfirmed"&&f.Starts==(mode=="company-quota"?0:1),"Conversation failures preserve a provider hold and never continue automatically.");
            if(mode is "company-drain" or "company-nontext")check(attempt.ReportedUsage==120&&attempt.Usage==new CompanyTokenUsage(100,20,5),"Complete measured usage survives a rejected conversational response.");
            if(mode=="company-measured-inconsistent")check(attempt.ReportedUsage==4951&&attempt.Usage==new CompanyTokenUsage(4800,150,5),"Inconsistent conversational usage is retained without trusted settlement.");
            if(mode=="company-measured-overrun")check(attempt.ReportedUsage==9150&&attempt.Usage==new CompanyTokenUsage(9000,150,5)&&Items<CompanyBudget>(f.Store,"budgets").Single(b=>b.Id==p.Task.RootId).Reserved==8000,"Conversational overage preserves all measured usage and its reservation without clamping.");
            if(mode=="company-measured-output")check(attempt.ReportedUsage==6000&&Items<CompanyBudget>(f.Store,"budgets").Single(b=>b.Id==p.Task.RootId) is {Used:6000,Reserved:0},"An output tripwire withholds reply text and settles full known usage.");
            await f.Refused(C("conversation.prepare") with{EmployeeId=employee.Employee.Id,ThreadId=employee.Thread.Id,Scope="employee",Body="No automatic retry",CreditAllowance=1});
        }
        foreach(var mode in new[]{"company-wait","company-measured-late","company-measured-partial"})
        {
            await using var f=await Fixture.Create(root,mode,mode);var employee=f.Seed("developer","CANCEL");var p=await f.Prepare(employee,"employee","Wait for the owner.");var command=Run(p);var running=f.Post(command);
            for(var i=0;i<250&&!Directory.GetFiles(f.Root,"fixture-ready",SearchOption.AllDirectories).Any();i++)await Task.Delay(20);
            check(Directory.GetFiles(f.Root,"fixture-ready",SearchOption.AllDirectories).Any()&&f.Store.Attempt(p.Attempt.Id).Measured!.State=="dispatched","Pending state reflects a real dispatched fake turn.");
            var duplicate=f.Post(command);await f.Cancel(p);await running;await duplicate;var a=f.Store.Attempt(p.Attempt.Id);
            check(f.Starts==1&&f.Store.Work(p.Task.Id).State=="canceled"&&a.Error=="provider-accounting-unconfirmed"&&JsonSerializer.SerializeToElement(f.Store.Thread(employee.Employee.Id,employee.Thread.Id),CompanyJson.Options).GetProperty("messages").GetArrayLength()==2,"Cancellation interrupts the owned turn; duplicate send/late content cannot create a reply.");
            if(mode=="company-measured-late")check(a.ReportedUsage==4950&&Items<CompanyBudget>(f.Store,"budgets").Single(b=>b.Id==p.Task.RootId) is {Used:4950,Reserved:0},"A complete late reply receipt settles once without publishing canceled content.");
            if(mode=="company-measured-partial")check(a.ReportedUsage is null&&a.Usage==new CompanyTokenUsage(4800,20,5)&&Items<CompanyBudget>(f.Store,"budgets").Single(b=>b.Id==p.Task.RootId).Reserved==8000,"Partial cancellation receipts retain usage and the planning reservation.");
        }
        await ReviewRoutingAsync(root,check);
    }
    private static async Task ReviewRoutingAsync(string root,Action<bool,string> check)
    {
        await using var f=await Fixture.Create(root,"review-routing","company-conversation-browser");var marketing=f.Seed("marketing","APP_PRIVATE");var developer=f.Seed("developer","REPO_PRIVATE");
        const string app="APP_REVIEW_FIXTURE: Review the fictional Compass app from supplied evidence only. [APP-UI-01] Navigation has Company, People and private employee conversations. Suggest at most three product-communication improvements; label missing evidence. No tools or actions.";
        const string repo="REPOSITORY_REVIEW_FIXTURE: Review fictional example/compass from supplied evidence only. [REPO-META-01] One explicitly scoped repository, main revision fictional-sha, one pending check. Suggest at most three updates to investigate. No fetching, coding or GitHub changes.";
        var a=await f.Prepare(marketing,"company",app);var b=await f.Prepare(developer,"company",repo);var ac=f.Store.TextCall(a.Attempt.Id);var bc=f.Store.TextCall(b.Attempt.Id);
        check(f.Starts==0&&ac.Context.RoleId=="marketing"&&bc.Context.RoleId=="developer"&&ac.ModelRoute=="openai/gpt-6-luna"&&bc.ModelRoute==ac.ModelRoute,"Explicit product-feedback and repository-triage selection routes to the correct employees and Luna without dispatch during preparation.");
        check(ac.Context.Input==app&&bc.Context.Input==repo&&!CompanyJson.Encode(ac.Context).Contains("REPO_PRIVATE")&&!CompanyJson.Encode(bc.Context).Contains("APP_PRIVATE")&&!CompanyJson.Encode(ac.Context).Contains("REPOSITORY_REVIEW_FIXTURE")&&!CompanyJson.Encode(bc.Context).Contains("APP_REVIEW_FIXTURE")&&ac.Context.Notes==""&&bc.Context.Notes==""&&ac.Context.History.Length==0&&bc.Context.History.Length==0,"Two review packets contain only their exact supplied inventory and exclude other-role and private evidence.");
        foreach(var p in new[]{a,b}){var exact=Run(p);await Task.WhenAll(f.Post(exact),f.Post(exact));await f.Refused(Run(p));}
        check(f.Starts==2&&f.Store.Work(a.Task.Id).State=="completed"&&f.Store.Work(b.Task.Id).State=="completed","Duplicate sends start exactly two fake review turns, one per requested review.");
        string Transcript(CompanyStore store,Prepared p)=>JsonSerializer.SerializeToElement(store.Thread(p.Task.EmployeeId,p.Task.ThreadId),CompanyJson.Options).GetProperty("messages").GetRawText();
        check(Transcript(f.Store,a).Contains("SIMULATED MARKETING REVIEW")&&Transcript(f.Store,a).Contains("APP-UI-01")&&!Transcript(f.Store,a).Contains("SIMULATED DEVELOPER REVIEW")&&Transcript(f.Store,b).Contains("SIMULATED DEVELOPER REVIEW")&&Transcript(f.Store,b).Contains("REPO-META-01")&&!Transcript(f.Store,b).Contains("SIMULATED MARKETING REVIEW"),"Saved simulated findings retain evidence references, stay in the selected chat and are visibly labeled as synthetic.");
        check(new[]{a,b}.All(p=>f.Store.Attempt(p.Attempt.Id).ReportedUsage==4950&&Items<CompanyBudget>(f.Store,"budgets").Single(x=>x.Id==p.Task.RootId) is {Used:4950,Reserved:0})&&Items<CompanyBudget>(f.Store,"budgets").Single(x=>x.Id=="company-total") is {Used:9900,Reserved:0},"Both review turns settle full measured usage once across their task and shared budgets.");
        var reopened=new CompanyStore(Path.Combine(f.Root,"data","workspace.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions(),measuredRuntimeIdentity:f.Model.RuntimeIdentity);reopened.Recover();
        check(Transcript(reopened,a).Contains("SIMULATED MARKETING REVIEW")&&Transcript(reopened,b).Contains("SIMULATED DEVELOPER REVIEW")&&f.Starts==2,"Both saved review chats can reopen without redispatch.");
        var unsent=await f.Prepare(marketing,"company","APP_REVIEW_FIXTURE: Cancel this proposed follow-up without dispatch.");await f.Cancel(unsent);
        check(f.Starts==2&&f.Store.Attempt(unsent.Attempt.Id).Phase=="not_sent"&&Items<CompanyBudget>(f.Store,"budgets").Single(x=>x.Id==unsent.Task.RootId) is {Used:0,Reserved:0},"Canceling a prepared review sends nothing and consumes no token reservation.");
        var pending=await f.Prepare(developer,"company","[wait] REPOSITORY_REVIEW_FIXTURE: Wait for explicit cancellation.");var run=Run(pending);var active=f.Post(run);
        for(var i=0;i<250&&!Directory.GetFiles(f.Root,"fixture-partial-emitted",SearchOption.AllDirectories).Any();i++)await Task.Delay(20);
        check(Directory.GetFiles(f.Root,"fixture-partial-emitted",SearchOption.AllDirectories).Any()&&f.Store.Attempt(pending.Attempt.Id).Measured!.State=="dispatched","The pending review exercises a real emitted fake delta before cancellation.");
        var duplicate=f.Post(run);await f.Cancel(pending);await active;await duplicate;
        check(f.Starts==3&&f.Store.Work(pending.Task.Id).State=="canceled"&&!Transcript(f.Store,pending).Contains("UNCONFIRMED_PARTIAL_REPLY")&&Items<CompanyBudget>(f.Store,"budgets").Single(x=>x.Id==pending.Task.RootId) is {Used:0,Reserved:8000},"Cancellation withholds partial review text and retains the unknown-usage reservation; duplicate Send does not replay.");
        reopened.Recover();check(f.Starts==3&&Items<CompanyArtifact>(f.Store,"artifacts").Length==0&&Items<CompanyPlan>(f.Store,"plans").Length==0&&Items<CompanyExecutionGrant>(f.Store,"executions").Length==0,"Review routing creates no execution grants, approved artifacts or autonomous plans, and recovery does not resume the canceled turn.");
    }
    sealed record Person(CompanyEmployee Employee,CompanyThread Thread);
    sealed class Fixture:IAsyncDisposable
    {
        public required string Root;public required WebApplication App;public required HttpClient Client;public required CompanyStore Store;public required CompanyCodexConnection Model;
        public int Starts=>Directory.GetFiles(Root,"fixture-native-starts",SearchOption.AllDirectories).Sum(p=>File.ReadAllLines(p).Length);
        public static async Task<Fixture> Create(string root,string name,string mode)
        {
            var folder=Path.Combine(root,"conversation-http-"+name);Directory.CreateDirectory(folder);var model=MeasuredQueueChecks.Model(folder,mode);var app=CompanyWorkspace.TryCreate([CompanyWorkspace.Flag],"owner@example.invalid",true,Path.Combine(folder,"data"),fixture:model,fixtureUrl:"http://127.0.0.1:0")!;await app.StartAsync();
            var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};client.DefaultRequestHeaders.Host="127.0.0.1:5065";client.DefaultRequestHeaders.Add("Origin","http://127.0.0.1:5065");client.DefaultRequestHeaders.Add("X-Dev-Request","1");
            return new(){Root=folder,App=app,Client=client,Store=app.Services.GetRequiredService<CompanyStore>(),Model=model};
        }
        public Person Seed(string role,string tag)
        {
            T A<T>(CompanyCommand c)=>CompanyJson.Decode<T>(Store.Apply(c));var d=A<CompanyDepartment>(C("department.create") with{Name="Fictional "+tag});var e=A<CompanyEmployee>(C("employee.hire") with{DepartmentId=d.Id,RoleId=role,Name="Fictional "+tag,Responsibility="ONLY_"+tag+"_RESPONSIBILITY"});
            var fields=CompanyWorkspace.Definitions().Single(r=>r.Id==role).OnboardingFields.ToDictionary(k=>k,k=>"ONLY_"+tag+"_FACT");var k=A<CompanyKnowledge>(C("knowledge.save") with{DepartmentId=d.Id,Body=CompanyJson.Encode(fields)});Store.Apply(C("knowledge.review") with{Id=k.Id,ExpectedVersion=k.Version,Digest=k.Digest,Decision="approve"});
            var t=Items<CompanyThread>(Store,"threads").Single(t=>t.EmployeeId==e.Id);Store.Apply(C("notes.save") with{EmployeeId=e.Id,Body="ONLY_"+tag+"_NOTES"});Store.Apply(C("message.send") with{EmployeeId=e.Id,ThreadId=t.Id,Body="ONLY_"+tag+"_PRIVATE_HISTORY"});var sibling=A<CompanyThread>(C("thread.create") with{EmployeeId=e.Id,Name="Sibling"});Store.Apply(C("message.send") with{EmployeeId=e.Id,ThreadId=sibling.Id,Body="ONLY_"+tag+"_SIBLING_HISTORY"});return new(e,t);
        }
        public async Task<Prepared> Prepare(Person p,string scope,string body)=>CompanyJson.Decode<Prepared>(await Post(C("conversation.prepare") with{EmployeeId=p.Employee.Id,ThreadId=scope=="employee"?p.Thread.Id:null,Scope=scope,Body=body,CreditAllowance=1}));
        public Task<string> Cancel(Prepared p)=>Post(C("task.cancel") with{Id=p.Task.Id,ExpectedVersion=Store.Work(p.Task.Id).Version});
        public async Task<string> Post(CompanyCommand c){using var response=await Client.PostAsJsonAsync("/api/company/commands",c);var body=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new Exception("Conversation HTTP fixture refused: "+body);return body;}
        public async Task Refused(CompanyCommand c){using var response=await Client.PostAsJsonAsync("/api/company/commands",c);if(response.IsSuccessStatusCode)throw new Exception("Conversation command should be refused.");}
        public async ValueTask DisposeAsync(){Client.Dispose();await App.StopAsync();await App.DisposeAsync();}
    }
}
