using HireZero.Core;

// Test executable only. The provider is unconditionally the compiled fake stdio runtime.
static class BrowserQueueFixture
{
    public static async Task RunAsync(string[] args)
    {
        if(args.Length!=2)throw new ArgumentException("Select one disposable browser test root.");
        var root=Path.GetFullPath(args[1]);var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if(Path.GetDirectoryName(root)!=parent||!Path.GetFileName(root).StartsWith("hirezero-manual-ui-",StringComparison.Ordinal))throw new ArgumentException("Browser fixture requires its disposable temporary root.");
        Environment.SetEnvironmentVariable(HeldWorkspace.Variable,"0");
        var model=MeasuredQueueChecks.Model(root,"company-measured-success");
        await using var app=CompanyWorkspace.TryCreate([CompanyWorkspace.Flag],"owner@example.invalid",true,Path.Combine(root,"company"),fixture:model,fixtureUrl:"http://127.0.0.1:5065")!;
        var store=app.Services.GetRequiredService<CompanyStore>();
        T Apply<T>(CompanyCommand c)=>CompanyJson.Decode<T>(store.Apply(c));
        var department=Apply<CompanyDepartment>(MeasuredQueueChecks.C("department.create") with{Name="Fictional browser studio"});
        Apply<CompanyEmployee>(MeasuredQueueChecks.C("employee.hire") with{DepartmentId=department.Id,RoleId="marketing",Name="Browser draft fixture",Responsibility="Prepare internal copy from approved fictional facts"});
        var fields=new{purpose="Fictional paper crane kit",audience="Adult hobbyists",positioning="Paper craft kit",voice="Clear and warm",claims="Contains paper and a printed folding guide",constraints="No prices, proof, performance claims, publishing or outreach"};
        var knowledge=Apply<CompanyKnowledge>(MeasuredQueueChecks.C("knowledge.save") with{DepartmentId=department.Id,Body=CompanyJson.Encode(fields)});
        store.Apply(MeasuredQueueChecks.C("knowledge.review") with{Id=knowledge.Id,ExpectedVersion=knowledge.Version,Digest=knowledge.Digest,Decision="approve"});
        Console.WriteLine("BROWSER FIXTURE: fake provider only; zero live inference or credentials.");
        await app.RunAsync();
    }
}
