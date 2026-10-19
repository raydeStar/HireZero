// The execution hold switch (HIREZERO_EXECUTION_HOLD): while it is 1 the workspace refuses to start, so nothing resumes
// until the owner clears it.
public static class ExecutionHold
{
    public const string Variable = "HIREZERO_EXECUTION_HOLD";

    public static bool Requested(string? value) =>
        value switch
        {
            null or "" or "0" => false,
            "1" => true,
            _ => throw new InvalidOperationException(Variable + " must be 1 or 0."),
        };
}
