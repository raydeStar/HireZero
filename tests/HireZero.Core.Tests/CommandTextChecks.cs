using HireZero.Core;

// Plain-language steps from real command shapes Codex and Claude produce.
public static class CommandTextChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        const string pwsh = "\"C:\\Users\\owner\\.dotnet\\tools\\pwsh.exe\" -Command ";
        check(CommandText.Describe(pwsh + "'npm ci'") == "Installing dependencies", "A wrapped npm ci reads as installing dependencies.");
        check(CommandText.Describe(pwsh + "'Get-Content package.json; Get-Content .github/workflows/ci.yml'") == "Reading package.json and ci.yml", "Several file reads become one step naming the files.");
        check(CommandText.Describe(pwsh + "\"node --version; npm --version; Get-Command node,npm\"") == "Checking installed tool versions", "Version checks read as such.");
        check(CommandText.Describe(pwsh + "'$env:npm_config_cache = Join-Path $env:TEMP ''cache''; npm audit --json'") == "Running a security audit", "Environment setup is skipped; the real work is named.");
        check(CommandText.Describe("bash -lc 'npm test -- --run'") == "Running tests", "Tests read as tests through a POSIX shell too.");
        check(CommandText.Describe("rg -n \"MissingResult\" src") == "Searching the code for “MissingResult”", "Searches name what is searched for.");
        check(CommandText.Describe("git status --short") == "Reviewing the changes so far", "git status reads as reviewing changes.");
        check(CommandText.Describe("npm run check && npm test") == "Type-checking (and 1 more)", "Chains name the first step and count the rest.");
        check(CommandText.Describe("dotnet build HireZero.slnx") == "Building the project" && CommandText.Describe("") == "Running a command", "Builds are named; empty commands don't throw.");
        check(CommandText.Finished(pwsh + "'npm ci'", 0) == "✓ Installing dependencies" && CommandText.Finished("npm test", 1) == "✗ Running tests — failed (exit 1)",
            "Finished steps show a check or a cross with the exit code.");
        return Task.CompletedTask;
    }
}
