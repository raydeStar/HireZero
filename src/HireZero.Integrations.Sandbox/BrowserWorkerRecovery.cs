using System.Text;
using System.Text.Json;

namespace HireZero.Integrations.Sandbox;

public static partial class BrowserWorkerProtocol
{
    public sealed record RecoveredReceipt(BrowserResult Result, bool BrowserExited);

    // Recovery consumes an exited worker's original stdout. It has no broker and cannot revisit a website.
    public static async Task<RecoveredReceipt> ReadRecoveredAsync(
        BrowserRequest request,
        Stream logs,
        CancellationToken token
    )
    {
        request.Validate();
        var reader = new Frames(logs);
        var expected = 1;
        RecoveredReceipt? receipt = null;
        for (; ; )
        {
            var frame = await reader.ReadAsync(ResultMessageBytes, token);
            if (frame is null)
                break;
            if (receipt is not null)
                throw new IOException("Recovered browser logs contain data after the terminal receipt.");
            using var document = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            UniqueFields(root);
            if (root.ValueKind != JsonValueKind.Object || root.GetProperty("jobId").GetString() != request.JobId)
                throw new IOException("Recovered browser logs belong to another job.");
            switch (root.GetProperty("type").GetString())
            {
                case "resource":
                    if (
                        frame.Length > ResourceMessageBytes
                        || expected > request.Limits.Requests
                        || root.GetProperty("id").GetInt32() != expected++
                    )
                        throw new IOException("Recovered browser resource sequence is invalid.");
                    var value = root.GetProperty("request");
                    if (value.EnumerateObject().Count() != 2)
                        throw new IOException("Recovered browser request fields exceed scope.");
                    var resource =
                        value.Deserialize<BrowserResourceRequest>(Json)
                        ?? throw new IOException("Missing recovered resource request.");
                    var uri = BrowserRequest.Address(resource.Url);
                    if (
                        resource.Method != "GET"
                        || !request
                            .Origins.Select(BrowserRequest.Address)
                            .Select(BrowserRequest.Origin)
                            .Contains(BrowserRequest.Origin(uri), StringComparer.Ordinal)
                    )
                        throw new IOException("Recovered browser resource leaves the approved websites.");
                    break;
                case "result":
                    var payload = root.GetProperty("result");
                    if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > BrowserRequest.ResultBytes)
                        throw new IOException("Recovered browser result exceeds its allowance.");
                    var result =
                        payload.Deserialize<BrowserResult>(Json)
                        ?? throw new IOException("Missing recovered browser result.");
                    result.Validate(request);
                    receipt = new(result, root.GetProperty("browserExited").GetBoolean());
                    break;
                default:
                    throw new IOException("Unknown recovered browser message.");
            }
        }
        return receipt ?? throw new IOException("Recovered browser logs contain no complete result.");
    }
}
