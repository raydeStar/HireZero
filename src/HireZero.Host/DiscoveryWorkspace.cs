using System.Net;
using HireZero.Core;
using HireZero.Roles.Developer;

// An explicit local observation root. It cannot resolve or dispatch any execution provider.
public static class DiscoveryWorkspace
{
    public const string Variable = "HIREZERO_DISCOVERY_ONLY";
    public static WebApplication? TryCreate(string[] args, string owner, bool localOwner, string dataRoot,
        GitHubReviewClient? fixtureGitHub = null, string? fixtureUrl = null)
    {
        var mode = Environment.GetEnvironmentVariable(Variable);
        if (mode is null or "" or "0") return null;
        if (mode != "1" || !localOwner) throw new InvalidOperationException("Discovery requires explicit local owner mode.");
        if (fixtureUrl is not null && (!Uri.TryCreate(fixtureUrl, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            !IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address)))
            throw new InvalidOperationException("Discovery fixtures must use loopback.");
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
            options.Limits.MaxRequestBodySize = 1024;
            if (fixtureUrl is not null) { var fixture = new Uri(fixtureUrl); options.Listen(IPAddress.Parse(fixture.Host), fixture.Port); }
            else { options.Listen(IPAddress.Loopback, 5064); options.Listen(IPAddress.IPv6Loopback, 5064); }
        });
        var database = Path.Combine(dataRoot, "workspace.sqlite");
        var reviews = new GitHubReviewStore(database);
        var discovery = new GitHubDiscovery(database, fixtureGitHub ?? new GitHubReviewClient(), reviews);
        builder.Services.AddSingleton(discovery);
        var app = builder.Build();
        app.UseWorkspaceCorrelation();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
            if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
                context.Request.Host.Value != "127.0.0.1:5064" || context.Request.Headers.Keys.Any(key =>
                    key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Tailscale-", StringComparison.OrdinalIgnoreCase) || key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)) ||
                context.Request.Headers.Origin is { Count: > 0 } origins && origins.ToString() != "http://127.0.0.1:5064")
            { context.Response.StatusCode = 403; return; }
            if (context.Request.Method is not ("GET" or "HEAD") &&
                (context.Request.Method != "POST" || context.Request.Path != "/api/discovery/scan"))
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new { code = "execution-held", error = "Execution remains held. Discovery grants no repair or publication approval." });
                return;
            }
            await next();
        });
        var runtime = new WorkspaceRuntime(owner, Path.GetFullPath(dataRoot), null, [], true) { ExecutionHeld = true };
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", executionHeld = true, discoveryOnly = true }));
        app.MapGet("/api/runtime", () => Results.Ok(new { runtime.Version, runtime.OwnerLogin, runtime.DataRoot,
            runtime.TailnetAddress, runtime.TailnetNames, runtime.LocalOwnerEnabled, runtime.ExecutionHeld, discoveryOnly = true }));
        app.MapGet("/api/session", () => Results.Ok(new { login = owner, role = "owner", accessMode = "local", executionHeld = true, discoveryOnly = true }));
        app.MapGet("/api/discovery", () => Results.Ok(discovery.Read()));
        app.MapPost("/api/discovery/scan", async (CancellationToken token) =>
        {
            try { return Results.Ok(await discovery.ScanAsync(token)); }
            catch (Exception error) when (error is WorkspaceException or System.Text.Json.JsonException or IOException or InvalidOperationException)
            { return Results.Json(new { error = "Discovery could not complete. Verify GitHub CLI sign-in and access to configured repositories. Existing results were retained." }, statusCode: 503); }
        });
        app.MapGet("/", () => Results.Content(Page, "text/html"));
        app.MapGet("/discovery.css", () => Results.Content(Style, "text/css"));
        app.MapGet("/discovery.js", () => Results.Content(Script, "text/javascript"));
        app.MapFallback("/api/{**path}", () => Results.StatusCode(503));
        return app;
    }

    private const string Page = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>HireZero — GitHub discovery</title><link rel="stylesheet" href="/discovery.css"><script src="/discovery.js" defer></script></head>
        <body><main><header><div class="eyebrow">HIREZERO / DEVELOPER</div><h1>A fresh look at your GitHub PRs</h1><p>Read-only discovery is ready. Repairs, model turns, pushes and merges remain held and require fresh approval.</p></header>
        <section class="controls"><button id="scan">Scan configured repositories</button><span id="time">No scan yet</span></section><p id="status" role="status">Loading saved observations…</p><section id="findings"></section>
        <footer>Only your open PRs in previously configured repositories are checked. Findings are observations, with no execution consent. Assigned-issue discovery remains disabled pending its existing review. Previous work and history are preserved in the local database.</footer></main></body></html>
        """;
    private const string Style = """
        :root{font-family:system-ui,sans-serif;color:#e9edf5;background:#10151d}body{margin:0}main{max-width:1000px;margin:70px auto;padding:0 28px}header{max-width:760px}.eyebrow{font-size:13px;letter-spacing:.12em;color:#92a4bc}h1{font-size:40px;letter-spacing:-.035em;line-height:1.15}p,footer{line-height:1.6;color:#bcc8d9}.controls{display:flex;align-items:center;gap:24px;padding:26px 0}button{background:#e9edf5;border:0;border-radius:6px;padding:14px 20px;color:#10151d;font:600 15px system-ui;cursor:pointer}button:disabled{opacity:.6;cursor:wait}article{border:1px solid #344253;border-radius:8px;padding:22px;margin:16px 0;background:#19222e}h2{font-size:20px;margin:0 0 10px}a{color:#acd0ff}code{color:#a9b7ca}footer{border-top:1px solid #344253;padding-top:24px;margin-top:36px;font-size:14px}#time{font-size:14px;color:#92a4bc}
        """;
    private const string Script = """
        const button=document.querySelector('#scan'),status=document.querySelector('#status'),findings=document.querySelector('#findings');
        function render(data){status.textContent=data.note;document.querySelector('#time').textContent=data.checkedAt?'Checked '+new Date(data.checkedAt).toLocaleString():'No scan yet';findings.replaceChildren();for(const item of data.findings){const card=document.createElement('article'),heading=document.createElement('h2'),link=document.createElement('a'),reason=document.createElement('p'),revision=document.createElement('code');link.href='https://github.com/'+item.repository+'/pull/'+item.number;link.textContent=item.repository+' #'+item.number+' — '+item.title;link.target='_blank';link.rel='noreferrer';heading.append(link);reason.textContent=item.reason+' · Needs fresh approval for diagnosis or repair';revision.textContent='Observed revision '+item.headSha;card.append(heading,reason,revision);findings.append(card)}if(!data.findings.length&&data.checkedAt){const empty=document.createElement('article');empty.textContent='No actionable PR findings returned. Check the scan note for unavailable sources.';findings.append(empty)}}
        async function load(){const response=await fetch('/api/discovery');if(response.ok)render(await response.json());else status.textContent='Saved discovery results are unavailable.'}
        button.addEventListener('click',async()=>{button.disabled=true;status.textContent='Checking GitHub metadata in configured repositories…';try{const response=await fetch('/api/discovery/scan',{method:'POST'});const data=await response.json();if(response.ok)render(data);else status.textContent=data.error}catch{status.textContent='Discovery did not complete. Refresh to see the last saved results.'}finally{button.disabled=false}});load();
        """;
}
