using System.Text.Json;
using HireZero.Core;

static class MarketingQueueChecks
{
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        var index=0;
        (CompanyStore Store,CompanyEmployee Employee,CompanyTask Task,string Database) Setup()
        {
            var database=Path.Combine(root,"marketing-queue-"+(index++)+".sqlite");var store=new CompanyStore(database,"owner@example.invalid",CompanyWorkspace.Definitions());
            var department=Apply<CompanyDepartment>(store,Command("department.create") with{Name="Fictional campaign"});
            var employee=Apply<CompanyEmployee>(store,Command("employee.hire") with{DepartmentId=department.Id,RoleId="marketing",Name="Draft fixture",Responsibility="Prepare internal drafts from approved facts"});
            var fields=CompanyWorkspace.Definitions().Single(r=>r.Id=="marketing").OnboardingFields.ToDictionary(f=>f,f=>"Approved fictional "+f);
            var knowledge=Apply<CompanyKnowledge>(store,Command("knowledge.save") with{DepartmentId=department.Id,Body=CompanyJson.Encode(fields)});
            store.Apply(Command("knowledge.review") with{Id=knowledge.Id,ExpectedVersion=knowledge.Version,Digest=knowledge.Digest,Decision="approve"});
            var thread=Items<CompanyThread>(store,"threads").Single();
            var brief=CompanyJson.Encode(new{objective="Introduce fictional paper-crane kits",audience="Fictional hobbyists",channel="Internal draft",deliverable="One headline and short paragraph",constraints="No prices, testimonials or performance claims"});
            var task=Apply<CompanyTask>(store,Command("task.create") with{EmployeeId=employee.Id,ThreadId=thread.Id,Skill="marketing-draft",Body=brief});
            return(store,employee,task,database);
        }
        var good=Setup();var provider=new Provider();var scheduler=new CompanyScheduler(good.Store,provider);
        await scheduler.TickAsync(good.Employee.Id);var attempt=Attempt(good.Store,good.Task.Id);var artifact=Items<CompanyArtifact>(good.Store,"artifacts").Single();
        check(provider.Calls==1&&provider.Call!.Context.Input==good.Task.Input&&provider.Call.Instructions.Contains("Separate draft copy, claim sources, missing facts and owner review questions")&&good.Store.Work(good.Task.Id).State=="awaiting_review","A queued Marketing brief produces a correctly instructed internal draft through the ordinary scheduler.");
        check(attempt.Admission is {Accounting.ApplicationTokens:200,Accounting.RuntimeOverheadTokens:3000}&&attempt.Admission.ContextDigest==CompanyJson.Digest(provider.Call!.Context)&&attempt.Usage==new CompanyTokenUsage(3200,100,10)&&attempt.ReportedUsage==3300&&Budget(good.Store,good.Task).Used==3300,"Admission binds the exact packed context, counts runtime overhead, and settles the full provider input/output receipt.");
        check(artifact.State=="needs_review"&&artifact.Provenance=="model-output","Native-shaped fixture output grants no content approval.");
        artifact=Apply<CompanyArtifact>(good.Store,Command("artifact.review") with{Id=artifact.Id,ExpectedVersion=artifact.Version,Digest=artifact.Digest,Decision="approve"});
        good.Store.Apply(Command("task.complete") with{Id=good.Task.Id,ExpectedVersion=good.Store.Work(good.Task.Id).Version});
        check(good.Store.Work(good.Task.Id).State=="completed"&&artifact.State=="approved","Exact owner content review completes the queued Marketing task without publication.");

        foreach(var overhead in new int?[]{null,4100})
        {
            var fixture=Setup();var blocked=new Provider{Overhead=overhead};await new CompanyScheduler(fixture.Store,blocked).TickAsync(fixture.Employee.Id);
            check(blocked.Calls==0&&fixture.Store.Work(fixture.Task.Id).State=="blocked"&&Budget(fixture.Store,fixture.Task).Reserved==0,"Unknown or excessive runtime overhead blocks before any provider call or reservation.");
        }
        var canceled=Setup();var late=new Provider{Hold=new(TaskCreationOptions.RunContinuationsAsynchronously)};var cancelScheduler=new CompanyScheduler(canceled.Store,late);
        var running=cancelScheduler.TickAsync(canceled.Employee.Id);await late.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        canceled.Store.Apply(Command("task.cancel") with{Id=canceled.Task.Id,ExpectedVersion=canceled.Store.Work(canceled.Task.Id).Version});late.Hold.SetResult(Provider.Reply());await running;
        check(canceled.Store.Work(canceled.Task.Id).State=="canceled"&&Items<CompanyArtifact>(canceled.Store,"artifacts").Length==0&&Budget(canceled.Store,canceled.Task) is {Used:3300,Reserved:0},"A late Marketing response after owner cancellation is withheld while complete actual usage is charged.");

        var interrupted=Setup();var cooperative=new Provider{Hold=new(TaskCreationOptions.RunContinuationsAsynchronously),HonorCancellation=true};
        var interrupting=new CompanyScheduler(interrupted.Store,cooperative).TickAsync(interrupted.Employee.Id);await cooperative.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        interrupted.Store.Apply(Command("task.cancel") with{Id=interrupted.Task.Id,ExpectedVersion=interrupted.Store.Work(interrupted.Task.Id).Version});await interrupting.WaitAsync(TimeSpan.FromSeconds(3));
        check(cooperative.CancellationObserved&&interrupted.Store.Work(interrupted.Task.Id).State=="canceled"&&Budget(interrupted.Store,interrupted.Task).Reserved==5000&&Items<CompanyArtifact>(interrupted.Store,"artifacts").Length==0,"Owner cancellation reaches the active adapter; absent final usage remains held and no draft is published.");

        var unconfirmed=Setup();var stuck=new Provider{Hold=new(TaskCreationOptions.RunContinuationsAsynchronously)};var heldWorker=new CompanyScheduler(unconfirmed.Store,stuck);
        var stopping=heldWorker.TickAsync(unconfirmed.Employee.Id);await stuck.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        unconfirmed.Store.Apply(Command("task.cancel") with{Id=unconfirmed.Task.Id,ExpectedVersion=unconfirmed.Store.Work(unconfirmed.Task.Id).Version});await stopping.WaitAsync(TimeSpan.FromSeconds(11));
        var follow=Apply<CompanyTask>(unconfirmed.Store,Command("task.create") with{EmployeeId=unconfirmed.Employee.Id,ThreadId=unconfirmed.Task.ThreadId,Skill="marketing-draft",Body=unconfirmed.Task.Input});
        await heldWorker.TickAsync(unconfirmed.Employee.Id);
        check(stuck.CancellationObserved&&stuck.Calls==1&&Attempt(unconfirmed.Store,unconfirmed.Task.Id).Error=="provider-termination-unconfirmed"&&unconfirmed.Store.Work(follow.Id).Reason=="provider-unavailable","Unconfirmed adapter termination blocks another queued dispatch rather than releasing account serialization into an overlapping call.");
        stuck.Hold.SetResult(Provider.Reply());for(var i=0;i<100&&Budget(unconfirmed.Store,unconfirmed.Task).Reserved!=0;i++)await Task.Delay(10);
        var reopened=new CompanyStore(unconfirmed.Database,"owner@example.invalid",CompanyWorkspace.Definitions());var afterRestart=new Provider();await new CompanyScheduler(reopened,afterRestart).TickAsync(unconfirmed.Employee.Id);
        check(Budget(reopened,unconfirmed.Task) is {Used:3300,Reserved:0}&&Items<CompanyArtifact>(reopened,"artifacts").Length==0&&afterRestart.Calls==0,"A complete late receipt settles billing without content, while the termination hold survives restart for owner reconciliation.");

        foreach(var mode in new[]{"missing","input-overhead","counter-drift","over-reservation","invalid-text","lost"})
        {
            var fixture=Setup();var failed=new Provider{Mode=mode};var worker=new CompanyScheduler(fixture.Store,failed);await worker.TickAsync(fixture.Employee.Id);await worker.TickAsync(fixture.Employee.Id);
            var budget=Budget(fixture.Store,fixture.Task);var receipt=Attempt(fixture.Store,fixture.Task.Id);
            check(failed.Calls==1&&Items<CompanyArtifact>(fixture.Store,"artifacts").Length==0,"Marketing "+mode+" never creates an accepted draft or automatically replays its call.");
            if(mode=="input-overhead")check(fixture.Store.Work(fixture.Task.Id).State=="failed"&&receipt.Usage==new CompanyTokenUsage(4355,14)&&receipt.ReportedUsage==4369&&budget is {Used:4369,Reserved:0},"Observed native-sized input is withheld and fully accounted, rather than subtracting overhead or discarding known usage.");
            else if(mode=="counter-drift")check(fixture.Store.Work(fixture.Task.Id).State=="failed"&&budget is {Used:3301,Reserved:0},"A provider count that differs from its exact admission is withheld even below the hard limits.");
            else if(mode=="over-reservation")check(fixture.Store.Work(fixture.Task.Id).State=="unknown"&&receipt.ReportedUsage==6100&&receipt.Usage==new CompanyTokenUsage(6000,100)&&budget.Reserved==5000,"Usage above the reservation retains its full receipt and hold for reconciliation; the allowance is not increased.");
            else if(mode=="invalid-text")check(fixture.Store.Work(fixture.Task.Id).State=="failed"&&receipt.ReportedUsage==3300&&budget is {Used:3300,Reserved:0},"Invalid draft text does not discard complete known provider usage.");
            else check(fixture.Store.Work(fixture.Task.Id).State=="unknown"&&budget.Reserved==5000,"Missing or lost Marketing usage remains unknown with its complete reservation held.");
            if(mode is "input-overhead" or "counter-drift" or "over-reservation")
            {
                Apply<CompanyTask>(fixture.Store,Command("task.create") with{EmployeeId=fixture.Employee.Id,ThreadId=fixture.Task.ThreadId,Skill="marketing-draft",Body=fixture.Task.Input});
                await worker.TickAsync(fixture.Employee.Id);check(failed.Calls==1,"A breached input/accounting contract holds future dispatch until reconciliation instead of trusting the same counter again.");
            }
        }
    }
    static CompanyCommand Command(string action)=>new(Guid.NewGuid().ToString("D"),action);
    static T Apply<T>(CompanyStore store,CompanyCommand command)=>CompanyJson.Decode<T>(store.Apply(command));
    static T[] Items<T>(CompanyStore store,string key)=>CompanyJson.Decode<T[]>(JsonSerializer.SerializeToElement(store.Dashboard(),CompanyJson.Options).GetProperty(key).GetRawText());
    static CompanyBudget Budget(CompanyStore store,CompanyTask task)=>Items<CompanyBudget>(store,"budgets").Single(b=>b.Id==task.RootId);
    static CompanyAttempt Attempt(CompanyStore store,string task)=>JsonSerializer.SerializeToElement(store.TaskDetail(task),CompanyJson.Options).GetProperty("attempts").EnumerateArray().Select(a=>CompanyJson.Decode<CompanyAttempt>(a.GetRawText())).Single();
    sealed class Provider:ICompanyModel
    {
        public bool Qualified=>true;public string Status=>"Fictional fixture only; no native runtime.";
        public int? Overhead=3000;public string Mode="success";public int Calls;public CompanyModelCall? Call;public bool HonorCancellation,CancellationObserved;
        public TaskCompletionSource<CompanyModelReply>? Hold;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CompanyInputAccounting CountInput(CompanyModelCall call)=>new(200,Overhead,"fictional-complete-envelope-counter");
        public static CompanyModelReply Reply()=>new("DRAFT for internal review.\nFictional paper-crane kits.\nClaim sources: approved brief.\nMissing facts: price.\nOwner questions: confirm wording.",3300,"fictional-marketing-receipt",new(3200,100,10));
        public async Task<CompanyModelReply> CompleteAsync(CompanyModelCall call,CancellationToken token)
        {
            Calls++;Call=call;using var registration=token.Register(()=>CancellationObserved=true);Entered.TrySetResult();
            if(Hold is not null){try{return HonorCancellation?await Hold.Task.WaitAsync(token):await Hold.Task;}catch(OperationCanceledException) when(token.IsCancellationRequested){CancellationObserved=true;throw;}}
            return Mode switch
            {
                "missing"=>new("Withheld fictional draft",null),
                "input-overhead"=>new("Withheld fictional draft",4369,"fictional-overhead-receipt",new(4355,14)),
                "counter-drift"=>new("Withheld fictional draft",3301,"fictional-drift-receipt",new(3201,100)),
                "over-reservation"=>new("Withheld fictional draft",6100,"fictional-excess-receipt",new(6000,100)),
                "invalid-text"=>Reply() with{Body=""},
                "lost"=>throw new CompanyTransportException("Fictional connection lost after dispatch"),
                _=>Reply()
            };
        }
    }
}
