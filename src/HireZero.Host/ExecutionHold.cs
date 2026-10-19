// The execution hold switch (HIREZERO_EXECUTION_HOLD): while it is 1 the workspace refuses to start, so nothing resumes
// until the owner clears it. CompanySettings reads and checks it.
public static class ExecutionHold
{
    public const string Variable = "HIREZERO_EXECUTION_HOLD";
}
