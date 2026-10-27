using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireZero.Integrations.Google;

internal sealed class DriveDocumentFixture : HttpMessageHandler
{
    public const string FileId = "fictional_drive_brand";
    public string Key { get; }
    public List<string> Requests { get; } = [];
    public string Scope { get; private set; } = "";
    public string MimeType { get; set; } = "application/vnd.google-apps.document";
    public string Name { get; set; } = "Fictional shared brand guide";
    public byte[] Content { get; set; } =
        Encoding.UTF8.GetBytes("Cobalt Paper Studio uses patient demonstrations for beginner paper artists.");
    public bool CanDownload { get; set; } = true;
    public bool Trashed { get; set; }
    public bool ChangeVersion { get; set; }
    public string Version { get; set; } = "1";
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public Func<CancellationToken, Task>? BeforeContent { get; set; }
    public bool UnknownLength { get; set; }
    private int _metadataReads;

    public DriveDocumentFixture()
    {
        using var rsa = RSA.Create(2048);
        Key = JsonSerializer.Serialize(
            new
            {
                type = "service_account",
                client_email = "drive-reader@example.invalid",
                private_key = rsa.ExportPkcs8PrivateKeyPem(),
                token_uri = GoogleServiceAccount.TokenEndpoint,
            }
        );
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!;
        Requests.Add(request.Method + " " + uri.AbsoluteUri);
        if (uri.AbsoluteUri == GoogleServiceAccount.TokenEndpoint)
        {
            var form = await request.Content!.ReadAsStringAsync(token);
            var assertion = Uri.UnescapeDataString(
                form.Split('&').Single(p => p.StartsWith("assertion="))["assertion=".Length..]
            );
            var encoded = assertion.Split('.')[1].Replace('-', '+').Replace('_', '/');
            using var payload = JsonDocument.Parse(
                Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))
            );
            Scope = payload.RootElement.GetProperty("scope").GetString()!;
            if (payload.RootElement.TryGetProperty("sub", out _))
                throw new InvalidOperationException("Fixture refuses impersonation.");
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { access_token = "fictional-drive-token" }),
            };
        }
        if (
            uri.Host != "www.googleapis.com"
            || !uri.AbsolutePath.StartsWith("/drive/v3/files/" + FileId, StringComparison.Ordinal)
            || request.Method != HttpMethod.Get
            || request.Headers.Authorization?.Parameter != "fictional-drive-token"
        )
            throw new InvalidOperationException("This fixture has no such Drive request.");
        if (Status != HttpStatusCode.OK)
            return new(Status) { Content = new StringContent("fictional upstream error must not reach the UI") };
        if (uri.Query.Contains("fields="))
        {
            var count = Interlocked.Increment(ref _metadataReads);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new
                    {
                        id = FileId,
                        name = Name,
                        mimeType = MimeType,
                        version = ChangeVersion && count % 2 == 0 ? "2" : Version,
                        modifiedTime = "2026-10-20T12:00:00Z",
                        trashed = Trashed,
                        capabilities = new { canDownload = CanDownload },
                    }
                ),
            };
        }
        if (BeforeContent is not null)
            await BeforeContent(token);
        return new(HttpStatusCode.OK)
        {
            Content = UnknownLength ? new UnknownContent(Content) : new ByteArrayContent(Content),
        };
    }

    private sealed class UnknownContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(bytes));
    }
}
