using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Google;

// A Google service account signing in for read-only scopes (docs/adr/0029-marketing-analytics-reads.md): a short-lived
// RS256 assertion exchanged for an access token at Google's token endpoint, and nowhere else. The key is a secret read
// at use, so a rotated key file applies on the next sign-in.
public sealed class GoogleServiceAccount(Func<string?> key, HttpClient http, Func<DateTimeOffset>? clock = null)
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string Scopes =
        "https://www.googleapis.com/auth/analytics.readonly https://www.googleapis.com/auth/webmasters.readonly";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string Token, DateTimeOffset Expires, string KeyDigest)? _token;

    public sealed record Key(string ClientEmail, string PrivateKey, string? PrivateKeyId);

    // Null when no key is configured; throws when one is configured but cannot be used.
    public static Key? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? Text(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            if (Text("type") != "service_account")
                throw new InvalidOperationException("The Google key is not a service account key.");
            if (Text("token_uri") is { } uri && uri != TokenEndpoint)
                throw new InvalidOperationException("The Google key names a token endpoint other than Google's.");
            var email = Text("client_email");
            var privateKey = Text("private_key");
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(privateKey))
                throw new InvalidOperationException("The Google key lacks its client email or private key.");
            return new(email, privateKey, Text("private_key_id"));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The Google key is not valid JSON.");
        }
    }

    public static string Assertion(Key key, DateTimeOffset now)
    {
        static string Encode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = new Dictionary<string, string> { ["alg"] = "RS256", ["typ"] = "JWT" };
        if (key.PrivateKeyId is { } id)
            header["kid"] = id;
        var claims = new Dictionary<string, object>
        {
            ["iss"] = key.ClientEmail,
            ["scope"] = Scopes,
            ["aud"] = TokenEndpoint,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
        };
        var unsigned =
            Encode(JsonSerializer.SerializeToUtf8Bytes(header))
            + "."
            + Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivateKey);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsigned),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return unsigned + "." + Encode(signature);
    }

    public async Task<AuthenticationHeaderValue> AuthorizeAsync(CancellationToken token)
    {
        var json = key();
        var parsed = Parse(json) ?? throw new InvalidOperationException("No Google service account key is configured.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json!)));
        await _gate.WaitAsync(token);
        try
        {
            var now = _clock();
            if (_token is { } cached && cached.KeyDigest == digest && cached.Expires > now.AddMinutes(1))
                return new("Bearer", cached.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                        ["assertion"] = Assertion(parsed, now),
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
                var reason = body.RootElement.TryGetProperty("error", out var error) ? error.ToString() : "no token";
                throw new HttpRequestException("The service account could not sign in (" + reason + ").");
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
