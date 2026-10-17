namespace HireZero.Roles.Developer;

// The owner's local checkout of a project: its branch, recent commits and uncommitted changes, read through the
// git.read tool. Owner context only; visitors get the outline without commits.
public sealed class RepositoryContext(GitHubReviewClient github) : IProjectContextContributor
{
    public async Task<ContextSection[]> ContributeAsync(ProjectRef project, Audience audience, CancellationToken token)
    {
        if (audience != Audience.Owner || project.Root is not { } root || !Directory.Exists(root))
            return [];
        try
        {
            var branch = (await github.GitAsync(root, ["rev-parse", "--abbrev-ref", "HEAD"], token)).Trim();
            var log = (
                await github.GitAsync(
                    root,
                    ["log", "--since=21.days", "-n", "15", "--date=short", "--pretty=format:%ad %h %s"],
                    token
                )
            ).Trim();
            if (log.Length > 6000)
                log = log[..6000];
            var dirty = (await github.GitAsync(root, ["status", "--porcelain=v1"], token))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Length;
            return
            [
                new(
                    "Repository (local checkout)",
                    "Current branch: "
                        + branch
                        + (dirty > 0 ? $"; {dirty} uncommitted change(s)" : "; clean")
                        + "\nRecent commits:\n"
                        + (log.Length == 0 ? "none in three weeks" : log)
                ),
            ];
        }
        catch (ReviewAccessException)
        {
            return [];
        }
    }
}
