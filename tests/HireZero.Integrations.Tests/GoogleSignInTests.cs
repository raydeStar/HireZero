using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireZero.Integrations.Google;

// Google sign-in (docs/adr/0049-google-sign-in.md) against a fake handler: the consent URL carries PKCE and asks for
// read-only scopes; the code exchange sends the verifier and returns the refresh token; a signed-in provider reads with
// the sign-in rather than a service account; and the pickers list what the account can read. No call reaches Google.
public sealed class GoogleSignInTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "google-sign-in-" + Guid.NewGuid().ToString("N"));
    private const string ClientId = "fictional-client.apps.googleusercontent.com";
    private const string Redirect = "http://127.0.0.1:5065/oauth/google/callback";

    public GoogleSignInTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Url, string? Auth, string Body)> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
            Requests.Add((url, request.Headers.Authorization?.ToString(), body));
            var json =
                url == GoogleServiceAccount.TokenEndpoint
                    ? body.Contains("grant_type=authorization_code", StringComparison.Ordinal)
                        ? """{"access_token":"fictional-first","refresh_token":"fictional-refresh","expires_in":3600}"""
                        : """{"access_token":"fictional-signed-in","expires_in":3600}"""
                    : url.StartsWith("https://analyticsadmin.googleapis.com/", StringComparison.Ordinal)
                        ? """{"accountSummaries":[{"propertySummaries":[{"property":"properties/123456","displayName":"Fictional site"}]}]}"""
                        : url.StartsWith(
                            "https://searchconsole.googleapis.com/webmasters/v3/sites",
                            StringComparison.Ordinal
                        )
                        && request.Method == HttpMethod.Get
                            ? """{"siteEntry":[{"siteUrl":"sc-domain:fictional.example.invalid","permissionLevel":"siteOwner"},{"siteUrl":"https://unverified.example.invalid/","permissionLevel":"siteUnverifiedUser"}]}"""
                            : """{"rows":[{"clicks":1,"impressions":10,"ctr":0.1,"position":3}]}""";
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private (GoogleAnalyticsProvider Provider, Fake Fake, Dictionary<string, string> Secrets) Create()
    {
        File.WriteAllText(
            Path.Combine(_root, "google.json"),
            JsonSerializer.Serialize(
                new { oauthClientId = ClientId, searchConsoleSite = "sc-domain:fictional.example.invalid" }
            )
        );
        var secrets = new Dictionary<string, string> { [GoogleOAuth.ClientSecretName] = "fictional-client-secret" };
        var fake = new Fake();
        var provider = new GoogleAnalyticsProvider(
            _root,
            () => null,
            fake,
            secrets: name => secrets.GetValueOrDefault(name)
        );
        return (provider, fake, secrets);
    }

    [Fact]
    public void TheConsentPageAsksForReadOnlyScopesWithPkce()
    {
        var (provider, _, _) = Create();
        var (url, state, verifier) = provider.OAuth.Begin(Redirect);
        var query = new Uri(url)
            .Query.TrimStart('?')
            .Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.StartsWith(GoogleOAuth.AuthorizeEndpoint + "?", url);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal(Redirect, query["redirect_uri"]);
        Assert.Equal(GoogleServiceAccount.Scopes, query["scope"]);
        Assert.Equal(
            ("offline", "S256", state),
            (query["access_type"], query["code_challenge_method"], query["state"])
        );
        var challenge = Convert
            .ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        Assert.Equal(challenge, query["code_challenge"]);
        Assert.DoesNotContain("fictional-client-secret", url);
    }

    [Fact]
    public async Task TheCodeIsExchangedWithItsVerifierAndTheSignInIsPreferred()
    {
        var (provider, fake, secrets) = Create();
        Assert.False(provider.OAuth.SignedIn);
        Assert.Contains("Sign in with Google", provider.Status().Reason);

        var refresh = await provider.OAuth.CompleteAsync("fictional-code", "fictional-verifier", Redirect, Token);
        Assert.Equal("fictional-refresh", refresh);
        var exchange = Assert.Single(fake.Requests);
        Assert.Contains("code_verifier=fictional-verifier", exchange.Body);
        Assert.Contains("client_secret=fictional-client-secret", exchange.Body);

        secrets[GoogleOAuth.RefreshTokenName] = refresh;
        var status = provider.Status();
        Assert.True(status.Active);
        Assert.Contains("with your Google sign-in", status.Reason);

        // Reads use the sign-in's access token, and the pickers list what the account can read.
        var (properties, sites, problems) = await provider.ChoicesAsync(Token);
        Assert.Equal([new("123456", "Fictional site")], properties);
        Assert.Equal(["sc-domain:fictional.example.invalid"], sites.Select(s => s.Value));
        Assert.Empty(problems);
        Assert.All(
            fake.Requests.Where(r => r.Url != GoogleServiceAccount.TokenEndpoint),
            r => Assert.Equal("Bearer fictional-signed-in", r.Auth)
        );
    }
}
