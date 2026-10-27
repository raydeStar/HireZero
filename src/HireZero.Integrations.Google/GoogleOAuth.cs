using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Google;

// Signing in with the owner's own Google account for read-only Analytics and Search Console
// (docs/adr/0049-google-sign-in.md): the owner's "Desktop app" OAuth client, Google's consent page, a loopback
// redirect with PKCE, and a refresh token kept as a secret. Access tokens come from Google's token endpoint only.
// The secrets are read at use, so a new sign-in or client applies at once.
public sealed class GoogleOAuth(
    Func<string, string?> secret,
    Func<string?> clientId,
    HttpClient http,
    Func<DateTimeOffset>? clock = null
)
{
    public const string ClientSecretName = "HIREZERO_GOOGLE_OAUTH_CLIENT_SECRET";
    public const string RefreshTokenName = "HIREZERO_GOOGLE_OAUTH_REFRESH_TOKEN";
    public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string Token, DateTimeOffset Expires, string RefreshDigest)? _token;

    public string? ClientId => clientId() is { Length: > 0 } id ? id.Trim() : null;

    public bool Configured => ClientId is not null && !string.IsNullOrWhiteSpace(secret(ClientSecretName));

    public bool SignedIn => Configured && !string.IsNullOrWhiteSpace(secret(RefreshTokenName));

    // Google's consent page for this sign-in, and the PKCE verifier and state the callback must match.
    public (string Url, string State, string Verifier) Begin(string redirectUri)
    {
        var id = ClientId ?? throw new InvalidOperationException("Add your Google OAuth client first.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["client_id"] = id,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = GoogleServiceAccount.Scopes,
            ["access_type"] = "offline",
            // Asked every time, so Google always returns a refresh token for this sign-in.
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        return (
            AuthorizeEndpoint + "?" + string.Join("&", query.Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value))),
            state,
            verifier
        );
    }

    // Exchanges the callback's code for a refresh token; the caller stores it as a secret.
    public async Task<string> CompleteAsync(string code, string verifier, string redirectUri, CancellationToken token)
    {
        using var body = await TokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = redirectUri,
            },
            token
        );
        return
            body.RootElement.TryGetProperty("refresh_token", out var refresh)
            && refresh.GetString() is { Length: > 0 } r
            ? r
            : throw new InvalidOperationException("Google returned no refresh token; sign in again.");
    }

    public async Task<AuthenticationHeaderValue> AuthorizeAsync(CancellationToken token)
    {
        var refresh = secret(RefreshTokenName);
        if (string.IsNullOrWhiteSpace(refresh))
            throw new InvalidOperationException("Sign in with Google first.");
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(ClientId + "\n" + secret(ClientSecretName) + "\n" + refresh))
        );
        await _gate.WaitAsync(token);
        try
        {
            var now = _clock();
            if (_token is { } cached && cached.RefreshDigest == digest && cached.Expires > now.AddMinutes(1))
                return new("Bearer", cached.Token);
            using var body = await TokenAsync(
                new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh },
                token
            );
            var access = body.RootElement.GetProperty("access_token").GetString()!;
            var lifetime = body.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
            _token = (access, now.AddSeconds(lifetime), digest);
            return new("Bearer", access);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JsonDocument> TokenAsync(Dictionary<string, string> fields, CancellationToken token)
    {
        fields["client_id"] = ClientId ?? throw new InvalidOperationException("Add your Google OAuth client first.");
        fields["client_secret"] =
            secret(ClientSecretName) ?? throw new InvalidOperationException("Add your Google OAuth client secret.");
        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleServiceAccount.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(fields),
        };
        using var response = await http.SendAsync(request, token);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!response.IsSuccessStatusCode || !body.RootElement.TryGetProperty("access_token", out _))
        {
            var reason = body.RootElement.TryGetProperty("error", out var error) ? error.ToString() : "no token";
            body.Dispose();
            throw new HttpRequestException("Google sign-in failed (" + reason + ").");
        }
        return body;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
