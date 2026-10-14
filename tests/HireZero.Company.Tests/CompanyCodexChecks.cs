using HireZero.Core;
using HireZero.Integrations.Codex;

static class CompanyCodexChecks
{
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        var executable=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../HireZero.FakeCodex/bin/Debug/net10.0/HireZero.FakeCodex.exe"));
        var identity=CompanyCodingFactory.Identity(executable);
        CompanyCodexConnection Connection(string mode="company-success")=>new(new(executable,identity,Path.Combine(root,"codex-text"),SyntheticMode:mode));
        CompanyModelCall Call(string employee,string note,string? history=null)=>new(Guid.NewGuid().ToString("N"),new(Guid.NewGuid().ToString("N"),employee,"developer","thread-"+employee,"Fictional employee","Approved fictional facts","",note,
            history is null?[]:[new("message","thread-"+employee,employee,"owner",history,1)],[],"Reply briefly"),"Fictional text task",4000,1000,"openai/gpt-6-luna");
        var routing=new CompanyModelRouting();check(routing.For(false)=="openai/gpt-6-luna"&&routing.For(true)=="openai/gpt-6.1-sol","Purpose selects Luna text or Sol coding independently of employee role.");
        var connection=Connection();var info=await connection.InspectAsync();check(info.AccountType=="chatgpt"&&info.IncludedUsageAllowed&&info.Models.Contains("gpt-6-luna"),"Actual stdio inspection finds native login, included permission and exact model catalog.");
        check(!connection.Qualified,"No fixture or Codex connection silently qualifies hard token admission.");
        try{connection.CountTokens(Call("a",""));check(false,"No invented token counter.");}catch(ModelUnavailableException){check(true,"Missing tokenizer is reported explicitly.");}
        var sentinel=Environment.GetEnvironmentVariable("HIREZERO_PARENT_SENTINEL");Environment.SetEnvironmentVariable("HIREZERO_PARENT_SENTINEL","PRIVATE_PARENT_VALUE");
        try
        {
            var a=await connection.CompletePilotAsync(Call("a","PRIVATE_ALPHA","ALPHA_HISTORY"));
            var b=await connection.CompletePilotAsync(Call("b","PRIVATE_BETA","BETA_HISTORY"));
            check(a.Body.Contains("PRIVATE_ALPHA")&&!a.Body.Contains("PRIVATE_BETA")&&b.Body.Contains("PRIVATE_BETA")&&!b.Body.Contains("ALPHA_HISTORY"),"Fresh employee threads receive only their admitted notes and history.");
            check(a.ContextId!=b.ContextId&&a.EmployeeId!=b.EmployeeId&&a.Model=="gpt-6-luna"&&a.TotalTokens==120&&a.ReasoningTokens==5,"Receipts bind employee, context, exact model and complete usage.");
            await connection.CompletePilotAsync(Call("a","UPDATED_ALPHA","UPDATED_HISTORY"));await connection.CompletePilotAsync(Call("b","UPDATED_BETA"));
            try{await connection.CompletePilotAsync(Call("a",""));check(false,"Fifth turn must fail.");}catch(WorkspaceException){check(true,"Qualification permits four attempts and no fifth turn.");}
        }
        finally{Environment.SetEnvironmentVariable("HIREZERO_PARENT_SENTINEL",sentinel);}
        foreach(var mode in new[]{"company-apikey","company-quota","company-missing-model","company-ambient","company-tool","company-nontext","company-missing-usage"})
        {
            var refused=Connection(mode);try{await refused.CompletePilotAsync(Call("a","PRIVATE_ALPHA"));check(false,mode+" must fail closed.");}
            catch(Exception e) when(e is WorkspaceException or CompanyTransportException){check(true,mode+" is refused without replay or fallback.");}
            try{await refused.CompletePilotAsync(Call("a",""));check(false,"Refused qualification cannot replay.");}catch(WorkspaceException){check(true,"Refusal closes that qualification instance.");}
        }
        var waiting=Connection("company-wait");using var cancel=new CancellationTokenSource();var pending=waiting.CompletePilotAsync(Call("a",""),cancel.Token);
        var readyDeadline=DateTime.UtcNow.AddSeconds(5);
        while(!Directory.GetFiles(Path.Combine(root,"codex-text"),"fixture-ready",SearchOption.AllDirectories).Any()&&DateTime.UtcNow<readyDeadline)await Task.Delay(20);
        cancel.Cancel();try{await pending;check(false,"Canceled request cannot complete.");}catch(OperationCanceledException){check(true,"Cancellation interrupts only the owned text runtime and withholds output.");}
        try{await Connection().CompletePilotAsync(Call("a","") with{ModelRoute="openai/gpt-6.1-sol"});check(false,"Text route cannot upgrade itself.");}catch(WorkspaceException){check(true,"Coding route requires an explicit execution task.");}
    }
    public static async Task InspectInstalledAsync(string[] args)
    {
        if(args.Length!=3)throw new ArgumentException("Use --codex-inspect or --codex-pilot, executable and expected identity.");
        var root=Path.Combine(Path.GetTempPath(),"hirezero-codex-inspect-"+Guid.NewGuid().ToString("N"));
        var connection=new CompanyCodexConnection(new(args[1],args[2],root));
        var info=await connection.InspectAsync();Console.WriteLine(CompanyJson.Encode(info));
        if(args[0]=="--codex-pilot")
        {
            long total=0;
            for(var i=0;i<4;i++)
            {
                var employee=i%2==0?"fictional-alpha":"fictional-beta";var note=i%2==0?"FICTIONAL_ALPHA":"FICTIONAL_BETA";
                var call=new CompanyModelCall(Guid.NewGuid().ToString("N"),new("fictional-context-"+i,employee,i%2==0?"marketing":"developer","thread-"+employee,"Fictional only","Fictional paper cranes","",note,
                    i<2?[]:[new("fictional-message","thread-"+employee,employee,"owner","Own prior fictional topic: paper cranes",1)],[],"State only your private marker and any supplied prior topic. Do not guess other employees' markers."),"Use only supplied fictional data. Reply in one short sentence.",4000,1000,"openai/gpt-6-luna");
                var receipt=await connection.CompletePilotAsync(call);total+=receipt.TotalTokens;Console.WriteLine(CompanyJson.Encode(receipt));
                if(!receipt.Body.Contains(note)||receipt.Body.Contains(i%2==0?"FICTIONAL_BETA":"FICTIONAL_ALPHA")||total>20000)throw new WorkspaceException("Fictional isolation or observed usage failed; no retry.");
            }
        }
        if(Directory.Exists(root)&&!Directory.EnumerateFileSystemEntries(root).Any())Directory.Delete(root);
    }
}
