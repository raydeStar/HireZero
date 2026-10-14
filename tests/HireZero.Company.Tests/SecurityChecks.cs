using System.Text.Json;
using HireZero.Core;

static class SecurityChecks
{
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var store=new CompanyStore(Path.Combine(root,"security.sqlite"),"owner@example.invalid",CompanyWorkspace.Definitions()){Clock=()=>now};
        CompanyCommand C(string action)=>new(Guid.NewGuid().ToString("D"),action);
        T A<T>(CompanyCommand c)=>CompanyJson.Decode<T>(store.Apply(c));
        T[] List<T>(string name)=>CompanyJson.Decode<T[]>(JsonSerializer.SerializeToElement(store.Dashboard(),CompanyJson.Options).GetProperty(name).GetRawText());
        bool Refused(Action action){try{action();return false;}catch(WorkspaceException){return true;}}
        var dep=A<CompanyDepartment>(C("department.create") with{Name="Concurrent fixture department"});
        var hires=await Task.WhenAll(Enumerable.Range(0,2).Select(i=>Task.Run(()=>A<CompanyEmployee>(C("employee.hire") with{DepartmentId=dep.Id,RoleId="developer",Name="Engineer "+i,Responsibility="Isolated fictional work"}))));
        var department=List<CompanyDepartment>("departments").Single();check(hires.Any(e=>e.Id==department.Guide),"Concurrent first hires choose one valid guide.");
        check(List<CompanyEmployee>("employees").All(e=>e.State=="onboarding"),"Concurrent hires do not bypass owner onboarding.");
        var fields=CompanyWorkspace.Definitions().Single(r=>r.Id=="developer").OnboardingFields.ToDictionary(f=>f,f=>"Approved fixture "+f);
        var draft=A<CompanyKnowledge>(C("knowledge.save") with{DepartmentId=dep.Id,Body=CompanyJson.Encode(fields)});
        var knowledge=A<CompanyKnowledge>(C("knowledge.review") with{Id=draft.Id,ExpectedVersion=draft.Version,Digest=draft.Digest,Decision="approve"});
        var threads=List<CompanyThread>("threads");var e1=hires[0];var e2=hires[1];var t1=threads.Single(t=>t.EmployeeId==e1.Id);var t2=threads.Single(t=>t.EmployeeId==e2.Id);
        CompanyTask Submit(CompanyEmployee e,CompanyThread t,string body="Fixture work")=>A<CompanyTask>(C("task.create") with{EmployeeId=e.Id,ThreadId=t.Id,Skill="chat",Body=body});
        CompanyTask Work(string id)=>List<CompanyTask>("tasks").Single(t=>t.Id==id);
        var task=Submit(e1,t1);var claims=await Task.WhenAll(Task.Run(()=>store.Claim(e1.Id,true)),Task.Run(()=>store.Claim(e1.Id,true)));
        check(claims.Count(a=>a is not null)==1,"Two database claimants cannot own one employee concurrently.");
        var a=claims.Single(a=>a is not null)!;store.Capture(a.Id);
        check(Refused(()=>store.AcquireWorkspace(a.Id,Path.Combine(root,"unapproved-checkout"))),"A workspace claim requires explicit owner scope.");
        store.Apply(C("task.cancel") with{Id=task.Id,ExpectedVersion=Work(task.Id).Version});check(!store.AuthorizeDispatch(a.Id),"Cancel before dispatch invalidates the attempt fence.");
        var waiting1=Submit(e1,t1);var waiting2=Submit(e2,t2);var fake=new FakeModel{Hold=new(TaskCreationOptions.RunContinuationsAsynchronously)};var scheduler=new CompanyScheduler(store,fake);
        var run1=scheduler.TickAsync(e1.Id);await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));var run2=scheduler.TickAsync(e2.Id);
        for(var i=0;i<100&&Work(waiting2.Id).State!="running";i++)await Task.Delay(10);
        store.Apply(C("task.cancel") with{Id=waiting2.Id,ExpectedVersion=Work(waiting2.Id).Version});var held=fake.Hold;fake.Hold=null;held!.SetResult(new("First completed fixture",100));await Task.WhenAll(run1,run2);
        check(fake.Calls.Count==1&&Work(waiting2.Id).State=="canceled","Cancel while waiting for the principal gate prevents the second model call.");
        var pinned=Submit(e2,t2);var pinnedAttempt=store.Claim(e2.Id,true)!;var context=store.Capture(pinnedAttempt.Id);
        var newer=A<CompanyKnowledge>(C("knowledge.save") with{DepartmentId=dep.Id,Body=CompanyJson.Encode(fields.ToDictionary(k=>k.Key,k=>"New "+k.Value))});
        check(context.DepartmentKnowledge==knowledge.Body,"Draft knowledge is not inherited by admitted work.");
        A<CompanyKnowledge>(C("knowledge.review") with{Id=newer.Id,ExpectedVersion=newer.Version,Digest=newer.Digest,Decision="approve"});
        check(store.AuthorizeDispatch(pinnedAttempt.Id),"Ordinary knowledge publication does not rewrite an admitted task.");
        store.Apply(C("knowledge.review") with{Id=knowledge.Id,ExpectedVersion=knowledge.Version,Digest=knowledge.Digest,Decision="revoke"});
        store.Finish(pinnedAttempt.Id,new("REVOKED_OUTPUT_SENTINEL",100));check(!List<CompanyArtifact>("artifacts").Any(x=>x.TaskId==pinned.Id),"Revocation after dispatch withholds the response.");
        var uncertain=Submit(e2,t2);var ua=store.Claim(e2.Id,true)!;store.Capture(ua.Id);check(store.AuthorizeDispatch(ua.Id)&&!store.AuthorizeDispatch(ua.Id),"A dispatch reserves exactly once.");
        store.FailAttempt(ua.Id,"synthetic timeout",true);
        check(Refused(()=>store.Apply(C("task.retry") with{Id=uncertain.Id,ExpectedVersion=Work(uncertain.Id).Version})),"Unknown retry requires explicit uncertainty acknowledgement.");
        var retry=A<CompanyTask>(C("task.retry") with{Id=uncertain.Id,ExpectedVersion=Work(uncertain.Id).Version,Decision="acknowledge-uncertain"});
        var ra=store.Claim(e2.Id,true)!;store.Capture(ra.Id);check(retry.RootId==uncertain.RootId&&!store.AuthorizeDispatch(ra.Id),"Fresh attempt retains root budget and cannot spend an uncertain reservation twice.");store.FailAttempt(ra.Id,"budget-unavailable",false);
        var lead=A<CompanyDepartment>(C("department.create") with{Name="Leadership fixture"});var ceo=A<CompanyEmployee>(C("employee.hire") with{DepartmentId=lead.Id,RoleId="ceo",Name="Chair",Responsibility="Facilitate"});
        var leadFields=CompanyWorkspace.Definitions().Single(r=>r.Id=="ceo").OnboardingFields.ToDictionary(f=>f,f=>"Approved "+f);var lk=A<CompanyKnowledge>(C("knowledge.save") with{DepartmentId=lead.Id,Body=CompanyJson.Encode(leadFields)});store.Apply(C("knowledge.review") with{Id=lk.Id,ExpectedVersion=lk.Version,Digest=lk.Digest,Decision="approve"});
        var ceoThread=List<CompanyThread>("threads").Single(t=>t.EmployeeId==ceo.Id);var ceoTask=Submit(ceo,ceoThread);var ca=store.Claim(ceo.Id,true)!;var cc=store.Capture(ca.Id);
        check(cc.Colleagues?.Length==3&&cc.CompanyWork is not null,"Facilitator gets a scoped roster and status metadata.");
        store.FailAttempt(ca.Id,"not-sent-fixture",false);
        var meeting=A<CompanyMeeting>(C("meeting.create") with{Name="Cancellation fixture",Body="Review selected facts",Participants=[e1.Id,ceo.Id],ChairId=ceo.Id,Budget=20000});
        meeting=A<CompanyMeeting>(C("meeting.start") with{Id=meeting.Id,ExpectedVersion=meeting.Version});
        var ma=store.Claim(e1.Id,true)!;store.Capture(ma.Id);check(store.AuthorizeDispatch(ma.Id),"Meeting reserves before dispatch.");
        meeting=A<CompanyMeeting>(C("meeting.pause") with{Id=meeting.Id,ExpectedVersion=meeting.Version});
        check(Refused(()=>store.Apply(C("meeting.next") with{Id=meeting.Id,ExpectedVersion=meeting.Version})),"A paused, canceled in-flight turn cannot be silently replayed.");
        store.Finish(ma.Id,new("Late meeting reply withheld",100));meeting=A<CompanyMeeting>(C("meeting.next") with{Id=meeting.Id,ExpectedVersion=meeting.Version});
        var expires=store.Claim(e1.Id,true)!;store.Capture(expires.Id);now=now.AddMinutes(16);check(!store.AuthorizeDispatch(expires.Id),"Meeting deadline is enforced after waiting for dispatch.");store.FailAttempt(expires.Id,"expired",false);
        check(Refused(()=>store.Apply(C("plan.create") with{EmployeeId=ceo.Id,Body="Too many tasks",Budget=40000,Tasks=Enumerable.Range(0,5).Select(i=>new CompanyTaskSpec(e1.Id,t1.Id,"chat","Unique "+i,[],[])).ToArray()})),"Delegation task count is bounded server-side.");
        var raw=CompanyJson.Encode(C("department.create") with{Name="No authority"});check(!raw.Contains("approvedBy"),"Authority is not an accepted command field.");
        var cursor=store.Feed(0,1).Single().Sequence;check(store.Feed(cursor,100).All(e=>e.Sequence>cursor),"Feed cursor resumes strictly after the last event.");
        var retired=List<CompanyEmployee>("employees").Single(e=>e.Id==e2.Id);store.Apply(C("employee.lifecycle") with{Id=e2.Id,ExpectedVersion=retired.Version,Decision="retire"});
        check(Refused(()=>store.Apply(C("notes.save") with{EmployeeId=e2.Id,Body="Forbidden edit"})),"Retired private notes remain read-only.");
        var quota=List<CompanyBudget>("budgets").Single(b=>b.Id=="employee:"+e1.Id);
        store.Apply(C("budget.configure") with{Id=quota.Id,ExpectedVersion=quota.Version,Budget=quota.Used+quota.Reserved});
        Submit(e1,t1,"Employee allowance fixture");var qa=store.Claim(e1.Id,true)!;store.Capture(qa.Id);check(!store.AuthorizeDispatch(qa.Id),"Employee allowance is enforced in the same reservation transaction.");store.FailAttempt(qa.Id,"employee-allowance",false);
        Submit(ceo,ceoThread,"Independent department fixture");var independent=store.Claim(ceo.Id,true)!;store.Capture(independent.Id);check(store.AuthorizeDispatch(independent.Id),"An exhausted employee allowance does not block an unrelated department.");store.FailAttempt(independent.Id,"definitively-not-sent-fixture",false);
    }
}
