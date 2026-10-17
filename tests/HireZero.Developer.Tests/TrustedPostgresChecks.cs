using System.Text.Json;
using HireZero.Roles.Developer;

// The trusted PostgreSQL runner as proof: only HireZero's own, unmodified runner wrapping a known test command counts.
// The runner file is written from the build; PostgreSQL itself isn't downloaded here.
public static class TrustedPostgresChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-pg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new GitHubReviewStore(Path.Combine(root, "pg.sqlite"));
            var trusted = new TrustedPostgres(root, store);
            check(
                !trusted.RunnerIntact
                    && trusted.Inner("node \"" + trusted.RunnerPath + "\" -- node --test a.mjs") is null,
                "Before it's installed, the runner proves nothing."
            );
            trusted.WriteRunner();
            var slashed = trusted.RunnerPath.Replace('\\', '/');
            check(
                trusted.Inner("node \"" + trusted.RunnerPath + "\" -- node --test test/a.test.mjs")
                    == "node --test test/a.test.mjs"
                    && trusted.Inner("node " + slashed + " -- npm run test:postgres") == "npm run test:postgres"
                    && trusted.Inner("node \"C:/elsewhere/runner.mjs\" -- node --test a.mjs") is null
                    && trusted.Inner("node \"" + slashed + "\" node --test a.mjs") is null,
                "Only the installed runner's exact path, followed by -- and a command, is recognized."
            );

            string Event(string command, int exit) => JsonSerializer.Serialize(new { command, exitCode = exit });
            JsonElement Run(params (string Command, int Exit)[] steps) =>
                JsonSerializer.SerializeToElement(
                    new
                    {
                        events = steps.Select(step => new
                        {
                            type = "command-completed",
                            dataJson = Event(
                                "\"C:\\\\pwsh.exe\" -Command 'node \"" + slashed + "\" -- " + step.Command + "'",
                                step.Exit
                            ),
                        }),
                    }
                );
            check(
                GitHubPublication
                    .Evidence(Run(("node --test test/a.test.mjs", 1), ("node --test test/a.test.mjs", 0)))
                    .Reproduced,
                "A test that fails then passes under the trusted runner is proof."
            );
            check(
                !GitHubPublication.Evidence(Run(("node evil.mjs", 1), ("node evil.mjs", 0))).Reproduced,
                "The runner wrapping anything other than a known test command isn't proof."
            );

            File.AppendAllText(trusted.RunnerPath, "\n// changed");
            check(
                !GitHubPublication
                    .Evidence(Run(("node --test test/a.test.mjs", 1), ("node --test test/a.test.mjs", 0)))
                    .Reproduced && !trusted.Ready,
                "A modified runner proves nothing until HireZero reinstalls it."
            );

            store.SavePostgresProfile(
                new PostgresProfile("Owner/Example", "app_test", new() { ["DATABASE_URL"] = "{url}" }),
                "Owner/Example"
            );
            check(
                store.PostgresProfile("owner/example") is { Database: "app_test" }
                    && trusted.Enabled("OWNER/EXAMPLE")
                    && trusted.ProfileFile("owner/example") is null,
                "Profiles are per repository (any case), and no profile file is handed out while the runner isn't ready."
            );
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    }
}
