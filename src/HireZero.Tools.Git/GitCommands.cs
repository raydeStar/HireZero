namespace HireZero.Tools.Git;

// A git command line, without the executable. Leading "-c key=value" pairs are configuration, not the subcommand.
public sealed record GitCommand(string? Root, string[] Args);

// Which tool a git command belongs to. The integration enforces the same classification, so a caller cannot run a
// write through the read tool.
public static class GitCommands
{
    public static readonly ToolContract<GitCommand, string> Read = new(
        "git.read",
        ToolRisk.Read,
        "Run a read-only git command (rev-parse, status, diff, ls-files, log, show, remote get-url, config --get, worktree list)."
    );

    public static readonly ToolContract<GitCommand, string> Write = new(
        "git.write",
        ToolRisk.Write,
        "Change a local checkout (fetch, clone, worktree add, reset, clean, add, commit) under an approved workflow."
    );

    public static readonly ToolContract<GitCommand, string> Push = new(
        "git.push",
        ToolRisk.External,
        "Push an approved commit to a remote branch."
    );

    private static readonly HashSet<string> ReadOnly =
    [
        "rev-parse",
        "status",
        "diff",
        "ls-files",
        "ls-tree",
        "cat-file",
        "log",
        "show",
    ];
    private static readonly HashSet<string> LocalWrites = ["fetch", "clone", "reset", "clean", "add", "commit"];

    // The subcommand and its arguments after any leading "-c" configuration pairs.
    public static ArraySegment<string> Subcommand(string[] args)
    {
        var index = 0;
        while (index + 1 < args.Length && args[index] == "-c")
            index += 2;
        return new(args, index, args.Length - index);
    }

    // The tool for a command, or null when HireZero runs no such git command.
    public static ToolContract<GitCommand, string>? Classify(string[] args)
    {
        var command = Subcommand(args);
        if (command.Count == 0)
            return null;
        var name = command[0];
        string? Next(int offset) => command.Count > offset ? command[offset] : null;
        if (ReadOnly.Contains(name))
            return Read;
        if (name == "remote")
            return Next(1) == "get-url" ? Read : null;
        if (name == "config")
            return
                command.Any(a => a is "--get" or "--get-all" or "--get-regexp")
                && !command.Any(a =>
                    a
                        is "--unset"
                            or "--unset-all"
                            or "--add"
                            or "--replace-all"
                            or "--edit"
                            or "-e"
                            or "--rename-section"
                            or "--remove-section"
                )
                ? Read
                : null;
        if (name == "worktree")
            return Next(1) switch
            {
                "list" => Read,
                "add" => Write,
                _ => null,
            };
        if (LocalWrites.Contains(name))
            return Write;
        return name == "push" ? Push : null;
    }
}
