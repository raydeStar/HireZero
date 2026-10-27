using System.Diagnostics;
using System.Net;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Sandbox;
using HireZero.Integrations.Web;

// Opt-in local E2E: real child processes, entirely fictional HTTP responses, no model or containment claim.
public sealed class BrowserWorkerIntegrationTests
{
    private sealed class Pages(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        public List<string> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Seen.Add(request.RequestUri!.AbsolutePath);
            return answer(request, token);
        }
    }

    private static HttpResponseMessage Text(string content, string type = "text/html") =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, type) };

    private static async Task<BrowserWorkerReceipt> Run(BrowserRequest request, Pages pages, CancellationToken token)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("HIREZERO_TEST_BROWSER_WORKER") == "1",
            "Opt in to the fictional local Chrome worker fixture with HIREZERO_TEST_BROWSER_WORKER=1."
        );
        var chrome = Environment.GetEnvironmentVariable("HIREZERO_TEST_CHROME");
        Assert.True(
            chrome is not null && Path.IsPathFullyQualified(chrome) && File.Exists(chrome),
            "Choose an installed test Chrome executable."
        );
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "HireZero.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("HIREZERO_TEST_NODE") ?? "node")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var machine = start
            .Environment.Where(pair =>
                pair.Key is "PATH" or "SystemRoot" or "WINDIR" or "SystemDrive" or "TEMP" or "TMP"
            )
            .ToArray();
        start.Environment.Clear();
        foreach (var pair in machine)
            start.Environment[pair.Key] = pair.Value;
        start.Environment["HIREZERO_TEST_CHROME"] = chrome;
        start.ArgumentList.Add(Path.Combine(root.FullName, "tests", "browser-worker-fixture.mjs"));
        using var process = new Process { StartInfo = start };
        Assert.True(process.Start());
        using var network = new PublicBrowserNetwork(request, () => true, pages);
        using var diagnostics = new CancellationTokenSource(TimeSpan.FromSeconds(request.Limits.Seconds + 20));
        var error = process.StandardError.ReadToEndAsync(diagnostics.Token);
        try
        {
            var receipt = await BrowserWorkerProtocol.ExchangeAsync(
                request,
                process.StandardOutput.BaseStream,
                process.StandardInput.BaseStream,
                network,
                () => true,
                token
            );
            await process.WaitForExitAsync(diagnostics.Token);
            Assert.True(process.ExitCode == 0, "The fictional worker exits cleanly: " + await error);
            Assert.True(receipt.BrowserExited);
            Assert.True(receipt.Result.ContextClosed);
            receipt.Result.Validate(request);
            if (receipt.Result.State == "completed")
            {
                var proof = Path.Combine(root.FullName, "test-results");
                Directory.CreateDirectory(proof);
                await File.WriteAllTextAsync(
                    Path.Combine(proof, "browser-worker.json"),
                    CompanyJson.Encode(
                        new
                        {
                            at = DateTimeOffset.UtcNow,
                            fixtureOnly = true,
                            productionContainmentQualified = false,
                            state = receipt.Result.State,
                            completedSteps = receipt.Result.CompletedSteps,
                            partial = receipt.Result.Partial,
                            contextClosed = receipt.Result.ContextClosed,
                            browserExited = receipt.BrowserExited,
                            hostNetwork = receipt.Network,
                            driverNetwork = receipt.Result.Network,
                            captureCount = receipt.Result.Captures.Length,
                        }
                    ),
                    diagnostics.Token
                );
                if (receipt.Result.Captures.FirstOrDefault()?.Screenshot is { } screenshot)
                    await File.WriteAllBytesAsync(
                        Path.Combine(proof, "browser-worker-page.jpg"),
                        Convert.FromBase64String(screenshot.Base64),
                        diagnostics.Token
                    );
            }
            return receipt;
        }
        catch (Exception failure)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new InvalidOperationException("Fictional worker boundary failed: " + await error, failure);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await diagnostics.CancelAsync();
        }
    }

    [Fact]
    public async Task TheActualBrokerWorkerAndChromeReturnOneScopedDynamicCapture()
    {
        using var pages = new Pages(
            (r, _) =>
                Task.FromResult(
                    r.RequestUri!.AbsolutePath switch
                    {
                        "/robots.txt" => Text("User-agent: HireZero-source-reader\nAllow: /", "text/plain"),
                        "/" => Text(
                            "<html><head><title>Fictional public studio</title></head><body><main><h1>Paper studio</h1><button type=button id=show>Show services</button><p id=details></p></main><script src='/app.js'></script></body></html>"
                        ),
                        "/app.js" => Text(
                            "document.getElementById('show').onclick=async()=>{const response=await fetch('/services');document.getElementById('details').textContent=await response.text();document.getElementById('details').id='loaded';};",
                            "application/javascript"
                        ),
                        "/services" => Text("Fictional website reviews and stationery.", "text/plain"),
                        "/favicon.ico" => new(HttpStatusCode.NotFound)
                        {
                            Content = new StringContent("", Encoding.UTF8, "image/x-icon"),
                        },
                        _ => throw new InvalidOperationException(
                            "No other page is authorized by this fictional fixture."
                        ),
                    }
                )
        );
        var request = BrowserProtocolTests.Scope() with
        {
            Steps =
            [
                new("navigate", "https://paper.example.invalid/"),
                new("click", "#show"),
                new("wait-for", "#loaded"),
                new("read", "main"),
            ],
        };
        var receipt = await Run(request, pages, TestContext.Current.CancellationToken);
        Assert.Equal("completed", receipt.Result.State);
        Assert.False(receipt.Result.Partial);
        var capture = Assert.Single(receipt.Result.Captures);
        Assert.Contains("Fictional website reviews and stationery.", capture.Text);
        Assert.NotNull(capture.Screenshot);
        Assert.Contains("/robots.txt", pages.Seen);
        Assert.Contains("/app.js", pages.Seen);
        Assert.Contains("/services", pages.Seen);
        Assert.Equal(pages.Seen.Count, receipt.Network.Requests);
        Assert.True(
            receipt.Network.ReceivedBytes > receipt.Result.Network.ReceivedBytes,
            "Host accounting includes the crawl policy, not just fulfilled page bytes."
        );
    }

    [Fact]
    public async Task CancelingDuringAnActualResourceReadStopsTheBrowserWithoutReplaying()
    {
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var pages = new Pages(
            async (r, token) =>
            {
                if (r.RequestUri!.AbsolutePath == "/robots.txt")
                    return new(HttpStatusCode.NotFound);
                await canceled.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Text("This response never arrives.");
            }
        );
        var receipt = await Run(BrowserProtocolTests.Scope(), pages, canceled.Token);
        Assert.Equal("canceled", receipt.Result.State);
        Assert.Empty(receipt.Result.Captures);
        Assert.Equal(new[] { "/robots.txt", "/" }, pages.Seen);
    }
}
