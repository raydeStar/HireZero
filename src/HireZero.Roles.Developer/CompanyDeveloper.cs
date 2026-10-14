namespace HireZero.Roles.Developer;

public static class CompanyDeveloper
{
    public static CompanyRole Definition => new("developer","Developer",
        "Prepare internal engineering advice and implementation proposals using only supplied context. Repository descriptions do not grant access. No code runner, shell, network or publishing tools are available. Mark unverified claims and missing evidence.",
        ["chat","developer-advice","developer-work","meeting-turn"],["purpose","architecture","standards","acceptance","constraints"]) {WorkspaceClaims=true,ExecutionSkills=["developer-work"]};
}
