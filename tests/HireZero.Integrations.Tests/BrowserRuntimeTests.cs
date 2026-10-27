using System.Net;
using System.Text;
using HireZero.Core;
using HireZero.Integrations.Sandbox;
using HireZero.Integrations.Web;

// Explicit operator qualification only. Routine checks never start an engine or provision a real browser.
public sealed class BrowserRuntimeTests
{
    private sealed class Pages(Action cancel) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Assert.Equal("paper.example.invalid", request.RequestUri!.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            if (request.RequestUri.AbsolutePath == "/robots.txt")
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent("User-agent: *\nAllow: /", Encoding.UTF8, "text/plain"),
                };
            if (request.RequestUri.AbsolutePath == "/cancel")
            {
                cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    <html><head><title>Fictional browser qualification</title></head><body>
                    <button id="show" onclick="document.querySelector('main').prepend('Fictional approved details. ')">Show</button>
                    <main>Fictional report.
                    """
                        + new string('x', 12000)
                        + """
                        </main><script>
                        fetch('https://outside.example.invalid/blocked').catch(()=>{
                          const marker=document.createElement('p'); marker.id='scope-checked';document.body.append(marker);
                        });
                        </script></body></html>
                        """,
                    Encoding.UTF8,
                    "text/html"
                ),
            };
        }
    }

    [Fact]
    public async Task ThePinnedBrowserRunsBoundedItinerariesAndReleasesItsRealResources()
    {
        var image = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_IMAGE");
        Assert.SkipWhen(
            string.IsNullOrEmpty(image),
            "Set HIREZERO_BROWSER_TEST_IMAGE only for authorized isolated-runtime qualification."
        );
        var executable = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_DOCKER");
        var endpoint = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_ENDPOINT");
        var installation = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_INSTALLATION");
        var receiptPath = Environment.GetEnvironmentVariable("HIREZERO_BROWSER_TEST_RECEIPT");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.False(string.IsNullOrWhiteSpace(endpoint));
        Assert.True(Guid.TryParseExact(installation, "N", out _));
        Assert.True(receiptPath is not null && Path.IsPathFullyQualified(receiptPath));
        Assert.False(File.Exists(receiptPath), "Choose a new private receipt path; evidence is never overwritten.");
        var token = TestContext.Current.CancellationToken;
        var commands = new DockerCommands(executable, endpoint);
        var profile = await DockerSandbox.InspectEngineAsync(commands, token);
        Assert.NotNull(profile);
        var executableHash = SandboxQualification.ExecutableHash(executable);
        var authorized = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var runtime = new DockerBrowser(
            commands,
            image!,
            installation!,
            profile.Id,
            (request, allowed) =>
                new PublicBrowserNetwork(
                    request,
                    allowed,
                    new Pages(() =>
                    {
                        authorized = false;
                        cancellation.Cancel();
                    })
                )
        );
        var resources = new Dictionary<string, (BrowserRequest Request, BrowserExecution? Receipt)>();
        async Task<BrowserExecution> Run(BrowserRequest request, CancellationToken lifetime)
        {
            resources.Add(request.JobId, (request, null));
            var result = await runtime.ExecuteAsync(request, () => authorized, lifetime);
            resources[request.JobId] = (request, result);
            return result;
        }
        try
        {
            Assert.True(
                await runtime.AvailableAsync(token),
                "The explicit image and Linux runsc profile must be available; no fallback is permitted."
            );
            var request = new BrowserRequest(
                Guid.NewGuid().ToString("N"),
                "co-browser-qualification",
                ["https://paper.example.invalid"],
                [
                    new("navigate", "https://paper.example.invalid/"),
                    new("click", "#show"),
                    new("wait-for", "#scope-checked"),
                    new("read", "main"),
                ],
                new(30)
            );
            var result = await Run(request, token);
            Assert.True(result.State == "completed", "Fictional browser result: " + CompanyJson.Encode(result));
            Assert.True(result.CreationConfirmed);
            Assert.True(result.BrowserExited);
            Assert.True(result.Result!.ContextClosed);
            result.Result.Validate(request);
            var capture = Assert.Single(result.Result.Captures);
            Assert.Contains("Fictional approved details.", capture.Text);
            Assert.True(capture.Truncated);
            Assert.NotNull(capture.Screenshot);
            Assert.Contains(
                result.Result.Blocked,
                r => r.Url.Contains("outside.example.invalid", StringComparison.Ordinal)
            );
            Assert.True(result.Network!.Requests > 0);
            var recovered = await runtime.RecoverAsync(request, token);
            Assert.Equal(CompanyJson.Encode(result.Result), CompanyJson.Encode(recovered.Result));
            Assert.Null(recovered.Network);
            Assert.True((await runtime.ReleaseAsync(request, result.Release.ResourceId, true, token)).Confirmed);

            var deadline = request with
            {
                JobId = Guid.NewGuid().ToString("N"),
                Limits = new(8),
                Steps =
                [
                    new("navigate", "https://paper.example.invalid/"),
                    new("wait-for", "#never-present"),
                    new("read", "main"),
                ],
            };
            var expired = await Run(deadline, token);
            Assert.Equal("timed-out", expired.State);
            Assert.True(expired.BrowserExited);
            Assert.Empty(expired.Result!.Captures);
            Assert.True(expired.WallMilliseconds <= 30000);

            var canceled = await Run(
                request with
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    Steps = [new("navigate", "https://paper.example.invalid/cancel"), new("read", "body")],
                },
                cancellation.Token
            );
            Assert.False(authorized, "The broker must observe the deliberately withdrawn authority.");
            Assert.NotEqual("completed", canceled.State);
            Assert.True(canceled.Result is null || canceled.Result.Captures.Length == 0);
        }
        finally
        {
            var released = new List<bool>();
            foreach (var resource in resources.Values)
            {
                // Keep operator-only evidence before cleanup; the tenant must never receive daemon paths.
                try
                {
                    using var inspect = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    var snapshot = await commands.RunAsync(
                        ["container", "inspect", resource.Receipt?.Release.ResourceId ?? ""],
                        null,
                        TimeSpan.FromSeconds(10),
                        128 * 1024,
                        inspect.Token
                    );
                    if (snapshot.ExitCode == 0)
                    {
                        using var diagnostic = new FileStream(
                            receiptPath + ".resource-" + resource.Request.JobId + ".json",
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None
                        );
                        await diagnostic.WriteAsync(Encoding.UTF8.GetBytes(snapshot.Output), inspect.Token);
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    // A failed photograph must not keep the laboratory occupied: cleanup has its own deadline.
                }
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var receipt = resource.Receipt ?? await runtime.RecoverAsync(resource.Request, cleanup.Token);
                released.Add(
                    (
                        await runtime.ReleaseAsync(
                            resource.Request,
                            receipt.Release.ResourceId,
                            receipt.CreationConfirmed,
                            cleanup.Token
                        )
                    ).Confirmed
                );
            }
            Assert.All(
                released,
                confirmed => Assert.True(confirmed, "Qualification did not confirm removal of every owned resource.")
            );
        }
        Assert.Equal(profile, await DockerSandbox.InspectEngineAsync(commands, token));
        Assert.Equal(executableHash, SandboxQualification.ExecutableHash(executable));
        var now = DateTimeOffset.UtcNow;
        var qualification = new BrowserQualification(
            BrowserQualification.CurrentProtocol,
            image!.ToLowerInvariant(),
            installation!.ToLowerInvariant(),
            endpoint!,
            executableHash,
            profile,
            now,
            now.AddDays(7),
            BrowserQualification.RequiredChecks
        );
        using var output = new FileStream(receiptPath!, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await output.WriteAsync(Encoding.UTF8.GetBytes(CompanyJson.Encode(qualification)), token);
        output.Flush(flushToDisk: true);
    }
}
