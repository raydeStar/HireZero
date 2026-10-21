using HireZero.Integrations.GitHub;

// GitHub sign-in (owner feedback, 2026-10-20): an existing sign-in on this computer reads as connected, with its
// account, instead of asking the owner to sign in again; without one the page offers sign-in. Fictional accounts.
public sealed class GitHubSignInTests
{
    [Fact]
    public void AnExistingSignInIsConnectedWithItsAccount()
    {
        var status = new GitHubSignIn(() => "fictional-owner").Status();
        Assert.Equal(("connected", "fictional-owner"), (status.State, status.Account));
    }

    [Fact]
    public void WithoutASignInThePageOffersOne()
    {
        var status = new GitHubSignIn(() => null).Status();
        Assert.Equal(("idle", null), (status.State, status.Account));
    }

    [Fact]
    public void TheCliStatusNamesTheAccountAndNeverTheToken()
    {
        const string output = """
            github.com
              ✓ Logged in to github.com account fictional-owner (keyring)
              - Active account: true
              - Token: gho_************************************
            """;
        Assert.Equal("fictional-owner", GitHubSignIn.ParseAccount(output));
        Assert.Null(GitHubSignIn.ParseAccount("You are not logged into any GitHub hosts."));
    }

    [Fact]
    public void TheExistingSignInIsCheckedAtMostOncePerInterval()
    {
        var checks = 0;
        var signIn = new GitHubSignIn(() =>
        {
            checks++;
            return "fictional-owner";
        });
        signIn.Status();
        signIn.Status();
        Assert.Equal(1, checks);
    }
}
