using HireZero.Roles.Developer;

public static class ShiftScopeChecks
{
    sealed class FakeClient : GitHubReviewClient
    {
        public readonly List<string> Calls = [];
        public bool IncludePersonal;

        protected override Task<string> CommandAsync(
            string executable,
            string? root,
            string[] args,
            string? input,
            CancellationToken token
        )
        {
            var endpoint = args[^1];
            Calls.Add(endpoint);
            if (endpoint.StartsWith("search/issues?"))
                return Task.FromResult(
                    IncludePersonal
                        ? "{\"total_count\":2,\"items\":[{\"repository_url\":\"https://api.github.com/repos/organization/example\",\"number\":1,\"title\":\"Outside scope\"},{\"repository_url\":\"https://api.github.com/repos/OWNER/example\",\"number\":2,\"title\":\"Personal scope\"}]}"
                        : "{\"total_count\":1,\"items\":[{\"repository_url\":\"https://api.github.com/repos/organization/example\",\"number\":1,\"title\":\"Outside scope\"}]}"
                );
            throw new ReviewAccessException("Synthetic stop before detail retrieval.");
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var client = new FakeClient();
        var account = new GitHubIdentity(1, "owner");
        check(
            (await client.MyReviewsAsync(account, default, personalRepositoriesOnly: true)).Length == 0
                && client.Calls.Count == 1,
            "Personal-only shift discovery never retrieves organization repository or PR details."
        );
        client.IncludePersonal = true;
        client.Calls.Clear();
        try
        {
            await client.MyReviewsAsync(account, default, personalRepositoriesOnly: true);
            check(false, "Fake detail boundary must stop.");
        }
        catch (ReviewAccessException)
        {
            check(
                client.Calls.SequenceEqual(new[] { client.Calls[0], "repos/OWNER/example" }),
                "Exact personal owner matching is case-insensitive and precedes repository retrieval."
            );
        }
    }
}
