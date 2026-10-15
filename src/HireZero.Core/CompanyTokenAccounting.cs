namespace HireZero.Core;

public static class CompanyTokenLimits
{
    public const int Input=4000,Output=1000,Total=Input+Output;
}

// Application instructions/context and runtime-added framing/instructions share the same
// provider input allowance. Unknown overhead is not zero, and observations are not bounds.
public sealed record CompanyInputAccounting(int ApplicationTokens,int? RuntimeOverheadTokens,string CounterIdentity)
{
    public int TotalTokens()
    {
        if(RuntimeOverheadTokens is null)throw new ModelUnavailableException("Runtime input overhead has no compatible exact counter; admission is unavailable.");
        if(ApplicationTokens<0||RuntimeOverheadTokens<0||string.IsNullOrWhiteSpace(CounterIdentity)||CounterIdentity.Length>160||ApplicationTokens>int.MaxValue-RuntimeOverheadTokens.Value)
            throw new WorkspaceException("Invalid complete input accounting.");
        return ApplicationTokens+RuntimeOverheadTokens.Value;
    }
}
public sealed record CompanyInputAdmission(CompanyInputAccounting Accounting,string ContextDigest,string InstructionsDigest);
// Output includes any reasoning tokens. A missing reasoning sub-breakdown is not recorded as zero.
public sealed record CompanyTokenUsage(long InputTokens,long OutputTokens,long? ReasoningTokens=null)
{
    public bool Consistent(long? total)=>InputTokens>=0&&OutputTokens>=0&&(ReasoningTokens is null||ReasoningTokens>=0&&ReasoningTokens<=OutputTokens)&&InputTokens<=long.MaxValue-OutputTokens&&total==InputTokens+OutputTokens;
}
