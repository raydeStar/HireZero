using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Codex;

public sealed record CompanyCodexOptions(string Executable,string Identity,string ScratchRoot,string? Home=null,string? SyntheticMode=null,decimal QualificationCreditBudget=0);
public sealed record CompanyCodexBilling(bool? IncludedUsageAllowed,bool? HasCredits,string? CreditBalance,bool? SpendControlReached,string? RateLimitReachedType,int? PrimaryUsedPercent,DateTimeOffset ObservedAt);
public sealed record CompanyCodexInspection(string? AccountType,string? Plan,bool IncludedUsageAllowed,string[] Models,CompanyCodexBilling? Billing=null);
public sealed record CompanyCodexReceipt(string EmployeeId,string ContextId,string Model,string ThreadId,string TurnId,string Body,long InputTokens,long OutputTokens,long ReasoningTokens,long TotalTokens,
    CompanyCodexBilling? Before=null,CompanyCodexBilling? After=null,string? StopReason=null);

// Native app-server sign-in reuse only. No credential files, bearer tokens, account changes or daemon control.
// The normal company scheduler requires hard token admission. Codex's public turn API does not provide it.
// This connection therefore exposes a separate, four-turn qualification API, never claims normal qualification.
public sealed class CompanyCodexConnection(CompanyCodexOptions options,CompanyModelRouting? routing=null) : ICompanyModel
{
    private const string PermissionProfile="hirezero_qualification";
    private readonly SemaphoreSlim _serial=new(1,1);
    private readonly CompanyModelRouting _routing=routing??new();
    private int _sent;
    private bool _stopped;
    // Conservative admission reserve: (1.05M input * 2.5 + 128k output * 12.5 credits/M)
    // * (4 HTTP retries + initial) * (5 stream retries + initial) = 126.75; rounded to 150.
    // Published Standard Codex rates and retry defaults; not a backend monetary-cap parameter.
    public const decimal CreditExposureReservePerTurn=150;
    public bool Qualified=>false;
    public string Status=>"Codex selected. The bounded qualification connection uses native sign-in. Automatic work remains blocked: exact input counting and upstream hard token caps are unavailable.";
    public int CountTokens(CompanyModelCall call)=>throw new ModelUnavailableException("Codex app-server has no compatible exact input counter; character counts are not token bounds.");
    public Task<CompanyModelReply> CompleteAsync(CompanyModelCall call,CancellationToken token)=>throw new ModelUnavailableException(Status);
    public async Task<CompanyCodexInspection> InspectAsync(CancellationToken token=default)
    {
        await _serial.WaitAsync(token);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try{await using var rpc=Start();return await Inspect(rpc,deadline.Token);}
        finally{_serial.Release();}
    }
    private async Task<CompanyCodexInspection> Inspect(Rpc rpc,CancellationToken token)
    {
        _routing.Validate();await rpc.Initialize(token);
        var account=await rpc.Request("account/read",new{refreshToken=false},token);
        string? type=null,plan=null;
        if(account.TryGetProperty("account",out var a)&&a.ValueKind==JsonValueKind.Object){type=Text(a,"type");plan=Text(a,"planType");}
        var list=await rpc.Request("model/list",new{limit=100,includeHidden=true},token);
        if(list.TryGetProperty("nextCursor",out var cursor)&&cursor.ValueKind!=JsonValueKind.Null)throw new WorkspaceException("Model catalog requires another page; qualification refuses incomplete discovery.");
        var models=list.GetProperty("data").EnumerateArray().Select(m=>Text(m,"model")??Text(m,"id")??"").ToArray();
        var allowed=false;CompanyCodexBilling? billing=null;
        if(type=="chatgpt")
        {
            var limits=await rpc.Request("account/rateLimits/read",new{supportsLunaReserve=false,excludeResetCreditDetails=true},token);
            allowed=limits.TryGetProperty("ordinaryUsageAllowed",out var ordinary)&&ordinary.ValueKind==JsonValueKind.True;
            billing=Billing(limits);
        }
        return new(type,plan,allowed,models,billing);
    }
    // Caller supplies fictional data only. This is an explicit qualification entry point, never a worker fallback.
    // Four attempts maximum per connection; any uncertain outcome permanently closes this qualification instance.
    // Byte/character/deadline checks below are conservative local controls, NOT upstream token/spending ceilings.
    public async Task<CompanyCodexReceipt> CompletePilotAsync(CompanyModelCall call,CancellationToken token=default)
    {
        var input=CompanyJson.Encode(call.Context);
        if(call.ModelRoute is not null&&call.ModelRoute!=_routing.TextModel)throw new WorkspaceException("Text dispatch cannot select the coding route.");
        if(call.Instructions.Length>1000||Encoding.UTF8.GetByteCount(input)>4000||call.Context.Input.Length>1000||string.IsNullOrWhiteSpace(call.Context.EmployeeId)||call.Context.Id.Length==0)
            throw new WorkspaceException("Narrow the fictional qualification context.");
        await _serial.WaitAsync(token);string? thread=null,turn=null;var started=false;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(60));
        Rpc? rpc=null;
        try
        {
            if(_stopped||_sent>=4)throw new WorkspaceException("Qualification stopped or its four-turn allowance is exhausted.");
            if(options.QualificationCreditBudget is <=0 or >1000||(_sent+1)*CreditExposureReservePerTurn>options.QualificationCreditBudget)
                throw new WorkspaceException("The approved qualification credit reserve cannot cover another in-flight turn.");
            rpc=Start();
            var inspection=await Inspect(rpc,deadline.Token);
            var model=CompanyModelRouting.Native(_routing.TextModel);
            if(inspection.AccountType!="chatgpt"||!inspection.IncludedUsageAllowed||inspection.Billing is {SpendControlReached:true}||inspection.Billing?.RateLimitReachedType is not null)throw new WorkspaceException("Existing ChatGPT sign-in with included usage permission is required. No API-key or provider fallback.");
            if(!inspection.Models.Contains(model)||!inspection.Models.Contains(CompanyModelRouting.Native(_routing.CodingModel)))throw new WorkspaceException("A selected model is absent from this account's catalog; no substitution.");
            await rpc.VerifyPermissions(deadline.Token);
            var response=await rpc.Request("thread/start",new{model,modelProvider="openai",cwd=rpc.WorkingDirectory,approvalPolicy="never",permissions=PermissionProfile,ephemeral=true,
                allowProviderModelFallback=false,environments=Array.Empty<object>(),runtimeWorkspaceRoots=Array.Empty<string>(),serviceName="hirezero_qualification",
                baseInstructions="You are a text-only employee assistant. Use only the supplied context. Never call tools, access files, retrieve other history or perform actions. Reply in at most 80 words.",
                developerInstructions=call.Instructions},deadline.Token);
            if(Text(response,"model")!=model||Text(response,"modelProvider")!="openai"||
               !response.TryGetProperty("instructionSources",out var sources)||sources.GetArrayLength()!=0||
               response.GetProperty("thread").GetProperty("ephemeral").ValueKind!=JsonValueKind.True||
               !response.TryGetProperty("activePermissionProfile",out var active)||Text(active,"id")!=PermissionProfile||Text(active,"extends") is not null||
               Text(response,"approvalPolicy")!="never"||
               Text(response.GetProperty("sandbox"),"type")!="readOnly"||response.GetProperty("sandbox").GetProperty("networkAccess").ValueKind!=JsonValueKind.False)
                throw new WorkspaceException("Codex thread isolation is unconfirmed: exact route, ephemeral state, read-only sandbox and zero ambient instruction sources are required. No model turn was sent.");
            thread=response.GetProperty("thread").GetProperty("id").GetString()??throw new WorkspaceException("Missing thread identity.");
            _sent++;started=true;
            var result=await rpc.Request("turn/start",new{threadId=thread,model,effort="low",summary="none",approvalPolicy="never",environments=Array.Empty<object>(),runtimeWorkspaceRoots=Array.Empty<string>(),
                serviceTierForTurn="default",permissions=PermissionProfile,
                input=new[]{new{type="text",text=input}}},deadline.Token);
            turn=result.GetProperty("turn").GetProperty("id").GetString()??throw new WorkspaceException("Missing turn identity.");
            var output=new StringBuilder();CompanyCodexReceipt? receipt=null;
            while(true)
            {
                var e=await rpc.Read(deadline.Token);var method=Text(e,"method");
                if(e.TryGetProperty("id",out var requestId))
                {await rpc.Deny(requestId,deadline.Token);throw new CompanyTransportException("Runtime requested a tool, approval or authentication change; qualification stopped.");}
                if(method=="error")throw new CompanyTransportException("Codex reported an error; no replay.");
                if(!e.TryGetProperty("params",out var p))continue;
                if(method=="account/rateLimits/updated"&&(Flag(p,"ordinaryUsageAllowed")==false||Billing(p) is {SpendControlReached:true}||Billing(p).RateLimitReachedType is not null))
                    throw new CompanyTransportException("Runtime reported quota or spending exhaustion; no continuation or replay.");
                if(method=="account/updated"&&Text(p,"authMode") is {} mode&&mode!="chatgpt")throw new CompanyTransportException("Runtime account mode changed; no provider fallback.");
                if(Text(p,"threadId")!=thread||Text(p,"turnId") is {} eventTurn&&eventTurn!=turn)continue;
                if(method is "item/started" or "item/completed")
                {
                    var item=p.GetProperty("item");var kind=Text(item,"type");
                    if(kind is not("agentMessage" or "reasoning" or "userMessage"))throw new CompanyTransportException("A non-text runtime item was withheld; qualification stopped.");
                    if(method=="item/completed"&&kind=="agentMessage"&&output.Length==0)output.Append(Text(item,"text"));
                }
                if(method=="item/agentMessage/delta")output.Append(Text(p,"delta"));
                if(output.Length>2000)throw new CompanyTransportException("Local output character allowance exceeded; qualification stopped.");
                if(method=="thread/tokenUsage/updated")
                {
                    var usage=p.GetProperty("tokenUsage").GetProperty("total");
                    var i=Number(usage,"inputTokens");var o=Number(usage,"outputTokens");var r=Number(usage,"reasoningOutputTokens");var total=Number(usage,"totalTokens");
                    if(total!=i+o||r>o)throw new CompanyTransportException("Complete runtime usage is inconsistent.");
                    receipt=new(call.Context.EmployeeId,call.Context.Id,model,thread,turn,"",i,o,r,total);
                }
                if(method=="turn/completed")
                {
                    var completed=p.GetProperty("turn");if(Text(completed,"id")!=turn)continue;
                    if(Text(completed,"status")!="completed"||receipt is null||output.Length==0)throw new CompanyTransportException("Completion or complete usage was not confirmed.");
                    if(receipt.TotalTokens>5000)throw new CompanyTransportException("Observed usage exceeded the soft 5,000-token threshold; reconcile before further work.");
                    var after=Billing(await rpc.Request("account/rateLimits/read",new{supportsLunaReserve=false,excludeResetCreditDetails=true},deadline.Token));
                    var stop=after.IncludedUsageAllowed!=true||after.SpendControlReached==true||after.RateLimitReachedType is not null?"Included usage is exhausted or unavailable.":
                        inspection.Billing?.CreditBalance is {} beforeBalance&&after.CreditBalance is {} afterBalance&&decimal.TryParse(beforeBalance,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var beforeValue)&&decimal.TryParse(afterBalance,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var afterValue)&&beforeValue!=afterValue?"Observed credit balance changed; reconcile account activity before continuing.":null;
                    if(stop is not null)_stopped=true;
                    return receipt with{Body=output.ToString(),Before=inspection.Billing,After=after,StopReason=stop};
                }
            }
        }
        catch
        {
            _stopped=true;
            if(rpc is not null&&started&&thread is not null&&turn is not null)
            {
                using var grace=new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try{await rpc.Request("turn/interrupt",new{threadId=thread,turnId=turn},grace.Token);}catch{ }
            }
            throw;
        }
        finally{try{if(rpc is not null)await rpc.DisposeAsync();}finally{_serial.Release();}}
    }
    private Rpc Start()
    {
        if(!Path.IsPathFullyQualified(options.Executable)||CompanyCodingFactory.Identity(options.Executable)!=options.Identity)throw new WorkspaceException("Pinned Codex executable identity changed.");
        var root=Path.GetFullPath(options.ScratchRoot);
        for(var p=new DirectoryInfo(root);p is not null;p=p.Parent)if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new WorkspaceException("Runtime scratch cannot traverse a reparse point.");
        Directory.CreateDirectory(root);var work=Path.Combine(root,"text-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        return new(options,work);
    }
    private static long Number(JsonElement j,string key)=>j.TryGetProperty(key,out var value)&&value.TryGetInt64(out var n)&&n>=0?n:throw new CompanyTransportException("Complete runtime usage is missing.");
    private static bool? Flag(JsonElement j,string key)=>j.ValueKind==JsonValueKind.Object&&j.TryGetProperty(key,out var value)?value.ValueKind==JsonValueKind.True?true:value.ValueKind==JsonValueKind.False?false:null:null;
    private static CompanyCodexBilling Billing(JsonElement j)
    {
        var limits=j.TryGetProperty("rateLimitsByLimitId",out var buckets)&&buckets.ValueKind==JsonValueKind.Object&&buckets.TryGetProperty("codex",out var codex)?codex:j.TryGetProperty("rateLimits",out var legacy)?legacy:default;
        var credits=limits.ValueKind==JsonValueKind.Object&&limits.TryGetProperty("credits",out var c)&&c.ValueKind==JsonValueKind.Object?c:default;
        int? used=limits.ValueKind==JsonValueKind.Object&&limits.TryGetProperty("primary",out var primary)&&primary.ValueKind==JsonValueKind.Object&&primary.TryGetProperty("usedPercent",out var percent)&&percent.TryGetInt32(out var n)?n:null;
        return new(Flag(j,"ordinaryUsageAllowed"),Flag(credits,"hasCredits"),Text(credits,"balance"),Flag(limits,"spendControlReached"),Text(limits,"rateLimitReachedType"),used,DateTimeOffset.UtcNow);
    }
    private static string? Text(JsonElement j,string key)=>j.ValueKind==JsonValueKind.Object&&j.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    private sealed class Rpc : IAsyncDisposable
    {
        private readonly Process _process;private int _id;
        private readonly Queue<JsonElement> _events=new();
        public string WorkingDirectory {get;}
        public Rpc(CompanyCodexOptions options,string work)
        {
            WorkingDirectory=work;var info=new ProcessStartInfo(options.Executable){WorkingDirectory=work,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            var environment=info.Environment.Where(x=>new[]{"PATH","SystemRoot","WINDIR","USERPROFILE","HOMEDRIVE","HOMEPATH","APPDATA","LOCALAPPDATA","TEMP","TMP","CODEX_HOME"}.Contains(x.Key,StringComparer.OrdinalIgnoreCase)).ToArray();
            info.Environment.Clear();foreach(var (key,value) in environment)info.Environment[key]=value;
            if(options.Home is not null)info.Environment["CODEX_HOME"]=Path.GetFullPath(options.Home);
            if(options.SyntheticMode is not null)info.Environment["HIREZERO_FIXTURE_MODE"]=options.SyntheticMode;
            foreach(var a in Arguments(work))info.ArgumentList.Add(a);
            _process=Process.Start(info)??throw new WorkspaceException("Codex did not start.");
            _process.ErrorDataReceived+=(_,_)=>{};_process.BeginErrorReadLine();
        }
        private static IEnumerable<string> Arguments(string work)
        {
            yield return "app-server";yield return "--stdio";yield return "--strict-config";
            // The pinned native deserializer rejects legacy readOnly.access, even though the
            // generated schema permits unknown properties. Configure restricted reads by name.
            yield return "-c";yield return "permissions={"+PermissionProfile+"={filesystem={"+JsonSerializer.Serialize(work)+"=\"read\"},network={enabled=false}}}";
            yield return "-c";yield return "default_permissions=\""+PermissionProfile+"\"";
            foreach(var value in new[]{"model_provider=\"openai\"","forced_login_method=\"chatgpt\"","mcp_servers={}","plugins={}","project_doc_max_bytes=0","developer_instructions=\"\"","include_environment_context=false","web_search=\"disabled\"","skills.bundled.enabled=false","skills.include_instructions=false","memories.generate_memories=false","memories.use_memories=false","tools.update_plan.enabled=false","tools.experimental_request_user_input.enabled=false"}){yield return "-c";yield return value;}
            foreach(var feature in new[]{"shell_tool","shell_snapshot","apps","plugins","remote_plugin","memories","multi_agent","hooks","computer_use","browser_use","image_generation","view_image","request_permissions_tool","skill_search","goals","code_mode_host","workspace_dependencies","sleep_tool","unbounded_connection_retries"}){yield return "--disable";yield return feature;}
        }
        public async Task Initialize(CancellationToken token)
        {await Request("initialize",new{clientInfo=new{name="hirezero_company",version="0.1.0"},capabilities=new{experimentalApi=true}},token);await Write(new{method="initialized",@params=new{}},token);}
        public async Task VerifyPermissions(CancellationToken token)
        {
            var config=(await Request("config/read",new{cwd=WorkingDirectory,includeLayers=false},token)).GetProperty("config");
            if(Text(config,"default_permissions")!=PermissionProfile||!config.TryGetProperty("permissions",out var profiles)||
               !profiles.TryGetProperty(PermissionProfile,out var profile)||profile.ValueKind!=JsonValueKind.Object||
               profile.EnumerateObject().Any(p=>p.Name is not("filesystem" or "network")&&p.Value.ValueKind!=JsonValueKind.Null)||
               !profile.TryGetProperty("filesystem",out var fs)||fs.ValueKind!=JsonValueKind.Object||
               fs.EnumerateObject().Count(p=>p.Value.ValueKind!=JsonValueKind.Null)!=1||Text(fs,WorkingDirectory)!="read"||
               !profile.TryGetProperty("network",out var network)||Flag(network,"enabled")!=false||
               network.EnumerateObject().Any(p=>p.Name!="enabled"&&p.Value.ValueKind!=JsonValueKind.Null))
                throw new WorkspaceException("Codex restricted permissions are unconfirmed: only the empty scratch root may be read, with no network or inherited profile.");
            var available=await Request("permissionProfile/list",new{cwd=WorkingDirectory,limit=100},token);
            if(available.TryGetProperty("nextCursor",out var cursor)&&cursor.ValueKind!=JsonValueKind.Null||
               !available.GetProperty("data").EnumerateArray().Any(p=>Text(p,"id")==PermissionProfile&&Flag(p,"allowed")==true))
                throw new WorkspaceException("Codex did not allow the selected restricted permissions profile. No model turn was sent.");
        }
        private async Task Write(object value,CancellationToken token)=>await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(),token);
        public async Task<JsonElement> Request(string method,object parameters,CancellationToken token)
        {
            var id=++_id;await Write(new{id,method,@params=parameters},token);
            while(true)
            {
                var j=await ReadWire(token);
                if(j.TryGetProperty("id",out var n)&&n.TryGetInt32(out var value)&&value==id)
                {if(j.TryGetProperty("error",out var error)){
                    var code=error.TryGetProperty("code",out var rawCode)&&rawCode.TryGetInt32(out var errorCode)?errorCode.ToString(System.Globalization.CultureInfo.InvariantCulture):"unknown";
                    throw new WorkspaceException("Codex refused the supported protocol request: "+method+" (code "+code+"). "+SafeDiagnostic(error)+" No automatic replay.");
                }return j.GetProperty("result").Clone();}
                if(j.TryGetProperty("id",out var serverId)){await Deny(serverId,token);throw new WorkspaceException("Codex requested an unapproved action during preflight.");}
                if(_events.Count>=256)throw new WorkspaceException("Too many runtime events.");_events.Enqueue(j);
            }
        }
        private static string SafeDiagnostic(JsonElement error)
        {
            // Emit only exact public protocol literals, never arbitrary server text or error.data.
            var message=Text(error,"message");
            foreach(var known in new[]{"readOnly.access is no longer supported; use permissionProfile for restricted reads",
                "workspaceWrite.readOnlyAccess is no longer supported; use permissionProfile for restricted reads",
                "`permissions` cannot be combined with `sandboxPolicy`","`permissions` cannot be combined with `sandbox`"})
                if(message==known||message=="Invalid request: "+known)return known+".";
            return "Server message/details withheld.";
        }
        public Task Deny(JsonElement id,CancellationToken token)=>Write(new{id=id.Clone(),error=new{code=-32601,message="HireZero qualification declines all tools, approvals and authentication changes."}},token);
        public Task<JsonElement> Read(CancellationToken token)=>_events.Count>0?Task.FromResult(_events.Dequeue()):ReadWire(token);
        private async Task<JsonElement> ReadWire(CancellationToken token)
        {
            var line=await _process.StandardOutput.ReadLineAsync(token)??throw new CompanyTransportException("Codex exited without a confirmed outcome.");
            if(line.Length>131072)throw new CompanyTransportException("Runtime event size exceeded its local allowance.");
            using var document=JsonDocument.Parse(line);return document.RootElement.Clone();
        }
        public async ValueTask DisposeAsync()
        {
            try{if(!_process.HasExited)_process.Kill(entireProcessTree:true);await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}
            finally{_process.Dispose();if(Directory.Exists(WorkingDirectory)&&!Directory.EnumerateFileSystemEntries(WorkingDirectory).Any())Directory.Delete(WorkingDirectory);}
        }
    }
}
