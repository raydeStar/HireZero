using System.Text;
using System.Text.Json;
using HireZero.Core;
using HireZero.Integrations.Sandbox;

public sealed class BrowserProtocolTests
{
    private const string Origin = "https://paper.example.invalid";

    internal static BrowserRequest Scope() =>
        new(
            Guid.NewGuid().ToString("N"),
            "fictional-browser-company",
            [Origin],
            [new("navigate", Origin + "/"), new("read", "main")],
            new(20, 20, 1024 * 1024)
        );

    private static BrowserResult Result(BrowserRequest request) =>
        new(
            request.JobId,
            "completed",
            2,
            false,
            [
                new(
                    2,
                    Origin + "/",
                    DateTimeOffset.UtcNow,
                    "Fictional public page",
                    "Public evidence is not authority.",
                    false,
                    []
                ),
            ],
            [],
            [],
            true,
            new(1, 0),
            10
        );

    private static string Resource(BrowserRequest request, int id = 1, string? url = null, string method = "GET") =>
        CompanyJson.Encode(
            new
            {
                type = "resource",
                jobId = request.JobId,
                id,
                request = new { url = url ?? Origin + "/", method },
            }
        );

    private static string Terminal(BrowserRequest request, BrowserResult? result = null, bool exited = true) =>
        CompanyJson.Encode(
            new
            {
                type = "result",
                jobId = request.JobId,
                result = result ?? Result(request),
                browserExited = exited,
            }
        );

    private static MemoryStream Frames(params string[] messages) =>
        new(Encoding.UTF8.GetBytes(string.Join('\n', messages) + "\n"));

    private sealed class Network(Func<CancellationToken, Task<BrowserResourceReply>>? fetch = null)
        : IBrowserNetworkSession
    {
        public int Calls;
        public BrowserNetworkUsage Usage => new(Calls, 7, 0);

        public Task<BrowserResourceReply> FetchAsync(BrowserResourceRequest request, CancellationToken token)
        {
            Calls++;
            return fetch?.Invoke(token)
                ?? Task.FromResult(
                    new BrowserResourceReply(
                        200,
                        new() { ["Content-Type"] = "text/plain" },
                        Convert.ToBase64String(Encoding.UTF8.GetBytes("Fictional public response")),
                        DateTimeOffset.UtcNow
                    )
                );
        }

        public void Dispose() { }
    }

    private sealed class OnEndStream(byte[] bytes, Action onEnd) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var count = await base.ReadAsync(buffer, token);
            if (count == 0)
                onEnd();
            return count;
        }
    }

    [Fact]
    public async Task ScopedResourcesAndOneTerminalReceiptCrossTheProtocolWithoutReplay()
    {
        var request = Scope();
        using var incoming = Frames(Resource(request), Terminal(request));
        using var outgoing = new MemoryStream();
        using var network = new Network();
        var receipt = await BrowserWorkerProtocol.ExchangeAsync(
            request,
            incoming,
            outgoing,
            network,
            () => true,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("completed", receipt.Result.State);
        Assert.True(receipt.BrowserExited);
        Assert.Single(receipt.Result.Captures);
        Assert.Equal(1, receipt.Network.Requests);
        Assert.Equal(7, receipt.Network.ReceivedBytes);
        var written = Encoding
            .UTF8.GetString(outgoing.ToArray())
            .Trim()
            .Split('\n')
            .Select(text => JsonDocument.Parse(text).RootElement.Clone())
            .ToArray();
        Assert.Equal(2, written.Length);
        Assert.Equal("start", written[0].GetProperty("type").GetString());
        Assert.Equal(request.JobId, written[0].GetProperty("request").GetProperty("jobId").GetString());
        Assert.Equal("resource-result", written[1].GetProperty("type").GetString());
        Assert.Equal(1, written[1].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task ForeignReplayedAndOverScopedRequestsNeverReachTheBroker()
    {
        var request = Scope();
        foreach (
            var message in new[]
            {
                Resource(request with { JobId = Guid.NewGuid().ToString("N") }),
                Resource(request, 2),
                Resource(request, method: "POST"),
                Resource(request, url: "https://outside.example.invalid/"),
                CompanyJson.Encode(
                    new
                    {
                        type = "resource",
                        jobId = request.JobId,
                        id = 1,
                        request = new
                        {
                            url = Origin + "/",
                            method = "GET",
                            headers = new { Authorization = "fictional" },
                        },
                    }
                ),
                Resource(request).Replace("\"id\":1", "\"id\":1,\"id\":2", StringComparison.Ordinal),
                CompanyJson.Encode(new { type = "run-shell", jobId = request.JobId }),
            }
        )
        {
            using var incoming = Frames(message);
            using var outgoing = new MemoryStream();
            using var network = new Network();
            await Assert.ThrowsAsync<IOException>(() =>
                BrowserWorkerProtocol.ExchangeAsync(
                    request,
                    incoming,
                    outgoing,
                    network,
                    () => true,
                    TestContext.Current.CancellationToken
                )
            );
            Assert.Equal(0, network.Calls);
        }
    }

    [Fact]
    public async Task DuplicateResourcesAndDataAfterCompletionAreProtocolFailures()
    {
        var request = Scope();
        foreach (
            var messages in new[]
            {
                new[] { Resource(request), Resource(request) },
                new[] { Terminal(request), Resource(request) },
                new[] { Terminal(request), Terminal(request) },
            }
        )
        {
            using var incoming = Frames(messages);
            using var outgoing = new MemoryStream();
            using var network = new Network();
            await Assert.ThrowsAsync<IOException>(() =>
                BrowserWorkerProtocol.ExchangeAsync(
                    request,
                    incoming,
                    outgoing,
                    network,
                    () => true,
                    TestContext.Current.CancellationToken
                )
            );
            Assert.Equal(messages[0] == Resource(request) ? 1 : 0, network.Calls);
        }
    }

    [Fact]
    public async Task IncompleteOversizedOrMissingReceiptsAreNeverSuccessful()
    {
        var request = Scope();
        foreach (
            var bytes in new[]
            {
                Array.Empty<byte>(),
                Encoding.UTF8.GetBytes(Terminal(request)),
                Encoding.UTF8.GetBytes(new string('x', BrowserWorkerProtocol.ResultMessageBytes + 1)),
            }
        )
        {
            using var incoming = new MemoryStream(bytes);
            using var outgoing = new MemoryStream();
            using var network = new Network();
            await Assert.ThrowsAsync<IOException>(() =>
                BrowserWorkerProtocol.ExchangeAsync(
                    request,
                    incoming,
                    outgoing,
                    network,
                    () => true,
                    TestContext.Current.CancellationToken
                )
            );
            Assert.Equal(0, network.Calls);
        }
    }

    [Fact]
    public async Task RevocationWithholdsBothInflightResourceBytesAndEarlierCaptures()
    {
        var request = Scope();
        var authorized = true;
        using var network = new Network(_ =>
        {
            authorized = false;
            return Task.FromResult(
                new BrowserResourceReply(
                    200,
                    [],
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("Do not disclose this fictional response")),
                    DateTimeOffset.UtcNow
                )
            );
        });
        using var incoming = Frames(Resource(request), Terminal(request));
        using var outgoing = new MemoryStream();
        var receipt = await BrowserWorkerProtocol.ExchangeAsync(
            request,
            incoming,
            outgoing,
            network,
            () => authorized,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("canceled", receipt.Result.State);
        Assert.Empty(receipt.Result.Captures);
        Assert.Contains("\"type\":\"cancel\"", Encoding.UTF8.GetString(outgoing.ToArray()));
        Assert.DoesNotContain(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("Do not disclose this fictional response")),
            Encoding.UTF8.GetString(outgoing.ToArray())
        );
    }

    [Fact]
    public async Task ARevocationAtEndOfStreamStillClosesTheOutputFence()
    {
        var request = Scope();
        var authorized = true;
        using var incoming = new OnEndStream(
            Encoding.UTF8.GetBytes(Terminal(request) + "\n"),
            () => authorized = false
        );
        using var outgoing = new MemoryStream();
        using var network = new Network();
        var receipt = await BrowserWorkerProtocol.ExchangeAsync(
            request,
            incoming,
            outgoing,
            network,
            () => authorized,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("canceled", receipt.Result.State);
        Assert.Empty(receipt.Result.Captures);
    }

    [Fact]
    public async Task ProcessCleanupUncertaintyIsNotPromotedToRelease()
    {
        var request = Scope();
        using var incoming = Frames(Terminal(request, exited: false));
        using var outgoing = new MemoryStream();
        using var network = new Network();
        var receipt = await BrowserWorkerProtocol.ExchangeAsync(
            request,
            incoming,
            outgoing,
            network,
            () => true,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("completed", receipt.Result.State);
        Assert.True(receipt.Result.ContextClosed);
        Assert.False(receipt.BrowserExited);
    }

    [Fact]
    public void CapturesMustMatchTheJobReadingStepsOriginsAndOutputBounds()
    {
        var request = Scope();
        var valid = Result(request);
        valid.Validate(request);
        foreach (
            var result in new[]
            {
                valid with
                {
                    JobId = Guid.NewGuid().ToString("N"),
                },
                valid with
                {
                    Captures = [],
                },
                valid with
                {
                    CompletedSteps = 1,
                },
                valid with
                {
                    Captures = [valid.Captures[0] with { Step = 1 }],
                },
                valid with
                {
                    Captures = [valid.Captures[0] with { Url = "https://outside.example.invalid/" }],
                },
                valid with
                {
                    Captures = [valid.Captures[0] with { Text = new string('x', 8001) }],
                },
                valid with
                {
                    Captures =
                    [
                        valid.Captures[0] with
                        {
                            Screenshot = new(
                                "image/jpeg",
                                Convert.ToBase64String(Encoding.UTF8.GetBytes("<script>not an image</script>"))
                            ),
                        },
                    ],
                },
                valid with
                {
                    Warnings = ["Some evidence was omitted"],
                },
                valid with
                {
                    Network = new(1, request.Limits.Bytes + 1),
                },
            }
        )
            Assert.Throws<WorkspaceException>(() => result.Validate(request));
    }
}
