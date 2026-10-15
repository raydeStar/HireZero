using System.Text.Json;
var mode=Environment.GetEnvironmentVariable("HIREZERO_FIXTURE_MODE")??"success";
const string thread="fixture-thread",turn="fixture-turn";
var companyTurnSent=false;var companyConversationTurn=false;
const string companyProfile="hirezero_qualification";
const string removedReadAccess="readOnly.access is no longer supported; use permissionProfile for restricted reads";
void Write(object value){Console.WriteLine(JsonSerializer.Serialize(value));Console.Out.Flush();}
string? CompanyPermissionError(JsonElement p) {
 if(p.TryGetProperty("sandboxPolicy",out var policy)&&policy.TryGetProperty("access",out var access)&&access.GetProperty("type").GetString()=="restricted")return "Invalid request: "+removedReadAccess;
 if(p.TryGetProperty("permissions",out _)&&p.TryGetProperty("sandboxPolicy",out _))return "`permissions` cannot be combined with `sandboxPolicy`";
 return null;
}
void Complete(string status="completed",string? id=turn,string? threadId=thread)=>Write(new{method="turn/completed",@params=new{threadId,turn=new{id,status,error=status=="failed"?new{message="Synthetic worker failure"}:null}}});
void Stale(string? threadId,string? turnId){
 Write(new{method="item/agentMessage/delta",@params=new{threadId,turnId,delta="Stale answer."}});
 foreach(var method in new[]{"item/started","item/completed"})Write(new{method,@params=new{threadId,turnId,item=new{type="commandExecution",command="echo fictional stale evidence",exitCode=0}}});
 Write(new{method="item/completed",@params=new{threadId,turnId,item=new{type="fileChange",path="fictional-stale.txt"}}});
 Write(new{method="thread/tokenUsage/updated",@params=new{threadId,turnId,tokenUsage=new{total=new{totalTokens=1234}}}});
 Write(new{method="error",@params=new{threadId,turnId,message="Stale error"}});
}
while(await Console.In.ReadLineAsync() is {} line){using var doc=JsonDocument.Parse(line);var r=doc.RootElement;var method=r.TryGetProperty("method",out var raw)?raw.GetString():null;
 if(mode.StartsWith("company-",StringComparison.Ordinal)){
  if(method=="initialize")Write(new{id=r.GetProperty("id").GetInt32(),result=new{}});
  else if(method=="account/read")Write(new{id=r.GetProperty("id").GetInt32(),result=new{account=new{type=mode=="company-apikey"?"apiKey":"chatgpt",planType="pro"},requiresOpenaiAuth=true}});
  else if(method=="account/rateLimits/read"){
   if(mode=="company-postbilling-refusal"&&companyTurnSent)Write(new{id=r.GetProperty("id").GetInt32(),error=new{code=-32000,message="Fictional private billing diagnostic"}});
   else Write(new{id=r.GetProperty("id").GetInt32(),result=new{ordinaryUsageAllowed=mode!="company-quota"&&!(mode=="company-drain"&&companyTurnSent),rateLimits=new{spendControlReached=false,rateLimitReachedType=(string?)null,credits=new{hasCredits=true,balance=mode=="company-credit-change"&&companyTurnSent?"9.0":"10.0"},primary=new{usedPercent=34}}}});
  }
  else if(method=="model/list")Write(new{id=r.GetProperty("id").GetInt32(),result=new{data=(mode=="company-missing-model"?new[]{"gpt-6.1-sol"}:new[]{"gpt-6-luna","gpt-6.1-sol"}).Select(model=>new{id=model,model}),nextCursor=(string?)null}});
  else if(method=="config/read"){
   var work=Directory.GetCurrentDirectory();
   var expected="permissions={"+companyProfile+"={filesystem={"+JsonSerializer.Serialize(work)+"=\"read\"},network={enabled=false}}}";
   if(!args.Contains(expected)||!args.Contains("default_permissions=\""+companyProfile+"\""))throw new Exception("Missing process-local restricted profile");
   var filesystem=new Dictionary<string,string>{{work,"read"}};
   if(mode=="company-broad-permissions")filesystem["fictional-broader-root"]="read";
    Write(new{id=r.GetProperty("id").GetInt32(),result=new{config=new{windows=mode=="company-windows-disabled"?null:new{sandbox=mode=="company-windows-unelevated"?"unelevated":mode=="company-windows-elevated"?"elevated":"mxc"},default_permissions=companyProfile,permissions=new Dictionary<string,object>{{companyProfile,new{extends=mode=="company-inherited-profile"?":read-only":(string?)null,filesystem,network=new{enabled=mode=="company-network"}}}}}}});
  }
  else if(method=="permissionProfile/list")Write(new{id=r.GetProperty("id").GetInt32(),result=new{data=new[]{new{id=companyProfile,allowed=mode!="company-disallowed-profile"}},nextCursor=(string?)null}});
  else if(method=="thread/start"){
   var p=r.GetProperty("params");
   companyConversationTurn=p.TryGetProperty("developerInstructions",out var instructions)&&instructions.GetString()!.Contains("Conversational reply only.",StringComparison.Ordinal);
   if(p.GetProperty("model").GetString()!="gpt-6-luna"||p.GetProperty("allowProviderModelFallback").GetBoolean()||!p.GetProperty("ephemeral").GetBoolean()||p.GetProperty("environments").GetArrayLength()!=0||p.GetProperty("approvalPolicy").GetString()!="never"||p.TryGetProperty("sandbox",out _)||p.TryGetProperty("permissionProfile",out _)||p.GetProperty("permissions").GetString()!=companyProfile)throw new Exception("Invalid company thread envelope");
   Write(new{id=r.GetProperty("id").GetInt32(),result=new{thread=new{id=thread,ephemeral=true},model="gpt-6-luna",modelProvider="openai",approvalPolicy="never",activePermissionProfile=new{id=mode=="company-wrong-profile"?":read-only":companyProfile,extends=(string?)null},instructionSources=mode=="company-ambient"?new[]{"fictional/global/AGENTS.md"}:Array.Empty<string>(),sandbox=new{type="readOnly",networkAccess=false}}});
  }
  else if(method=="turn/start"){
   var p=r.GetProperty("params");
   if(CompanyPermissionError(p) is {} error){Write(new{id=r.GetProperty("id").GetInt32(),error=new{code=-32600,message=error}});continue;}
   if(mode=="company-protocol-validation"){Write(new{id=r.GetProperty("id").GetInt32(),result=new{valid=true}});continue;}
   companyTurnSent=true;
   File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(),"fixture-last-context.json"),p.GetProperty("input")[0].GetProperty("text").GetString());
   File.AppendAllText(Path.Combine(Directory.GetCurrentDirectory(),"fixture-native-starts"),"start\n");
   if(mode=="company-start-refusal"){Write(new{id=r.GetProperty("id").GetInt32(),error=new{code=-32602,message="Fictional private diagnostic must not be exported"}});continue;}
   if(mode is "company-removed-access" or "company-diagnostic-suffix"){Write(new{id=r.GetProperty("id").GetInt32(),error=new{code=-32600,message="Invalid request: "+removedReadAccess+(mode=="company-diagnostic-suffix"?" PRIVATE_DIAGNOSTIC_SUFFIX":""),data="PRIVATE_ERROR_DATA"}});continue;}
   if(p.GetProperty("model").GetString()!="gpt-6-luna"||p.GetProperty("effort").GetString()!="low"||p.GetProperty("summary").GetString()!="none"||p.GetProperty("serviceTierForTurn").GetString()!="default"||p.TryGetProperty("sandboxPolicy",out _)||p.TryGetProperty("permissionProfile",out _)||p.GetProperty("permissions").GetString()!=companyProfile||p.GetProperty("environments").GetArrayLength()!=0||p.GetProperty("runtimeWorkspaceRoots").GetArrayLength()!=0)throw new Exception("Invalid company turn envelope");
   if(Environment.GetEnvironmentVariable("HIREZERO_PARENT_SENTINEL") is not null||Environment.GetEnvironmentVariable("OPENAI_API_KEY") is not null)throw new Exception("Ambient secret environment leaked");
   Write(new{id=r.GetProperty("id").GetInt32(),result=new{turn=new{id=turn}}});
   if(mode=="company-conversation-browser"&&p.GetProperty("input")[0].GetProperty("text").GetString()!.Contains("[wait]",StringComparison.Ordinal)||mode is "company-wait" or "company-measured-late" or "company-measured-partial" or "company-measured-stale-partial" or "company-measured-queued-partial"){if(mode is "company-measured-partial" or "company-measured-stale-partial")Write(new{method="thread/tokenUsage/updated",@params=new{threadId=thread,turnId=turn,tokenUsage=new{total=new{inputTokens=4800,outputTokens=20,reasoningOutputTokens=5,totalTokens=4820}}}});if(mode=="company-conversation-browser"){Write(new{method="item/agentMessage/delta",@params=new{threadId=thread,turnId=turn,delta="UNCONFIRMED_PARTIAL_REPLY_SHOULD_NOT_RENDER"}});File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(),"fixture-partial-emitted"),"sent");}File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(),"fixture-ready"),"ready");continue;}
   if(mode=="company-tool"){Write(new{id=901,method="item/commandExecution/requestApproval",@params=new{threadId=thread,turnId=turn}});continue;}
   using var context=JsonDocument.Parse(p.GetProperty("input")[0].GetProperty("text").GetString()!);
   var body=context.RootElement.GetProperty("notes").GetString()+"|"+context.RootElement.GetProperty("history").GetRawText();
   if(mode.StartsWith("company-measured",StringComparison.Ordinal))body="DRAFT for internal review\nFold a fictional paper crane.\nThis fictional kit contains paper and a printed folding guide.\nClaim sources: approved facts. Missing facts: price. Owner question: approve wording?";
   if(companyConversationTurn)body="Fictional conversational reply. Use only the reviewed facts. The human owner retains final authority; no actions were performed.";
   if(mode=="company-measured-characters")body=new string('x',2001);
   if(mode=="company-nontext")Write(new{method="item/started",@params=new{threadId=thread,turnId=turn,item=new{type="fileChange"}}});
   Write(new{method="item/agentMessage/delta",@params=new{threadId=thread,turnId=turn,delta=body}});
   if(mode!="company-missing-usage"){var measured=mode.StartsWith("company-measured",StringComparison.Ordinal)||mode=="company-conversation-browser";var input=mode=="company-observed-overrun"?6000:mode=="company-measured-overrun"?9000:measured?4800:100;var output=mode=="company-measured-output"?1200:measured?150:20;var usage=new Dictionary<string,object>{{"inputTokens",input},{"outputTokens",output},{"totalTokens",input+output+(mode=="company-measured-inconsistent"?1:0)}};if(mode!="company-measured-no-reasoning")usage["reasoningOutputTokens"]=5;Write(new{method="thread/tokenUsage/updated",@params=new{threadId=thread,turnId=turn,tokenUsage=new{total=usage}}});}
   Complete();
  }
  else if(method=="turn/interrupt"){if(mode=="company-measured-queued-partial")Write(new{method="thread/tokenUsage/updated",@params=new{threadId=thread,turnId=turn,tokenUsage=new{total=new{inputTokens=4800,outputTokens=20,reasoningOutputTokens=5,totalTokens=4820}}}});Write(new{id=r.GetProperty("id").GetInt32(),result=new{}});if(mode=="company-measured-late")Write(new{method="thread/tokenUsage/updated",@params=new{threadId=thread,turnId=turn,tokenUsage=new{total=new{inputTokens=4800,outputTokens=150,reasoningOutputTokens=5,totalTokens=4950}}}});if(mode=="company-measured-partial")return;Complete(mode=="company-measured-late"?"completed":"interrupted");return;}
  else if(method is null&&r.TryGetProperty("id",out var denied)&&denied.GetInt32()==901){if(!r.TryGetProperty("error",out _))throw new Exception("A tool grant was created");return;}
  continue;
 }
 if(mode.StartsWith("scoped-",StringComparison.Ordinal)){
  var home=Environment.GetEnvironmentVariable("CODEX_HOME")??throw new Exception("Scoped home missing");
  if(Environment.GetEnvironmentVariable("HIREZERO_PARENT_SENTINEL") is not null||File.Exists(Path.Combine(home,"auth.json")))throw new Exception("Ambient authentication or environment escaped into scoped runtime");
  File.AppendAllText(Path.Combine(home,"fixture-wire.jsonl"),line+"\n");
  if(method=="thread/resume")throw new Exception("Scoped thread cannot resume");
  if(method=="thread/start"&&!r.GetProperty("params").GetProperty("ephemeral").GetBoolean())throw new Exception("Scoped thread must be ephemeral");
  if(method=="turn/start"){
   var p=r.GetProperty("params");if(p.GetProperty("approvalPolicy").GetString()!="never"||p.GetProperty("model").GetString()!="gpt-6.1-sol")throw new Exception("Scoped route or approval policy changed");
   var sandbox=p.GetProperty("sandboxPolicy");if(sandbox.GetProperty("type").GetString()=="workspaceWrite"&&(sandbox.GetProperty("networkAccess").GetBoolean()||sandbox.GetProperty("writableRoots").GetArrayLength()!=1||sandbox.GetProperty("writableRoots")[0].GetString()!=Directory.GetCurrentDirectory()))throw new Exception("Scoped sandbox expanded");
  }
 }
 if(method=="initialize")Write(new{id=r.GetProperty("id").GetInt32(),result=new{}});
 else if(method is "thread/start" or "thread/resume"){
  if(mode=="model-name"&&method=="thread/start"&&r.GetProperty("params").GetProperty("model").GetString()!="gpt-6.1-sol")throw new Exception("The thread received a provider-qualified model instead of its native ID.");
  Write(new{id=r.GetProperty("id").GetInt32(),result=new{thread=new{id=thread}}});
 }
 else if(method=="turn/start"){
  if(mode=="model-name"&&r.GetProperty("params").GetProperty("model").GetString()!="gpt-6.1-sol")throw new Exception("The turn received a provider-qualified model instead of its native ID.");
  Write(new{id=r.GetProperty("id").GetInt32(),result=new{turn=new{id=turn,model=mode=="model-name"||mode.StartsWith("scoped-")?"gpt-6.1-sol":"fixture"}}});
  if(mode is "scoped-success" or "scoped-missing-usage"){
   if(r.GetProperty("params").GetProperty("sandboxPolicy").GetProperty("type").GetString()=="workspaceWrite")File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(),"fixture-change.txt"),"Fictional scoped change");
   Write(new{method="item/agentMessage/delta",@params=new{threadId=thread,turnId=turn,delta="Fictional scoped coding result"}});
   if(mode!="scoped-missing-usage")Write(new{method="thread/tokenUsage/updated",@params=new{threadId=thread,turnId=turn,tokenUsage=new{total=new{totalTokens=321}}}});
   Complete();return;
  }
  if(mode=="scoped-wait"){File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME")!,"fixture-ready"),"ready");continue;}
  if(mode=="early-exit")return;
  if(mode=="hang"){File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(),"fixture-hang.pid"),Environment.ProcessId.ToString());continue;}
  if(mode=="approval"){Write(new{id=900,method="item/commandExecution/requestApproval",@params=new{threadId="other-thread",turnId="old-turn"}});continue;}
  if(mode=="bad-status"){Complete("inProgress");return;}
  if(mode=="failed"){Complete("failed");return;}
  if(mode=="missing-id"){Complete(id:null);return;}
  if(mode=="missing-thread"){Complete(threadId:null);return;}
  if(mode=="stale-turn")Complete(id:"old-turn");
  if(mode=="stale-events"){Stale(thread,"old-turn");Stale("other-thread",turn);Complete(threadId:"other-thread");}
  if(mode=="unscoped-events")Stale(null,null);
  Write(new{method="item/agentMessage/delta",@params=new{threadId=thread,turnId=turn,delta="Fictional answer"}});Complete();return;
 }
 else if(method=="turn/interrupt"){Write(new{id=r.GetProperty("id").GetInt32(),result=new{}});Complete("interrupted");return;}
 else if(method is null&&r.TryGetProperty("id",out var id)&&id.GetInt32()==900){if(r.GetProperty("result").GetProperty("decision").GetString()!="decline")throw new Exception("Fixture received an unexpected grant");Complete();return;}
}
