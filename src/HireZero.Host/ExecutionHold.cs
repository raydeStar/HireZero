// The execution hold switch (HIREZERO_EXECUTION_HOLD): 1 starts the held profile, which resumes nothing until the owner
// verifies; the company workspace refuses to start while it is set.
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
