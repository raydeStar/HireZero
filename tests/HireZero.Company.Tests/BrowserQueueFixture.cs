using HireZero.Core;

// Test executable only. The provider is unconditionally the compiled fake stdio runtime.
static class BrowserQueueFixture
{
    public static async Task RunAsync(string[] args)
    {
        if(args.Length is <2 or >4)throw new ArgumentException("Select one disposable browser test root.");
        var root=Path.GetFullPath(args[1]);var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if(Path.GetDirectoryName(root)!=parent||!Path.GetFileName(root).StartsWith("hirezero-manual-ui-",StringComparison.Ordinal))throw new ArgumentException("Browser fixture requires its disposable temporary root.");
        Environment.SetEnvironmentVariable(HeldWorkspace.Variable,"0");
        var mode=Environment.GetEnvironmentVariable("HIREZERO_BROWSER_SYNTHETIC_MODE")??"company-measured-success";
        if(mode is not ("company-measured-success" or "company-conversation-browser"))throw new ArgumentException("Choose a compiled browser fixture mode.");
        var model=args[0]=="--company-browser-fixture"?null:MeasuredQueueChecks.Model(root,mode);
        await using var app=CompanyWorkspace.TryCreate([CompanyWorkspace.Flag],"owner@example.invalid",true,Path.Combine(root,"company"),fixture:model,fixtureUrl:args.Length>=3?args[2]:"http://127.0.0.1:5065",fixtureWebRoot:args.Length==4?args[3]:null)!;
        if(model is null){Console.WriteLine("BROWSER FIXTURE: unavailable provider; zero live inference or credentials.");await app.RunAsync();return;}
        var store=app.Services.GetRequiredService<CompanyStore>();
        T Apply<T>(CompanyCommand c)=>CompanyJson.Decode<T>(store.Apply(c));
        var department=Apply<CompanyDepartment>(MeasuredQueueChecks.C("department.create") with{Name="Fictional browser studio"});
        Apply<CompanyEmployee>(MeasuredQueueChecks.C("employee.hire") with{DepartmentId=department.Id,RoleId="marketing",Name="Browser draft fixture",Responsibility="Prepare internal copy from approved fictional facts"});
        var fields=new{purpose="Fictional paper crane kit",audience="Adult hobbyists",positioning="Paper craft kit",voice="Clear and warm",claims="Contains paper and a printed folding guide",constraints="No prices, proof, performance claims, publishing or outreach"};
        var knowledge=Apply<CompanyKnowledge>(MeasuredQueueChecks.C("knowledge.save") with{DepartmentId=department.Id,Body=CompanyJson.Encode(fields)});
        store.Apply(MeasuredQueueChecks.C("knowledge.review") with{Id=knowledge.Id,ExpectedVersion=knowledge.Version,Digest=knowledge.Digest,Decision="approve"});
        var engineering=Apply<CompanyDepartment>(MeasuredQueueChecks.C("department.create") with{Name="Fictional browser engineering"});
        Apply<CompanyEmployee>(MeasuredQueueChecks.C("employee.hire") with{DepartmentId=engineering.Id,RoleId="developer",Name="Browser Developer fixture",Responsibility="Prepare isolated implementation proposals from owner-approved shared facts"});
        var engineeringFields=CompanyWorkspace.Definitions().Single(r=>r.Id=="developer").OnboardingFields.ToDictionary(k=>k,k=>"Fictional approved "+k);
        var engineeringKnowledge=Apply<CompanyKnowledge>(MeasuredQueueChecks.C("knowledge.save") with{DepartmentId=engineering.Id,Body=CompanyJson.Encode(engineeringFields)});
        store.Apply(MeasuredQueueChecks.C("knowledge.review") with{Id=engineeringKnowledge.Id,ExpectedVersion=engineeringKnowledge.Version,Digest=engineeringKnowledge.Digest,Decision="approve"});
        Console.WriteLine("BROWSER FIXTURE: fake provider only; zero live inference or credentials.");
        await app.RunAsync();
    }
}
