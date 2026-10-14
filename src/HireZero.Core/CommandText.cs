using System.Text.RegularExpressions;

namespace HireZero.Core;

// Turns a coding agent's shell command into a plain-language step ("Installing dependencies", "Reading package.json").
// The raw command is kept separately for anyone who wants the detail.
public static class CommandText
{
    static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);
    static readonly Regex Shell = new(@"^\s*(?:""[^""]*?(?:pwsh|powershell)(?:\.exe)?""|\S*?(?:pwsh|powershell)(?:\.exe)?)\s+(?:-\w+\s+)*?-(?:Command|c)\s+(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline, Limit);
    static readonly Regex Posix = new(@"^\s*(?:\S*/)?(?:bash|sh|zsh|cmd(?:\.exe)?)\s+(?:-l?c|/c)\s+(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline, Limit);
    static readonly Regex Statements = new(@";|&&|\|\||\r?\n", RegexOptions.None, Limit);
    static readonly Regex FileToken = new(@"[\w.\-/\\]+\.(json|ya?ml|ts|tsx|js|mjs|cjs|cs|csproj|md|txt|toml|lock|xml|config|ps1|sh|py|html|css|env\.example)\b", RegexOptions.IgnoreCase, Limit);
    static readonly Regex Quoted = new(@"['""]([^'""]{2,40})['""]", RegexOptions.None, Limit);

    public static string Describe(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "Running a command";
        try
        {
            var inner = Unwrap(command.Trim());
            var labels = Statements.Split(inner).Select(part => part.Trim()).Where(Significant).Select(Label).Where(label => label.Length > 0).Distinct().ToList();
            if (labels.Count == 0) return "Preparing the environment";
            var reads = labels.Where(label => label.StartsWith("Reading ", StringComparison.Ordinal)).ToList();
            if (reads.Count == labels.Count && reads.Count > 1)
            {
                var files = reads.SelectMany(label => label["Reading ".Length..].Split(", ")).Distinct().ToList();
                return "Reading " + Join(files);
            }
            return labels[0] + (labels.Count > 1 ? " (and " + (labels.Count - 1) + " more)" : "");
        }
        catch (RegexMatchTimeoutException) { return "Running a command"; }
    }

    // "Running tests" -> "Ran tests"-style outcomes stay simple: a check or a cross plus the step.
    public static string Finished(string? command, int? exitCode) =>
        exitCode switch { 0 => "✓ " + Describe(command), null => Describe(command) + " (finished)", _ => "✗ " + Describe(command) + " — failed (exit " + exitCode + ")" };

    static string Unwrap(string command)
    {
        for (var depth = 0; depth < 2; depth++)
        {
            var match = Shell.Match(command); if (!match.Success) match = Posix.Match(command);
            if (!match.Success) break;
            command = match.Groups[1].Value.Trim();
            if (command.Length >= 2 && (command[0] == '\'' || command[0] == '"') && command[^1] == command[0]) command = command[1..^1];
            command = command.Replace("''", "'");
        }
        return command;
    }

    static bool Significant(string statement) =>
        statement.Length > 0 && !Regex.IsMatch(statement, @"^(\$env:|\$[\w:]+\s*=|set\s+\w+=|export\s|cd\s|set-location|push-location|pop-location|\$ErrorActionPreference|\[Console\]|chcp)", RegexOptions.IgnoreCase, Limit);

    static string Label(string statement)
    {
        var s = statement.ToLowerInvariant();
        bool Has(string pattern) => Regex.IsMatch(s, pattern, RegexOptions.None, Limit);
        if (Has(@"^(npm|pnpm|yarn)\s+audit\s+fix\b")) return "Fixing vulnerable dependencies";
        if (Has(@"^(npm|pnpm|yarn)\s+audit\b")) return "Running a security audit";
        if (Has(@"^(npm|pnpm|yarn)\s+(ci|install|i|add)\b|^dotnet\s+restore\b|^pip\s+install\b")) return "Installing dependencies";
        if (Has(@"\b(vitest|jest|playwright\s+test|pytest|go\s+test|cargo\s+test|node\s+--test)\b|^(npm|pnpm|yarn)\s+(run\s+)?test\b|^dotnet\s+test\b|^dotnet\s+run\s+--project\s+\S*tests?")) return "Running tests";
        if (Has(@"\btsc\b.*--noemit|^(npm|pnpm|yarn)\s+run\s+(check|typecheck)\b")) return "Type-checking";
        if (Has(@"^(npm|pnpm|yarn)\s+run\s+lint\b|\beslint\b")) return "Linting";
        if (Has(@"^(npm|pnpm|yarn)\s+run\s+build\b|^dotnet\s+build\b|\bvite\s+build\b|^\s*tsc\b")) return "Building the project";
        if (Has(@"^(npm|pnpm|yarn)\s+(outdated|ls|list|view|explain|why|info)\b")) return "Checking dependency versions";
        if (Regex.Match(s, @"^(?:npm|pnpm|yarn)\s+run\s+([\w:-]+)", RegexOptions.None, Limit) is { Success: true } script) return "Running the " + script.Groups[1].Value + " script";
        if (Has(@"^git\s+(status|diff)\b")) return "Reviewing the changes so far";
        if (Has(@"^git\s+(log|show|blame)\b")) return "Looking at git history";
        if (Has(@"^git\s+(fetch|pull)\b")) return "Fetching from GitHub";
        if (Regex.Match(s, @"^git\s+(?:-c\s+\S+\s+)*([\w-]+)", RegexOptions.None, Limit) is { Success: true } git) return "Running git " + git.Groups[1].Value;
        if (Has(@"^gh\s")) return "Checking GitHub";
        if (Has(@"^(get-content|gc|cat|type|head|tail|more)\b"))
        {
            var files = FileToken.Matches(statement).Select(match => Path.GetFileName(match.Value.Replace('\\', '/'))).Distinct().Take(4).ToList();
            return files.Count > 0 ? "Reading " + string.Join(", ", files) : "Reading a file";
        }
        if (Has(@"^(rg|grep|select-string|sls|findstr|git\s+grep)\b"))
        {
            var term = Quoted.Match(statement);
            return "Searching the code" + (term.Success ? " for “" + term.Groups[1].Value + "”" : "");
        }
        if (Has(@"^(get-childitem|gci|ls|dir|tree|find)\b")) return "Looking through the files";
        if (Has(@"--version\b|^get-command\b|^where(\.exe)?\s|^which\s")) return "Checking installed tool versions";
        if (Has(@"^(node|python|py)\s+(-e|-c)\b")) return "Running a small script";
        if (Has(@"^(test-path|resolve-path|get-item)\b")) return "Checking files exist";
        if (Has(@"^(new-item|mkdir|set-content|out-file|copy-item|move-item|remove-item|rm)\b")) return "Changing files";
        var first = s.Split(' ', 2)[0].Trim('"', '\'');
        return "Running " + Path.GetFileNameWithoutExtension(first.Replace('\\', '/'));
    }

    static string Join(List<string> items) => items.Count switch
    {
        1 => items[0], 2 => items[0] + " and " + items[1],
        _ => string.Join(", ", items.Take(3)) + (items.Count > 3 ? " and " + (items.Count - 3) + " more" : "")
    };
}
