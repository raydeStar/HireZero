using System.Text.Json;
var mode=Environment.GetEnvironmentVariable("HIREZERO_FIXTURE_MODE")??"success";
const string thread="fixture-thread",turn="fixture-turn";
void Write(object value){Console.WriteLine(JsonSerializer.Serialize(value));Console.Out.Flush();}
void Complete(string status="completed",string? id=turn,string? threadId=thread)=>Write(new{method="turn/completed",@params=new{threadId,turn=new{id,status,error=status=="failed"?new{message="Synthetic worker failure"}:null}}});
void Stale(string? threadId,string? turnId){
 Write(new{method="item/agentMessage/delta",@params=new{threadId,turnId,delta="Stale answer."}});
 foreach(var method in new[]{"item/started","item/completed"})Write(new{method,@params=new{threadId,turnId,item=new{type="commandExecution",command="echo fictional stale evidence",exitCode=0}}});
 Write(new{method="item/completed",@params=new{threadId,turnId,item=new{type="fileChange",path="fictional-stale.txt"}}});
 Write(new{method="thread/tokenUsage/updated",@params=new{threadId,turnId,tokenUsage=new{total=new{totalTokens=1234}}}});
 Write(new{method="error",@params=new{threadId,turnId,message="Stale error"}});
}
while(await Console.In.ReadLineAsync() is {} line){using var doc=JsonDocument.Parse(line);var r=doc.RootElement;var method=r.TryGetProperty("method",out var raw)?raw.GetString():null;
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
