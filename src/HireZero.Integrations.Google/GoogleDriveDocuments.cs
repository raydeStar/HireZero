using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireZero.Integrations.Documents;

namespace HireZero.Integrations.Google;

// An invitation to one document, never a key to the filing cabinet. Only owner-selected file IDs are read.
public sealed class GoogleDriveDocuments : IIntegrationStatusSource, IConfigurableIntegration
{
    public const string KeySecret = "HIREZERO_GOOGLE_DRIVE_SERVICE_ACCOUNT";
    public const string ReadScope = "https://www.googleapis.com/auth/drive.readonly";
    public const string CopyNotice =
        "Import creates an editable text copy, not a live sync. Drive changes or disconnection do not update or remove saved copies. Review and approve copies separately; revoke or delete them in Documents.";
    private const string DocMime = "application/vnd.google-apps.document";
    private const string WordMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Api = "https://www.googleapis.com/drive/v3/files/";
    private readonly string _root;
    private readonly Func<string?> _key;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record Settings(string[]? FileIds = null);

    private sealed record Snapshot(string[] FileIds, string Key, string Digest);

    private sealed record Metadata(string Id, string Name, string MimeType, string Version, DateTimeOffset ModifiedAt);

    public sealed record Preview(
        string Title,
        string Text,
        string SourceName,
        string Notice,
        CompanyDocumentSource Source
    );

    public GoogleDriveDocuments(string dataRoot, Func<string?> key, HttpMessageHandler? handler = null)
    {
        _root = dataRoot;
        _key = key;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }, handler is null)
        {
            Timeout = TimeSpan.FromSeconds(25),
        };
    }

    public IntegrationSetup Setup { get; } =
        new(
            "google-drive",
            "google-drive.json",
            [
                new(
                    "fileIds",
                    "Allowed Drive files",
                    "Paste one Google Docs or Drive file link per line, or its file ID. At most 20 individual Google Docs, Word, UTF-8 text or Markdown files; folders and shortcuts are not followed. Links are saved as exact file IDs without changing their letter case.",
                    2048
                )
                {
                    List = true,
                    MaxItems = 20,
                    CaseSensitive = true,
                    Normalize = FileIdFromLink,
                    Check = value =>
                        ValidId(value)
                            ? null
                            : "Paste a Google Docs or Drive file link, or a file ID. Folder and other website links are not supported.",
                },
            ],
            [
                new(
                    KeySecret,
                    "Drive service account key",
                    "A dedicated service account JSON key. Share only the selected files with its email as Viewer. This connection never impersonates a user."
                )
                {
                    Multiline = true,
                },
            ]
        )
        {
            Steps =
            [
                new(
                    "Enable the Google Drive API and create a dedicated service account. Keep its JSON key private.",
                    "https://console.cloud.google.com/apis/library/drive.googleapis.com",
                    "Google Drive API"
                ),
                new(
                    "Share each selected file with that service account as Viewer, then paste its file link below. The read-only token can read files shared with the account; HireZero additionally enforces this exact allowlist. Do not grant domain-wide delegation."
                ),
                new(
                    "Open Company → Documents, select an allowed file and preview it. Nothing is imported automatically. "
                        + CopyNotice
                ),
                new(
                    "Remove file IDs or the key to stop new reads. Previously saved copies are managed separately in Documents."
                ),
            ],
        };

    private static bool ValidId(string? id) =>
        id is { Length: >= 10 and <= 200 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // Read the address, never visit it. Only the exact resulting ID can enter the existing allowlist.
    private static string FileIdFromLink(string value)
    {
        if (ValidId(value))
            return value;
        if (
            !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
            || uri.Host is not ("docs.google.com" or "drive.google.com")
            || value.Contains('\\')
        )
            return value;
        var parts = uri.AbsolutePath.TrimEnd('/').Split('/');
        var expected = uri.Host == "docs.google.com" ? "document" : "file";
        if (
            parts.Length is 4 or 5
            && parts[1] == expected
            && parts[2] == "d"
            && ValidId(parts[3])
            && (parts.Length == 4 || parts[4] is "edit" or "view" or "preview")
        )
            return parts[3];
        if (uri.Host == "drive.google.com" && uri.AbsolutePath == "/open")
        {
            var ids = uri
                .Query.TrimStart('?')
                .Split('&')
                .Where(p => p.StartsWith("id=", StringComparison.Ordinal))
                .ToArray();
            if (ids.Length == 1 && ValidId(ids[0][3..]))
                return ids[0][3..];
        }
        return value;
    }

    private Snapshot Current()
    {
        var path = Path.Combine(_root, "google-drive.json");
        if (File.Exists(path) && new FileInfo(path).Length > 32768)
            throw new WorkspaceException("The Drive settings file is too large.", 422);
        Settings settings;
        try
        {
            settings = File.Exists(path) ? CompanyJson.Decode<Settings>(File.ReadAllText(path)) : new();
        }
        catch (JsonException)
        {
            throw new WorkspaceException("Check the Drive file settings.", 422);
        }
        var ids = settings.FileIds ?? [];
        if (ids.Length > 20 || ids.Any(id => !ValidId(id)))
            throw new WorkspaceException("Choose at most 20 valid Drive file IDs.", 422);
        var key = _key() ?? "";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CompanyJson.Encode(ids) + "\n" + key)));
        return new(ids.Distinct(StringComparer.Ordinal).ToArray(), key, digest);
    }

    public string[] AllowedFiles() => Current().FileIds;

    public IntegrationStatus Status()
    {
        try
        {
            var current = Current();
            var account = GoogleServiceAccount.Parse(current.Key);
            var active = current.FileIds.Length > 0 && account is not null;
            return new(
                "google-drive",
                "Google Drive documents",
                active,
                active
                    ? $"Configured for {current.FileIds.Length} selected files. Preview verifies current access; no automatic imports."
                    : "Add a dedicated Drive service account key and select allowed files. Then preview a file in Documents."
            );
        }
        catch (Exception e) when (e is WorkspaceException or InvalidOperationException or IOException)
        {
            return new(
                "google-drive",
                "Google Drive documents",
                false,
                "Check the Drive key and allowed file settings."
            );
        }
    }

    public async Task<Preview> ReadAsync(string fileId, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await _gate.WaitAsync(timeout.Token);
        try
        {
            var snapshot = Current();
            if (!ValidId(fileId) || !snapshot.FileIds.Contains(fileId, StringComparer.Ordinal))
                throw new WorkspaceException("This file is not on this company's allowed Drive list.", 403);
            var key =
                GoogleServiceAccount.Parse(snapshot.Key)
                ?? throw new WorkspaceException("Connect this company's Drive service account first.", 409);
            using var auth = new HttpRequestMessage(HttpMethod.Post, GoogleServiceAccount.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                        ["assertion"] = GoogleServiceAccount.Assertion(key, DateTimeOffset.UtcNow, ReadScope),
                    }
                ),
            };
            using var credentials = JsonDocument.Parse(await Send(auth, 32768, timeout.Token));
            var access = credentials.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(access) || access.Length > 8192 || access.Any(char.IsControl))
                throw new WorkspaceException("Google did not return a usable Drive access token.", 502);
            var header = new AuthenticationHeaderValue("Bearer", access);
            void RequireCurrent()
            {
                if (Current().Digest != snapshot.Digest)
                    throw new WorkspaceException(
                        "Drive settings changed during the read. Preview the file again.",
                        409
                    );
            }
            async Task<Metadata> ReadMetadata()
            {
                RequireCurrent();
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    Api
                        + fileId
                        + "?supportsAllDrives=true&fields=id,name,mimeType,version,modifiedTime,trashed,capabilities(canDownload)"
                );
                request.Headers.Authorization = header;
                using var json = JsonDocument.Parse(await Send(request, 32768, timeout.Token));
                var root = json.RootElement;
                string Text(string name) => root.GetProperty(name).GetString() ?? "";
                var result = new Metadata(
                    Text("id"),
                    Text("name"),
                    Text("mimeType"),
                    Text("version"),
                    DateTimeOffset.Parse(Text("modifiedTime"), System.Globalization.CultureInfo.InvariantCulture)
                );
                if (
                    result.Id != fileId
                    || result.Name.Length is < 1 or > 160
                    || result.Name.Any(char.IsControl)
                    || result.Version.Length is < 1 or > 100
                    || !result.Version.All(char.IsAsciiDigit)
                    || root.GetProperty("trashed").GetBoolean()
                    || !root.GetProperty("capabilities").GetProperty("canDownload").GetBoolean()
                )
                    throw new WorkspaceException("This Drive file is unavailable for text import.", 422);
                if (result.MimeType is not (DocMime or WordMime or "text/plain" or "text/markdown"))
                    throw new WorkspaceException(
                        "Choose a Google Doc, Word .docx, UTF-8 text or Markdown file. Folders, shortcuts and other formats are not imported.",
                        422
                    );
                return result;
            }
            var before = await ReadMetadata();
            RequireCurrent();
            var uri =
                Api
                + fileId
                + (before.MimeType == DocMime ? "/export?mimeType=text%2Fplain" : "?alt=media&supportsAllDrives=true");
            using var content = new HttpRequestMessage(HttpMethod.Get, uri);
            content.Headers.Authorization = header;
            var bytes = await Send(
                content,
                before.MimeType == WordMime ? WordTextImport.FileByteLimit : CompanyStore.DocumentByteLimit,
                timeout.Token
            );
            var text =
                before.MimeType == WordMime
                    ? WordTextImport.Extract(bytes, CompanyStore.DocumentByteLimit).Text
                    : new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Trim();
            if (text.Length == 0 || text.Contains('\0'))
                throw new WorkspaceException("The Drive file contains no supported text.", 422);
            var after = await ReadMetadata();
            if (after != before || Current().Digest != snapshot.Digest)
                throw new WorkspaceException(
                    "The Drive file or connection changed during import. Preview it again to review a current copy.",
                    409
                );
            var imported = DateTimeOffset.UtcNow;
            var copy =
                $"Source: Google Drive https://drive.google.com/file/d/{fileId}/view\nDrive version: {before.Version}; modified {before.ModifiedAt:O}\nCopied: {imported:O}. Editable snapshot; not synchronized with Drive.\n\n"
                + text;
            if (Encoding.UTF8.GetByteCount(copy) > CompanyStore.DocumentByteLimit)
                throw new WorkspaceException(
                    "The extracted text and source information exceed 64 KiB. Import a shorter document.",
                    413
                );
            var name = new string(before.Name.Select(c => c is '/' or '\\' or ':' ? '_' : c).ToArray());
            var extension = before.MimeType == WordMime ? ".docx" : ".txt";
            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                name = name[..Math.Min(name.Length, 160 - extension.Length)] + extension;
            return new(
                before.Name,
                copy,
                name,
                CopyNotice
                    + (
                        before.MimeType == WordMime
                            ? " " + WordTextImport.Notice
                            : " Plain text omits layout and non-text content; check the export against the original."
                    ),
                new CompanyDocumentSource(
                    "google-drive",
                    fileId,
                    before.Version,
                    before.ModifiedAt,
                    imported,
                    CompanyJson.Digest(text.Replace("\r\n", "\n")),
                    CompanyJson.Digest(copy.Replace("\r\n", "\n"))
                )
            );
        }
        catch (DocumentImportException e)
        {
            throw new WorkspaceException(e.Message, e.Status);
        }
        catch (DecoderFallbackException)
        {
            throw new WorkspaceException("Export this Drive file as UTF-8 text before importing.", 422);
        }
        catch (Exception e)
            when (e
                    is JsonException
                        or KeyNotFoundException
                        or FormatException
                        or InvalidOperationException
                        or CryptographicException
            )
        {
            throw new WorkspaceException(
                "Google Drive returned an unreadable response, or the connection key is invalid. Check setup and preview again.",
                502
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]> Send(HttpRequestMessage request, int limit, CancellationToken token)
    {
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new WorkspaceException(
                "Google Drive could not read this selected file. Check sharing, connection settings and availability, then preview again.",
                (int)response.StatusCode == 429 ? 429 : 502
            );
        if (response.Content.Headers.ContentLength > limit)
            throw new WorkspaceException(
                "The Drive response exceeds the import limit. Choose a smaller document.",
                413
            );
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + count > limit)
                throw new WorkspaceException(
                    "The Drive response exceeds the import limit. Choose a smaller document.",
                    413
                );
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }
}
