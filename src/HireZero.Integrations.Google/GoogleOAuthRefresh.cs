using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Google;

// An OAuth client signing in with a stored refresh token (docs/adr/0032-google-ads-provider.md), for Google APIs that
// a service account cannot reach on its own, such as Google Ads. Only Google's token endpoint is ever called. The
// client secret and refresh token are secrets read at use, so rotating either applies on the next sign-in.
public sealed class GoogleOAuthRefresh(
    Func<string?> clientId,
    Func<string?> clientSecret,
    Func<string?> refreshToken,
    HttpClient http,
    Func<DateTimeOffset>? clock = null
)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string Token, DateTimeOffset Expires, string Digest)? _token;

    public async Task<AuthenticationHeaderValue> AuthorizeAsync(CancellationToken token)
    {
        var id = clientId();
        var secret = clientSecret();
        var refresh = refreshToken();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(refresh))
            throw new InvalidOperationException("The OAuth client id, client secret and refresh token are all needed.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id + "\n" + secret + "\n" + refresh)));
        await _gate.WaitAsync(token);
        try
        {
            var now = _clock();
            if (_token is { } cached && cached.Digest == digest && cached.Expires > now.AddMinutes(1))
                return new("Bearer", cached.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, GoogleServiceAccount.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["client_id"] = id,
                        ["client_secret"] = secret,
                        ["refresh_token"] = refresh,
                    }
                ),
            };
            using var response = await http.SendAsync(request, token);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (
                !response.IsSuccessStatusCode
                || !body.RootElement.TryGetProperty("access_token", out var access)
                || access.GetString() is not { Length: > 0 } accessToken
            )
            {
                // Google's error code (for example invalid_grant) names the problem without repeating any secret.
                var reason = body.RootElement.TryGetProperty("error", out var error) ? error.ToString() : "no token";
                throw new HttpRequestException("The OAuth client could not sign in (" + reason + ").");
            }
            var lifetime = body.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
            _token = (accessToken, now.AddSeconds(lifetime), digest);
            return new("Bearer", accessToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
