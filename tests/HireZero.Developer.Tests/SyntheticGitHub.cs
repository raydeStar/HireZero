using HireZero.Core;
using HireZero.Roles.Developer;
using System.Text.Json;

public class SyntheticGitHub:GitHubReviewClient
{
    private string localHead=new('a',40),remoteHead=new('a',40);
    public int Commits{get;private set;}public int Pushes{get;private set;}public int Merges{get;private set;}
    public int Writes=>Commits+Pushes+Merges;public bool Resolved{get;set;}
    public List<string> Resets {get;} = [];
    public bool CiEvidence {get;set;}
    public int LogReads {get;private set;}
    public List<string> ApiReads {get;} = [];
    protected override Task<string> CommandAsync(string executable,string? root,string[] args,string? input,CancellationToken token)
    {
        if(executable=="git")return Task.FromResult(Git(root!,args));
        if(args[0]=="run"){LogReads++;return Task.FromResult("Build\t##[group]Run npm run check\nBuild\t##[error]src/handler.cs:5 error TS2322: synthetic type mismatch\nAPI_KEY=fictional-test-value\n"+string.Concat(Enumerable.Repeat("Build\tdependency detail without an error\n",400))+"Build\tnpm audit fix https://github.com/advisories/GHSA-aaaa-bbbb-cccc\nBuild\tProcess completed with exit code 1.");}
        if(args.Contains("graphql"))return Task.FromResult(Graph());
        var endpoint=args.FirstOrDefault(a=>a=="user"||a.StartsWith("repos/")||a.StartsWith("advisories/")||a.StartsWith("search/"))??throw new InvalidOperationException("Unknown synthetic GitHub request.");
        ApiReads.Add(endpoint);
        object result=endpoint switch{
            "user"=>new{id=1,login="owner"},
            var e when e.StartsWith("search/issues?")=>new{total_count=1,items=new[]{new{number=42,repository_url="https://api.github.com/repos/owner/example",title="Synthetic review"}}},
            "repos/owner/example"=>new{id=10,full_name="owner/example",archived=false,disabled=false,default_branch="main",permissions=new{pull=true,push=true}},
            "repos/owner/example/pulls/42"=>new{title="Concrete failure",body="Narrow null guard.",draft=false,state="open",changed_files=1,mergeable_state="clean",
                head=new{sha=remoteHead,@ref="fix/handler",repo=new{full_name="owner/example",id=10}},labels=Array.Empty<object>()},
            var e when e.Contains("/check-runs?")=>new{total_count=1,check_runs=new[]{new{id=22,name="tests",status="completed",conclusion=Pushes==0?"failure":"success",
                output=new{summary="Compiler failed\nAPI_KEY=fictional-test-value",text="src/handler.cs:5 missing guard"},details_url=CiEvidence?"https://github.com/owner/example/actions/runs/100/job/22":"https://evil.invalid/log"}}},
            var e when e.Contains("/status?")=>new{total_count=0,statuses=Array.Empty<object>()},
            var e when e.EndsWith("/reviews?per_page=100")=>new[]{new{id=2,state="APPROVED",commit_id=remoteHead,user=new{id=2,login="reviewer",type="User"}}},
            var e when e.Contains("/files?")=>new[]{new{filename="src/handler.cs",status="modified",patch="@@ -5 +5 @@\n+ return result.Value;"}},
            var e when e.Contains("/comments?")=>Array.Empty<object>(),
            var e when e.Contains("/annotations?")=>Array.Empty<object>(),
            var e when e.EndsWith("/actions/runs/100")=>new{path=".github/workflows/ci.yml",run_attempt=1,head_sha=new string('a',40)},
            var e when e.Contains("/contents/")=>Content(e),
            var e when e.StartsWith("advisories/")=>new{summary="Synthetic dependency advisory",severity="high",vulnerabilities=new[]{new{package=new{name="next",ecosystem="npm"},vulnerable_version_range="<1.0.1",first_patched_version="1.0.1"}}},
            var e when e.Contains("/branches/")=>new{@protected=false,commit=new{sha=remoteHead}},
            "repos/owner/example/pulls/42/merge"=>Merge(input!),
            _=>throw new InvalidOperationException("Unexpected synthetic GitHub endpoint.")};
        return Task.FromResult(JsonSerializer.Serialize(result));
    }
    private static object Content(string endpoint)
    {
        var source=endpoint.Contains("package-lock.json")?"{\n\"packages\":{\n\"\":{\"dependencies\":{\"next\":\"^1.0.0\"}},\n\"node_modules/next\":{\"version\":\"1.0.0\",\"resolved\":\"https://registry.invalid/next\"}\n}}":
            endpoint.Contains("package.json")?"{\"scripts\":{\"check\":\"tsc --noEmit\"}}":endpoint.Contains(".github/workflows")?"steps:\n  - run: npm run check":"return result.Value;";
        return new{encoding="base64",size=System.Text.Encoding.UTF8.GetByteCount(source),content=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(source))};
    }
    private string Graph()
    {
        var comments=new{pageInfo=new{hasNextPage=false},nodes=new[]{new{id="comment",body="Handle the missing result.",updatedAt="2026-10-01",author=new{login="reviewer"}}}};
        var reviewThreads=new{pageInfo=new{hasNextPage=false},nodes=new[]{new{id="thread",path="src/handler.cs",line=5,isResolved=Resolved,isOutdated=false,comments}}};
        var pullRequest=new{number=42,title="Concrete failure",state="OPEN",headRefOid=remoteHead,author=new{login="owner"},headRepository=new{databaseId=10},reviewThreads};
        return JsonSerializer.Serialize(new{data=new{repository=new{databaseId=10,pullRequest}}});
    }
    private object Merge(string input){using var body=JsonDocument.Parse(input);if(body.RootElement.GetProperty("sha").GetString()!=remoteHead)throw new InvalidOperationException("Missing SHA condition.");Merges++;return new{merged=true};}
    private string Git(string root,string[] args)
    {
        var values=args[0]=="-c"?args.Skip(2).ToArray():args;
        return values[0] switch{
            "rev-parse" when values[1]=="--show-toplevel"=>root,
            "rev-parse" when values[1]=="FETCH_HEAD"=>remoteHead,
            "rev-parse" when values[1]=="HEAD^"=>new string('a',40),
            "rev-parse" when values[1]=="--git-common-dir"=>".git",
            "rev-parse"=>localHead,
            "remote"=>"https://github.com/owner/example.git",
            "status"=>"",
            "fetch"=>"",
            "worktree"=>Checkout(values[3]),
            "ls-files" when values.Contains("-d")=>"",
            "ls-files" when values.Contains("--others")=>"",
            "ls-files"=>"src/handler.cs\0",
            "diff" when values.Contains("--cached")=>Commits==0&&Staged?"src/handler.cs\0":"",
            "diff"=>"+if (result is null) return;",
            "reset"=>Reset(root,"reset"),
            "clean"=>Reset(root,"clean"),
            "add"=>Stage(),
            "commit"=>Commit(),
            "push"=>Push(),
            _=>throw new InvalidOperationException("Unexpected synthetic Git operation.")};
    }
    private string Reset(string root,string kind){Resets.Add(kind+":"+Path.GetFileName(root));return "";}
    private bool Staged{get;set;}
    private string Checkout(string path){Directory.CreateDirectory(Path.Combine(path,"src"));File.WriteAllText(Path.Combine(path,"src/handler.cs"),"if (result is null) return;\n");return "";}
    private string Stage(){Staged=true;return "";}
    private string Commit(){Commits++;localHead=new string('b',40);Staged=false;return "";}
    private string Push(){Pushes++;remoteHead=localHead;return "";}
}
