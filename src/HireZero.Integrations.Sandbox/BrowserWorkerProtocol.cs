using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Sandbox;

public sealed record BrowserWorkerReceipt(BrowserResult Result, bool BrowserExited, BrowserNetworkUsage Network);

// Duplex protocol only: the resource adapter must own process/container identity, exit, persistence and removal.
// The butler verifies every envelope; a worker cannot promote its own prose to permission.
public static partial class BrowserWorkerProtocol
{
    public const int Version = 1;
    public const int StartBytes = 64 * 1024;
    public const int ResourceMessageBytes = 4 * 1024;
    public const int ReplyMessageBytes = 3 * 1024 * 1024;
    public const int ResultMessageBytes = BrowserRequest.ResultBytes + 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<BrowserWorkerReceipt> ExchangeAsync(
        BrowserRequest request,
        Stream fromWorker,
        Stream toWorker,
        IBrowserNetworkSession network,
        Func<bool> stillAuthorized,
        CancellationToken token
    )
    {
        request.Validate();
        request = request with { Origins = request.Origins.ToArray(), Steps = request.Steps.ToArray() };
        request.Validate();
        token.ThrowIfCancellationRequested();
        if (!stillAuthorized())
            throw new WorkspaceException("Browser authority was revoked before dispatch.", 403);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(request.Limits.Seconds + 12));
        using var resources = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        using var writer = new SemaphoreSlim(1, 1);
        var cancelSent = 0;
        async Task WriteAsync(object message, int maximum)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
            if (bytes.Length > maximum)
                throw new IOException("The browser protocol output exceeded its bound.");
            await writer.WaitAsync(deadline.Token);
            try
            {
                await toWorker.WriteAsync(bytes, deadline.Token);
                await toWorker.WriteAsync(new byte[] { (byte)'\n' }, deadline.Token);
                await toWorker.FlushAsync(deadline.Token);
            }
            finally
            {
                writer.Release();
            }
        }
        async Task CancelAsync()
        {
            if (Interlocked.Exchange(ref cancelSent, 1) != 0)
                return;
            await resources.CancelAsync();
            // Continue bounded receipt collection after cancellation; it is not an instruction to replay work.
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            await WriteAsync(new { type = "cancel", jobId = request.JobId }, ResourceMessageBytes);
        }
        await WriteAsync(
            new
            {
                type = "start",
                version = Version,
                request,
            },
            StartBytes
        );
        using var observation = new CancellationTokenSource();
        async Task ObserveAsync()
        {
            try
            {
                while (!observation.IsCancellationRequested)
                {
                    if (token.IsCancellationRequested || !stillAuthorized())
                    {
                        await CancelAsync();
                        return;
                    }
                    await Task.Delay(100, observation.Token);
                }
            }
            catch (OperationCanceledException) when (observation.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                await resources.CancelAsync();
                await deadline.CancelAsync();
            }
        }
        var observer = ObserveAsync();
        var reader = new Frames(fromWorker);
        var expected = 1;
        BrowserWorkerReceipt? receipt = null;
        try
        {
            for (; ; )
            {
                var frame = await reader.ReadAsync(ResultMessageBytes, deadline.Token);
                if (frame is null)
                    break;
                if (receipt is not null)
                    throw new IOException("The browser emitted data after its terminal receipt.");
                using var document = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                UniqueFields(root);
                if (root.ValueKind != JsonValueKind.Object || root.GetProperty("jobId").GetString() != request.JobId)
                    throw new IOException("The browser message belongs to another job.");
                switch (root.GetProperty("type").GetString())
                {
                    case "resource":
                        if (
                            frame.Length > ResourceMessageBytes
                            || expected > request.Limits.Requests
                            || root.GetProperty("id").GetInt32() != expected
                        )
                            throw new IOException("The browser resource sequence exceeded its scope or was replayed.");
                        var id = expected++;
                        var value = root.GetProperty("request");
                        if (value.EnumerateObject().Count() != 2)
                            throw new IOException("Browser requests cannot carry headers, credentials or bodies.");
                        var resource =
                            value.Deserialize<BrowserResourceRequest>(Json)
                            ?? throw new IOException("Missing browser resource.");
                        BrowserResourceReply reply;
                        if (token.IsCancellationRequested || !stillAuthorized())
                            await CancelAsync();
                        if (resources.IsCancellationRequested)
                            reply = new(
                                0,
                                [],
                                "",
                                DateTimeOffset.UtcNow,
                                "The browser job is canceled or its authority was revoked."
                            );
                        else
                        {
                            // Recheck scope here even if a caller supplies a faulty or fictional network session.
                            var uri = BrowserRequest.Address(resource.Url);
                            if (
                                resource.Method != "GET"
                                || !request
                                    .Origins.Select(BrowserRequest.Address)
                                    .Select(BrowserRequest.Origin)
                                    .Contains(BrowserRequest.Origin(uri), StringComparer.Ordinal)
                            )
                                throw new IOException(
                                    "The browser attempted a request outside its approved read scope."
                                );
                            try
                            {
                                reply = await network.FetchAsync(resource, resources.Token);
                            }
                            catch (OperationCanceledException) when (resources.IsCancellationRequested)
                            {
                                reply = new(0, [], "", DateTimeOffset.UtcNow, "The browser resource was canceled.");
                            }
                            if (token.IsCancellationRequested || !stillAuthorized())
                                await CancelAsync();
                            if (resources.IsCancellationRequested)
                                reply = new(
                                    0,
                                    [],
                                    "",
                                    DateTimeOffset.UtcNow,
                                    "The browser response was withheld after cancellation or revocation."
                                );
                        }
                        await WriteAsync(
                            new
                            {
                                type = "resource-result",
                                jobId = request.JobId,
                                id,
                                reply,
                            },
                            ReplyMessageBytes
                        );
                        break;
                    case "result":
                        var payload = root.GetProperty("result");
                        if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > BrowserRequest.ResultBytes)
                            throw new IOException("The browser result exceeded its output allowance.");
                        var result =
                            payload.Deserialize<BrowserResult>(Json)
                            ?? throw new IOException("Missing browser result.");
                        result.Validate(request);
                        if (Volatile.Read(ref cancelSent) != 0 || token.IsCancellationRequested || !stillAuthorized())
                        {
                            // Saved earlier captures are not permission to disclose after the authority fence closes.
                            result = result with
                            {
                                State = "canceled",
                                Captures = [],
                                Blocked = [],
                                Warnings = [],
                                Partial = true,
                                Error = "Browser output was withheld after cancellation or revocation.",
                            };
                        }
                        receipt = new(result, root.GetProperty("browserExited").GetBoolean(), network.Usage);
                        break;
                    default:
                        throw new IOException("Unknown browser protocol message.");
                }
            }
            if (receipt is null)
                throw new IOException("The browser closed without a terminal receipt.");
            if (Volatile.Read(ref cancelSent) != 0 || token.IsCancellationRequested || !stillAuthorized())
                receipt = receipt with
                {
                    Result = receipt.Result with
                    {
                        State = "canceled",
                        Captures = [],
                        Blocked = [],
                        Warnings = [],
                        Partial = true,
                        Error = "Browser output was withheld after cancellation or revocation.",
                    },
                };
            return receipt;
        }
        finally
        {
            await observation.CancelAsync();
            await observer;
            await resources.CancelAsync();
        }
    }

    // Reject duplicate properties recursively: a second jobId or result state must not hide the first one.
    private static void UniqueFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new IOException("The browser message contains duplicate fields.");
                UniqueFields(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                UniqueFields(item);
    }

    private sealed class Frames(Stream stream)
    {
        private readonly byte[] _buffer = new byte[8192];
        private int _position,
            _length;

        public async Task<byte[]?> ReadAsync(int maximum, CancellationToken token)
        {
            using var frame = new MemoryStream();
            for (; ; )
            {
                if (_position == _length)
                {
                    _length = await stream.ReadAsync(_buffer, token);
                    _position = 0;
                    if (_length == 0)
                        return frame.Length == 0
                            ? null
                            : throw new IOException("The browser emitted an incomplete frame.");
                }
                var end = Array.IndexOf(_buffer, (byte)'\n', _position, _length - _position);
                var count = (end < 0 ? _length : end) - _position;
                if (frame.Length + count > maximum)
                    throw new IOException("The browser frame exceeded its bound.");
                frame.Write(_buffer, _position, count);
                _position += count;
                if (end >= 0)
                {
                    _position++;
                    if (frame.Length == 0)
                        throw new IOException("Empty browser protocol frame.");
                    return frame.ToArray();
                }
            }
        }
    }
}
