using Microsoft.Data.Sqlite;

namespace HireZero.Core;

public sealed record CompanyWorkspaceProposal(string AttemptId,string Workspace,string Revision,string Fingerprint,string ContextDigest,string Route,bool AllowWrite=false);
public sealed record CompanyExecutionGrant(string Id,string AttemptId,string TaskId,string EmployeeId,string Workspace,string Revision,string Fingerprint,
    string ContextDigest,string Route,bool AllowWrite,string Digest,string State,DateTimeOffset ExpiresAt,string? RunId=null,int Version=1);
public sealed record CompanyRepositorySnapshot(string Workspace,string Revision,string Fingerprint);
public interface ICompanyWorkspaceInspector {Task<CompanyRepositorySnapshot> InspectAsync(string root,CancellationToken token);}
public interface ICompanyCodingFactory
{
    bool FixtureQualified {get;}
    ICodingRunner Create(CompanyExecutionGrant grant,Func<bool> mayStart);
}

public sealed partial class CompanyStore
{
    public CompanyExecutionGrant ProposeExecution(CompanyWorkspaceProposal proposal)
    {
        using var db=Open();return Transaction(db,()=>
        {
            var a=Need<CompanyAttempt>(db,proposal.AttemptId);if(a.Phase!="claimed"||!Current(db,a)||a.ContextId is null)throw new WorkspaceException("A current admitted attempt is required.");
            var task=Need<CompanyTask>(db,a.TaskId);var employee=Need<CompanyEmployee>(db,a.EmployeeId);var workspace=CanonicalWorkspace(proposal.Workspace);
            if(!Role(employee.RoleId).WorkspaceClaims||!Role(employee.RoleId).ExecutionSkills.Contains(task.Skill)||task.Resource!=workspace||proposal.ContextDigest!=CompanyJson.Digest(Need<CompanyContext>(db,a.ContextId)))throw new WorkspaceException("The workspace or context does not match owner-scoped work.",403);
            Text(proposal.Revision,80);Text(proposal.Fingerprint,80);Text(proposal.Route,100);
            if(All<CompanyExecutionGrant>(db).Any(g=>g.AttemptId==a.Id))throw new WorkspaceException("This attempt already has an execution proposal.");
            var id=Id();var digest=CompanyJson.Digest(proposal);var grant=new CompanyExecutionGrant(id,a.Id,task.Id,employee.Id,workspace,proposal.Revision,proposal.Fingerprint,proposal.ContextDigest,proposal.Route,proposal.AllowWrite,digest,"proposed",Clock().AddMinutes(10));
            Save(db,grant,id,task.Id,employee.Id,grant.State);Event(db,"execution.proposed",id,employee.Id,"An exact workspace execution proposal requires owner approval. Live execution remains unavailable.");return grant;
        });
    }
    private CompanyExecutionGrant ReviewExecution(SqliteConnection db,CompanyCommand command)
    {
        var g=Need<CompanyExecutionGrant>(db,command.Id);Version(g.Version,command.ExpectedVersion);
        if(command.Decision=="revoke")
        {g=g with{State="revoked",Version=g.Version+1};Save(db,g,g.Id,g.TaskId,g.EmployeeId,g.State,g.Version);Stop(db,Need<CompanyTask>(db,g.TaskId),"canceled","Owner revoked the execution grant.");return g;}
        var a=Need<CompanyAttempt>(db,g.AttemptId);
        if(command.Decision!="approve"||g.State!="proposed"||g.Digest!=command.Digest||Clock()>=g.ExpiresAt||!Current(db,a)||a.ContextId is null||g.ContextDigest!=CompanyJson.Digest(Need<CompanyContext>(db,a.ContextId)))throw new WorkspaceException("Approve the exact current, unexpired execution proposal.");
        g=g with{State="approved",Version=g.Version+1};Save(db,g,g.Id,g.TaskId,g.EmployeeId,g.State,g.Version);Event(db,"execution.approved",g.Id,g.EmployeeId,"Owner approved one exact execution scope; the runtime qualification gate still applies.");return g;
    }
    public CompanyExecutionGrant Execution(string id){using var db=Open();return Need<CompanyExecutionGrant>(db,id);}
    public CompanyTask Work(string id){using var db=Open();return Need<CompanyTask>(db,id);}
    public CompanyContext ExecutionContext(string id)
    {using var db=Open();var g=Need<CompanyExecutionGrant>(db,id);var a=Need<CompanyAttempt>(db,g.AttemptId);return Need<CompanyContext>(db,a.ContextId);}
    public bool ExecutionCurrent(string id)
    {
        using var db=Open();var g=Need<CompanyExecutionGrant>(db,id);var a=Need<CompanyAttempt>(db,g.AttemptId);
        return (g.State is "approved" or "dispatched")&&Clock()<g.ExpiresAt&&Current(db,a)&&a.ContextId is not null&&g.ContextDigest==CompanyJson.Digest(Need<CompanyContext>(db,a.ContextId))&&KnowledgeCurrent(db,Need<CompanyContext>(db,a.ContextId),Need<CompanyTask>(db,a.TaskId));
    }
    public bool DispatchExecution(string id)
    {
        using var db=Open();return Transaction(db,()=>
        {
            var g=Need<CompanyExecutionGrant>(db,id);var a=Need<CompanyAttempt>(db,g.AttemptId);
            if(g.State!="approved"||Clock()>=g.ExpiresAt||a.ContextId is null||g.ContextDigest!=CompanyJson.Digest(Need<CompanyContext>(db,a.ContextId))||!Reserve(db,a))return false;
            g=g with{State="dispatched",Version=g.Version+1};Save(db,g,g.Id,g.TaskId,g.EmployeeId,g.State,g.Version);return true;
        });
    }
    public void BindExecution(string id,string runId)
    {
        using var db=Open();Transaction(db,()=>{var g=Need<CompanyExecutionGrant>(db,id);if(g.State!="dispatched"||g.RunId is not null)throw new WorkspaceException("Execution receipt is already bound or no longer current.");Save(db,g with{RunId=Text(runId,80),Version=g.Version+1},g.Id,g.TaskId,g.EmployeeId,g.State,g.Version+1);return true;});
    }
    public void RecordExecutionOutcome(string id,string state)
    {
        if(state is not("completed" or "withheld" or "unknown" or "not_sent"))throw new WorkspaceException("Unsupported execution outcome.");
        using var db=Open();Transaction(db,()=>{var g=Need<CompanyExecutionGrant>(db,id);if(g.State is "completed" or "withheld" or "unknown" or "not_sent" or "revoked")return false;Save(db,g with{State=state,Version=g.Version+1},g.Id,g.TaskId,g.EmployeeId,state,g.Version+1);return true;});
    }
}
