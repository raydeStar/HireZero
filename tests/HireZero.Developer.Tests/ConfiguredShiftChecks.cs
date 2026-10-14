using HireZero.Core;
using HireZero.Roles.Developer;

public static class ConfiguredShiftChecks
{
    sealed class Fake : GitHubReviewClient
    {
        public readonly List<string> ReviewReads = [], IssueReads = [], EvidenceReads = [];
        public Action? DuringEvidence; public bool FailFirst;
        public override Task<GitHubIdentity> IdentityAsync(CancellationToken token) => Task.FromResult(new GitHubIdentity(1,"owner"));
        public override Task<CodeReview[]> ReviewsAsync(ReviewRepository repository, GitHubIdentity account, CancellationToken token)
        {
            ReviewReads.Add(repository.Name); if (FailFirst && repository.Name == "company/blocked") throw new ReviewAccessException("Synthetic access denied",403);
            return Task.FromResult(new[] { new CodeReview(42,"Fictional task",new string('a',40),"OWNER",true,10,true,[],repository.Name),
                new CodeReview(43,"Someone else's task",new string('a',40),"coworker",true,10,true,[],repository.Name) });
        }
        public override Task<AssignedIssue[]> AssignedIssuesAsync(ReviewRepository repository, GitHubIdentity account, CancellationToken token)
        { IssueReads.Add(repository.Name); return Task.FromResult(new[] { new AssignedIssue(8,"Fictional assigned task",DateTimeOffset.UtcNow.ToString("O")) }); }
        public override Task<PullRequestEvidence> EvidenceAsync(string repository, int number, bool logs, CancellationToken token)
        {
            EvidenceReads.Add(repository); DuringEvidence?.Invoke();
            return Task.FromResult(new PullRequestEvidence(repository,10,number,"Fictional task",new string('a',40),"owner",true,false,repository,10,"fix","main","clean",true,
                [new PullRequestCheck("tests","failure",null)],[],[],[],[],[],[]));
        }
    }
    sealed class IssueClient : GitHubReviewClient
    {
        public int Reads; public string? Endpoint;
        protected override Task<string> CommandAsync(string executable,string? root,string[] args,string? input,CancellationToken token)
        {
            Reads++; Endpoint=args[^1];
            if(!args.Contains("GET"))throw new Exception("Mutation is forbidden");
            object Item(int number,string repository,string who,string title="Fictional task",bool closed=false) => new {
                number,repository_url="https://api.github.com/repos/"+repository,assignees=new[]{new{login=who}},title,state=closed?"closed":"open",updated_at="2026-01-01T00:00:00Z",body="Must never be propagated"};
            return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new{items=new[]{Item(1,"company/example","OWNER"),Item(2,"other/example","owner"),
                Item(3,"company/example","coworker"),Item(4,"company/example","owner","password=fictionalsecret"),Item(5,"company/example","owner",closed:true)}}));
        }
    }
    public static async Task RunAsync(Action<bool,string> check)
    {
        var path=Path.Combine(Path.GetTempPath(),"configured-shift-"+Guid.NewGuid().ToString("N")+".sqlite");
        try
        {
            var store=new GitHubReviewStore(path); var fake=new Fake(); var agents=new AgentStore(path); var owner=new WorkspaceOwner("owner@example.invalid");
            var proposals=new ShiftProposalStore(path); var now=DateTimeOffset.UtcNow;
            agents.SaveShift(new(true,"manual",now.ToString("O"),now.AddHours(1).ToString("O"),null,null,0,6,null,null));
            var sensor=new GitHubShiftSensor(fake,store,proposals,agents,owner);
            var empty=await sensor.SenseAsync(default);
            check(empty.Work.Length==0 && fake.ReviewReads.Count==0 && fake.IssueReads.Count==0,"No configured repositories means no broad organization or account PR search.");
            var repository=new ReviewRepository("allowed",1,10,"company/example","","fictional");store.Add(repository);
            store.Add(repository with{Id="duplicate",RootPath="another-fictional-root"});
            store.Add(repository with{Id="other-account",AccountId=2,Name="outside/example"});
            var found=await sensor.SenseAsync(default);
            check(found.Work.Length==1 && fake.ReviewReads.SequenceEqual(new[]{"company/example"}) && fake.EvidenceReads.SequenceEqual(new[]{"company/example"}),
                "An explicitly registered company repository qualifies for its owner's PR, duplicate checkouts dedupe and other authors/accounts are excluded.");
            check(proposals.Tasks().Length==0 && fake.IssueReads.Count==0 && agents.OpenTasks().Length==0,
                "Assigned-issue discovery is omitted: no issue reads, proposal publication or model dispatch occur.");
            check(found.Note.Contains("assigned-issue discovery is omitted"), "The omitted source is disclosed in visible shift progress.");
            store.Add(repository with{Id="blocked",Name="company/blocked",GitHubId=20});fake.FailFirst=true;
            var partial=await sensor.SenseAsync(default);
            check(partial.Work.Length==1 && partial.Note.Contains("1 checks unavailable"),"One repository failure preserves useful permitted findings and reports partial coverage.");
            fake.DuringEvidence=()=>{store.Remove("allowed");store.Remove("duplicate");};
            var revoked=await sensor.SenseAsync(default);
            check(revoked.Work.Length==0 && proposals.Tasks().Length==0 && fake.IssueReads.Count==0,"Removed repository scope cannot publish PR observations or invoke the omitted issue collector.");
            var client=new IssueClient();var issues=await client.AssignedIssuesAsync(repository,new(1,"owner"),default);
            check(issues.Length==1 && issues[0].Number==1 && client.Endpoint!.Contains("repo%3Acompany%2Fexample") && client.Endpoint.Contains("assignee%3Aowner"),
                "Assigned-task reads use exact repo and account-assignee filters, reject off-scope/closed/secret records and never propagate issue bodies.");
            try{await client.AssignedIssuesAsync(repository,new(2,"owner"),default);check(false,"Wrong account must fail.");}
            catch(ReviewAccessException){check(client.Reads==1,"An account mismatch is rejected before any provider call.");}
        }
        finally{File.Delete(path);}
    }
}
