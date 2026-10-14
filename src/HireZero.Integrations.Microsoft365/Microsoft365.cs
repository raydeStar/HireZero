using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace HireZero.Integrations.Microsoft365;

// Discovery from Microsoft 365: Outlook inbox (Graph delta) and Teams chats, read-only, fed into the intake
// pipeline. Public-client OAuth with PKCE (no secret); tokens are encrypted with ASP.NET Data Protection.
public sealed record MicrosoftSettings(string? ClientId, bool Mail = true, bool Teams = true);
public sealed record MicrosoftAccount(string Address, string UserId, bool WorkAccount, string ProtectedRefreshToken, string? ProtectedAccessToken,
    string? AccessExpiresAt, string[] Scopes, string? MailCursor, string? TeamsSince, string? LastMailSync, string? LastTeamsSync, string? LastError, string ConnectedAt);
// A normalized message before intake.
public sealed record GraphMessage(string Source, string ExternalId, string ThreadKey, string From, string Subject, string Body, DateTimeOffset ReceivedAt);

public sealed class MicrosoftStore(string dataRoot)
{
    private readonly string _settings = Path.Combine(dataRoot, "microsoft365.json"), _account = Path.Combine(dataRoot, "microsoft365-account.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    public MicrosoftSettings Settings() => Read<MicrosoftSettings>(_settings) ?? new(null);
    public void SaveSettings(MicrosoftSettings settings) => Write(_settings, settings);
    public MicrosoftAccount? Account() => Read<MicrosoftAccount>(_account);
    public void SaveAccount(MicrosoftAccount account) => Write(_account, account);
    public void Disconnect() { lock (_gate) if (File.Exists(_account)) File.Delete(_account); }
    private T? Read<T>(string path) { lock (_gate) { try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : default; } catch (JsonException) { return default; } } }
    private void Write<T>(string path, T value) { lock (_gate) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value, Json)); } }
}

public sealed class MicrosoftConnector(MicrosoftStore store, IHttpClientFactory clients, IDataProtectionProvider protection)
{
    public const string RedirectUri = "http://localhost:5064/oauth/microsoft/callback";
    const string Authority = "https://login.microsoftonline.com/common/oauth2/v2.0/";
    private readonly IDataProtector _tokens = protection.CreateProtector("HireZero.Microsoft365.Tokens.v1");
    private readonly Dictionary<string, (string Verifier, string[] Scopes, DateTimeOffset Expires)> _pending = new();
    private readonly object _gate = new();
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public static string[] Scopes(MicrosoftSettings settings, bool workAccount = true) =>
        ["openid", "profile", "email", "offline_access", "User.Read", .. settings.Mail ? new[] { "Mail.Read" } : [], .. settings.Teams && workAccount ? new[] { "Chat.Read" } : []];

    // Step 1: an authorize URL with PKCE and a one-time state, valid for ten minutes.
    public string Begin()
    {
        var settings = store.Settings();
        if (settings.ClientId is not { Length: > 0 } clientId) throw new WorkspaceException("Add your Microsoft app (client) ID first.", 400);
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64)); var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var scopes = Scopes(settings);
        lock (_gate) { foreach (var stale in _pending.Where(item => item.Value.Expires < Clock()).Select(item => item.Key).ToArray()) _pending.Remove(stale); _pending[state] = (verifier, scopes, Clock().AddMinutes(10)); }
        var query = new Dictionary<string, string> { ["client_id"] = clientId, ["redirect_uri"] = RedirectUri, ["response_type"] = "code", ["response_mode"] = "query",
            ["scope"] = string.Join(' ', scopes), ["state"] = state, ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256", ["prompt"] = "select_account" };
        return Authority + "authorize?" + string.Join('&', query.Select(pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value)));
    }

    // Step 2: the callback exchanges the code once, reads the signed-in identity and stores encrypted tokens.
    public async Task CompleteAsync(string code, string state, CancellationToken token)
    {
        (string Verifier, string[] Scopes, DateTimeOffset Expires) pending;
        lock (_gate) { if (!_pending.Remove(state, out pending) || pending.Expires < Clock()) throw new WorkspaceException("This sign-in link expired. Start again from Settings.", 400); }
        var clientId = store.Settings().ClientId!;
        var tokens = await TokenAsync(new() { ["client_id"] = clientId, ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = pending.Verifier, ["scope"] = string.Join(' ', pending.Scopes) }, token);
        using var client = clients.CreateClient("graph");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access);
        using var me = await client.GetFromJsonAsync<JsonDocument>("v1.0/me?$select=id,mail,userPrincipalName", token) ?? throw new WorkspaceException("Microsoft didn't return your identity.", 502);
        var address = Text(me.RootElement, "mail") ?? Text(me.RootElement, "userPrincipalName") ?? "account";
        var work = !Regex.IsMatch(address, @"@(outlook|hotmail|live|msn)\.", RegexOptions.IgnoreCase);
        store.SaveAccount(new(address, Text(me.RootElement, "id") ?? "", work, _tokens.Protect(tokens.Refresh ?? ""), _tokens.Protect(tokens.Access),
            Clock().AddSeconds(tokens.ExpiresIn).ToString("O"), pending.Scopes, null, null, null, null, null, Clock().ToString("O")));
    }

    private async Task<(string Access, string? Refresh, int ExpiresIn)> TokenAsync(Dictionary<string, string> form, CancellationToken token)
    {
        using var client = clients.CreateClient("microsoft-identity");
        using var response = await client.PostAsync(Authority + "token", new FormUrlEncodedContent(form), token);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: token);
        if (!response.IsSuccessStatusCode || body is null || Text(body.RootElement, "access_token") is not { } access)
            throw new WorkspaceException("Microsoft sign-in failed" + (body is not null && Text(body.RootElement, "error") is { } error ? " (" + error + ")" : "") + ". Reconnect in Settings.", 401);
        var expires = body.RootElement.TryGetProperty("expires_in", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var seconds) ? Math.Max(60, seconds) : 3600;
        return (access, Text(body.RootElement, "refresh_token"), expires);
    }

    // An access token, refreshed when it is within five minutes of expiry (or when forced after a 401).
    public async Task<string> AccessTokenAsync(bool force, CancellationToken token)
    {
        var account = store.Account() ?? throw new WorkspaceException("Microsoft 365 isn't connected.", 409);
        if (!force && account.ProtectedAccessToken is { } stored && DateTimeOffset.TryParse(account.AccessExpiresAt, out var expires) && expires - Clock() > TimeSpan.FromMinutes(5))
            return _tokens.Unprotect(stored);
        var refreshed = await TokenAsync(new() { ["client_id"] = store.Settings().ClientId!, ["grant_type"] = "refresh_token",
            ["refresh_token"] = _tokens.Unprotect(account.ProtectedRefreshToken), ["scope"] = string.Join(' ', account.Scopes) }, token);
        store.SaveAccount(account with { ProtectedAccessToken = _tokens.Protect(refreshed.Access), AccessExpiresAt = Clock().AddSeconds(refreshed.ExpiresIn).ToString("O"),
            ProtectedRefreshToken = refreshed.Refresh is { Length: > 0 } rotated ? _tokens.Protect(rotated) : account.ProtectedRefreshToken });
        return refreshed.Access;
    }

    // GET with one refresh after 401, Retry-After on 429/503/504 (up to 3 tries). 410 is returned to the caller.
    public async Task<(HttpStatusCode Status, JsonDocument? Body)> GetAsync(string url, CancellationToken token)
    {
        var force = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var client = clients.CreateClient("graph");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(force, token));
            request.Headers.Add("Prefer", "odata.maxpagesize=50");
            using var response = await client.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !force) { force = true; continue; }
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout && attempt < 3)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(500 * (attempt + 1));
                await Task.Delay(wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait, token); continue;
            }
            if (response.StatusCode == HttpStatusCode.Gone) return (response.StatusCode, null);
            if (!response.IsSuccessStatusCode) throw new WorkspaceException("Microsoft Graph answered " + (int)response.StatusCode + " for " + new Uri(url, UriKind.RelativeOrAbsolute).ToString().Split('?')[0] + ".", 502);
            return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: token));
        }
        throw new WorkspaceException("Microsoft Graph kept refusing; it will try again later.", 503);
    }

    // Outlook: a delta cursor over the inbox. The first sync looks back one day; a 410 restarts from now.
    public async Task<GraphMessage[]> ReadMailAsync(CancellationToken token)
    {
        var account = store.Account()!; var found = new List<GraphMessage>();
        var url = ValidDelta(account.MailCursor) ? account.MailCursor! : FirstMailUrl(Clock().AddDays(-1));
        for (var page = 0; page < 10 && found.Count < 500; page++)
        {
            var (status, body) = await GetAsync(url, token);
            if (status == HttpStatusCode.Gone) { store.SaveAccount(account with { MailCursor = null }); url = FirstMailUrl(Clock().AddHours(-1)); continue; }
            using (body)
            {
                found.AddRange(MailRecords(body!.RootElement, account.Address));
                if (Text(body.RootElement, "@odata.nextLink") is { } next && ValidDelta(next)) { url = next; continue; }
                if (Text(body.RootElement, "@odata.deltaLink") is { } delta && ValidDelta(delta)) url = delta;
                break;
            }
        }
        store.SaveAccount((store.Account() ?? account) with { MailCursor = url, LastMailSync = Clock().ToString("O"), LastError = null });
        return [.. found.DistinctBy(message => message.ExternalId)];
    }
    public static string FirstMailUrl(DateTimeOffset since) =>
        "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?changeType=created&$filter=receivedDateTime+ge+" + since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ") +
        "&$select=id,conversationId,receivedDateTime,subject,bodyPreview,from&$top=50";
    // Only Graph's own inbox delta links are followed.
    public static bool ValidDelta(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath.StartsWith("/v1.0/me/mailFolders/", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.EndsWith("/messages/delta", StringComparison.OrdinalIgnoreCase);
    public static IEnumerable<GraphMessage> MailRecords(JsonElement page, string ownAddress)
    {
        if (!page.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("@removed", out _) || Text(item, "id") is not { } id) continue;
            var from = item.TryGetProperty("from", out var sender) && sender.TryGetProperty("emailAddress", out var address) ? address : default;
            var fromAddress = Text(from, "address") ?? "";
            if (fromAddress.Equals(ownAddress, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Text(from, "name");
            yield return new("outlook", id, Text(item, "conversationId") ?? id, name is { Length: > 0 } && fromAddress.Length > 0 ? name + " <" + fromAddress + ">" : fromAddress,
                Text(item, "subject") ?? "", Clip(Text(item, "bodyPreview") ?? "", 2000), Time(item, "receivedDateTime"));
        }
    }

    // Teams: chats active since the last read; your own and system messages are skipped.
    public async Task<GraphMessage[]> ReadTeamsAsync(CancellationToken token)
    {
        var account = store.Account()!;
        if (!account.WorkAccount || !account.Scopes.Contains("Chat.Read")) return [];
        var since = DateTimeOffset.TryParse(account.TeamsSince, out var saved) ? saved.AddMinutes(-2) : Clock().AddDays(-1);
        var (_, chats) = await GetAsync("https://graph.microsoft.com/v1.0/me/chats?$expand=lastMessagePreview&$top=50&$orderby=lastMessagePreview/createdDateTime+desc", token);
        var found = new List<GraphMessage>();
        using (chats)
        {
            var list = chats is not null && chats.RootElement.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().ToArray() : [];
            foreach (var chat in list.Take(50))
            {
                if (Text(chat, "id") is not { } chatId) continue;
                var preview = chat.TryGetProperty("lastMessagePreview", out var last) ? Time(last, "createdDateTime") : DateTimeOffset.MinValue;
                if (preview < since && Time(chat, "lastUpdatedDateTime") < since) continue;
                var (_, messages) = await GetAsync("https://graph.microsoft.com/v1.0/chats/" + Uri.EscapeDataString(chatId) + "/messages?$top=50&$orderby=lastModifiedDateTime+desc&$filter=lastModifiedDateTime+gt+" +
                    since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), token);
                using (messages) if (messages is not null) found.AddRange(TeamsRecords(messages.RootElement, chatId, Text(chat, "topic"), account.UserId));
                if (found.Count >= 500) break;
            }
        }
        store.SaveAccount((store.Account() ?? account) with { TeamsSince = Clock().ToString("O"), LastTeamsSync = Clock().ToString("O"), LastError = null });
        return [.. found.DistinctBy(message => message.ExternalId)];
    }
    public static IEnumerable<GraphMessage> TeamsRecords(JsonElement page, string chatId, string? topic, string ownUserId)
    {
        if (!page.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "id") is not { } id || Text(item, "messageType") != "message") continue;
            var user = item.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object && from.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
            if (Text(user, "id") is { } author && author == ownUserId) continue;
            var body = item.TryGetProperty("body", out var content) ? PlainText(Text(content, "content") ?? "") : "";
            yield return new("teams", chatId + ":" + id, chatId, Text(user, "displayName") ?? "Teams", topic is { Length: > 0 } ? "Teams: " + topic : "Teams chat",
                body, Time(item, "createdDateTime"));
        }
    }

    public static string PlainText(string html)
    {
        var text = Regex.Replace(html, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        text = Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
        return text.Length == 0 ? "(No message text)" : Clip(text, 4000);
    }
    static string? Text(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    static DateTimeOffset Time(JsonElement value, string property) => DateTimeOffset.TryParse(Text(value, property), out var time) ? time : DateTimeOffset.MinValue;
    static string Clip(string value, int limit) => value.Length <= limit ? value : value[..limit];
    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

// Polls Outlook every minute and Teams every two minutes while connected; each message goes through intake.
public sealed class MicrosoftPoller(MicrosoftStore store, MicrosoftConnector connector, IntakePipeline intake, AgentWorker worker, ILogger<MicrosoftPoller> log) : BackgroundService
{
    private DateTimeOffset _lastTeams = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _now = new(0);
    public void SyncNow() { if (_now.CurrentCount == 0) _now.Release(); }

    public async Task<int> SyncAsync(bool includeTeams, CancellationToken token)
    {
        if (store.Account() is null || store.Settings().ClientId is null) return 0;
        var settings = store.Settings(); var messages = new List<GraphMessage>();
        try
        {
            if (settings.Mail) messages.AddRange(await connector.ReadMailAsync(token));
            if (settings.Teams && includeTeams) { messages.AddRange(await connector.ReadTeamsAsync(token)); _lastTeams = DateTimeOffset.UtcNow; }
        }
        catch (Exception error) when (error is WorkspaceException or HttpRequestException or JsonException or CryptographicException or TaskCanceledException && !token.IsCancellationRequested)
        {
            var account = store.Account(); if (account is not null) store.SaveAccount(account with { LastError = error is WorkspaceException known ? known.Message : "Microsoft 365 couldn't be read; it will try again." });
            log.LogWarning(error, "Microsoft 365 sync failed."); return 0;
        }
        foreach (var message in messages.OrderBy(item => item.ReceivedAt))
            await intake.ProcessAsync(new InboundMessage(message.Source, message.ExternalId, message.ThreadKey, message.From, message.Subject, message.Body, message.ReceivedAt), token);
        if (messages.Count > 0) { AgentActivity.Note("Read " + messages.Count + " new Microsoft 365 message(s) into the inbox."); worker.Wake(); }
        return messages.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            // Graph can only push change notifications to a public HTTPS endpoint, so a local workspace polls with delta queries:
            // mail every 30 seconds, Teams every minute, on shift or not.
            await SyncAsync(DateTimeOffset.UtcNow - _lastTeams >= TimeSpan.FromSeconds(60), stoppingToken);
            await _now.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}

public static class Microsoft365Services
{
    public static IServiceCollection AddMicrosoft365(this IServiceCollection services, string dataRoot)
    {
        var keys = services.AddDataProtection().SetApplicationName("HireZero").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataRoot, "keys")));
        if (OperatingSystem.IsWindows()) keys.ProtectKeysWithDpapi();
        services.AddHttpClient("graph", client => { client.BaseAddress = new Uri("https://graph.microsoft.com/"); client.Timeout = TimeSpan.FromSeconds(30); });
        services.AddHttpClient("microsoft-identity", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton(new MicrosoftStore(dataRoot));
        services.AddSingleton<MicrosoftConnector>();
        services.AddSingleton<MicrosoftPoller>();
        return services.AddHostedService(provider => provider.GetRequiredService<MicrosoftPoller>());
    }

    public static void MapMicrosoft365(this WebApplication app)
    {
        static bool Owner(HttpContext context) => (string?)context.Items["role"] == "owner";
        app.MapGet("/api/connections/microsoft", (HttpContext context, MicrosoftStore store) =>
        {
            if (!Owner(context)) return Results.StatusCode(403);
            var settings = store.Settings(); var account = store.Account();
            return Results.Ok(new { configured = settings.ClientId is { Length: > 0 }, settings.Mail, settings.Teams, redirectUri = MicrosoftConnector.RedirectUri,
                account = account is null ? null : new { account.Address, account.WorkAccount, account.Scopes, account.LastMailSync, account.LastTeamsSync, account.LastError, account.ConnectedAt } });
        });
        app.MapPut("/api/connections/microsoft", (HttpContext context, MicrosoftStore store, MicrosoftSettingsInput input) =>
        {
            if (!Owner(context)) return Results.StatusCode(403);
            if (input.ClientId is not null && !Guid.TryParse(input.ClientId.Trim(), out _)) return Results.BadRequest(new { error = "The app (client) ID is a GUID from your Entra app registration." });
            var current = store.Settings();
            store.SaveSettings(new(input.ClientId?.Trim() ?? current.ClientId, input.Mail ?? current.Mail, input.Teams ?? current.Teams));
            return Results.Ok(new { saved = true });
        });
        app.MapPost("/api/connections/microsoft/start", (HttpContext context, MicrosoftConnector connector) =>
            Owner(context) ? Results.Ok(new { url = connector.Begin() }) : Results.StatusCode(403));
        app.MapPost("/api/connections/microsoft/sync", (HttpContext context, MicrosoftPoller poller) => { if (!Owner(context)) return Results.StatusCode(403); poller.SyncNow(); return Results.Ok(new { queued = true }); });
        app.MapDelete("/api/connections/microsoft", (HttpContext context, MicrosoftStore store) => { if (!Owner(context)) return Results.StatusCode(403); store.Disconnect(); return Results.Ok(new { disconnected = true }); });
        // Not under /api: the browser arrives here from Microsoft. The one-time state, not a session, authorizes it.
        app.MapGet("/oauth/microsoft/callback", async (string? code, string? state, string? error, MicrosoftConnector connector, MicrosoftPoller poller, CancellationToken token) =>
        {
            if (error is not null || code is null || state is null) return Results.Redirect("http://127.0.0.1:5064/?view=settings&microsoft=" + Uri.EscapeDataString(error ?? "cancelled"));
            try { await connector.CompleteAsync(code, state, token); poller.SyncNow(); return Results.Redirect("http://127.0.0.1:5064/?view=settings&microsoft=connected"); }
            catch (WorkspaceException failure) { return Results.Redirect("http://127.0.0.1:5064/?view=settings&microsoft=" + Uri.EscapeDataString(failure.Message)); }
        });
    }
}

public sealed record MicrosoftSettingsInput(string? ClientId, bool? Mail, bool? Teams);
