using System.Net;
using HireZero.Core;
using HireZero.Roles.Developer;
using HireZero.Roles.Marketing;
using HireZero.Roles.Ceo;
using HireZero.Integrations.Codex;

// Explicit composition profile. Native Codex inspection is opt-in; legacy workers are never registered.
public static class CompanyWorkspace
{
    public const string Flag="--company-local";
    public const string CodexFlag="--company-codex";
    public static CompanyRole[] Definitions()=>[CompanyDeveloper.Definition,CompanyMarketing.Definition,CompanyCeo.Definition];
    public static WebApplication? TryCreate(string[] args,string owner,bool localOwner,string root,ICompanyModel? fixture=null,string? fixtureUrl=null)
    {
        if(!args.Contains(Flag))return null;
        if(!localOwner)throw new InvalidOperationException("Company local mode requires explicit local owner mode.");
        if(fixtureUrl is null&&string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HIREZERO_DATA_ROOT")))throw new InvalidOperationException("Select an explicit new HIREZERO_DATA_ROOT for company-local mode.");
        if(HeldWorkspace.Requested(Environment.GetEnvironmentVariable(HeldWorkspace.Variable)))throw new InvalidOperationException("Execution hold takes precedence. Use the held profile or explicitly select company-local with a separate data root.");
        var url=fixtureUrl??"http://127.0.0.1:5065";
        var uri=new Uri(url);if(uri.Scheme!="http"||!IPAddress.TryParse(uri.Host,out var ip)||!IPAddress.IsLoopback(ip))throw new InvalidOperationException("Company mode must use a loopback listener.");
        var builder=WebApplication.CreateBuilder(args.Where(a=>a!=Flag&&a!=CodexFlag).ToArray());
        builder.WebHost.ConfigureKestrel(o=>{o.Configure(new ConfigurationBuilder().Build(),false);o.Limits.MaxRequestBodySize=16384;o.Listen(ip,uri.Port);});
        Directory.CreateDirectory(root);
        var database=Path.Combine(root,"workspace.sqlite");
        if(File.Exists(database))
        {
            using var check=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=database,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());check.Open();using var query=check.CreateCommand();query.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='co_company'";
            if(Convert.ToInt32(query.ExecuteScalar())==0)throw new InvalidOperationException("Company-local mode refuses an existing legacy database. Select a new data root.");
        }
        // FileShare.None owns only this new company scheduler; no process enumeration or runtime manipulation.
        var ownership=new FileStream(Path.Combine(root,"company-scheduler.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            var routing=new CompanyModelRouting(Environment.GetEnvironmentVariable("HIREZERO_COMPANY_TEXT_MODEL")??"openai/gpt-6-luna",Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODING_MODEL")??"openai/gpt-6.1-sol");routing.Validate();
            ICompanyModel model=fixture??(args.Contains(CodexFlag)?new CompanyCodexConnection(new(
                Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_EXECUTABLE")??throw new InvalidOperationException("Pin the selected Codex executable."),
                Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_IDENTITY")??throw new InvalidOperationException("Pin the selected Codex executable bundle identity."),Path.Combine(root,"codex-scratch"),
                Home:Environment.GetEnvironmentVariable("HIREZERO_COMPANY_CODEX_HOME")??throw new InvalidOperationException("Select the dedicated native Codex profile home.")),routing):new UnavailableCompanyModel());
            var store=new CompanyStore(Path.Combine(root,"workspace.sqlite"),owner,Definitions(),routing,(model as ICompanyMeasuredModel)?.RuntimeIdentity);
            builder.Services.AddSingleton(store);builder.Services.AddSingleton<ICompanyModel>(model);builder.Services.AddSingleton<CompanyScheduler>();
            builder.Services.AddHostedService(s=>s.GetRequiredService<CompanyScheduler>());
            var app=builder.Build();app.Lifetime.ApplicationStopped.Register(ownership.Dispose);
            app.UseWorkspaceCorrelation();
            app.Use(async(c,next)=>
            {
                c.Response.Headers.CacheControl="no-store";c.Response.Headers["X-Content-Type-Options"]="nosniff";c.Response.Headers["Referrer-Policy"]="no-referrer";
                c.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
                // Fixture port zero is bound by Kestrel, but Host still proves the chosen profile's authority.
                var expected=fixtureUrl is null?uri.Authority:"127.0.0.1:5065";
                if(c.Connection.RemoteIpAddress is not {} remote||!IPAddress.IsLoopback(remote)||c.Request.Host.Value!=expected||c.Request.Headers.Keys.Any(k=>k.StartsWith("X-Forwarded-",StringComparison.OrdinalIgnoreCase)||k.StartsWith("Tailscale-",StringComparison.OrdinalIgnoreCase)||k.Equals("Forwarded",StringComparison.OrdinalIgnoreCase)))
                {c.Response.StatusCode=403;return;}
                if(c.Request.Method is not("GET" or "HEAD")&&(c.Request.Headers.Origin!="http://"+expected||c.Request.Headers["X-Dev-Request"]!="1"||!c.Request.HasJsonContentType())){c.Response.StatusCode=403;return;}
                c.Items["login"]=owner;c.Items["role"]="owner";
                try{await next();}
                catch(WorkspaceException e){c.Response.StatusCode=e.Status;await c.Response.WriteAsJsonAsync(new{error=e.Message,code="company-command-refused"});}
                catch(System.Text.Json.JsonException){c.Response.StatusCode=422;await c.Response.WriteAsJsonAsync(new{error="Invalid structured input."});}
            });
            app.MapGet("/health",()=>Results.Ok(new{status="healthy",companyLocal=true,modelExecution=model.Qualified}));
            app.MapGet("/api/session",()=>Results.Ok(new{login=owner,role="owner",accessMode="local",roles=Array.Empty<string>(),agentModel=model is ICompanyMeasuredModel?CompanyModelRouting.Native(routing.TextModel)+" (manual only)":"Unavailable",agentReasoning="",companyEnabled=true,executionHeld=false}));
            app.MapGet("/api/company",()=>Results.Ok(new{capabilities=new{localManagement=true,modelExecution=model.Qualified,measuredOneJob=model is ICompanyMeasuredModel,providerStatus=model.Status,modelRouting=routing,ceoEnabled=true,meetingsEnabled=true,legacyDeveloperAvailable=false},data=store.Dashboard()}));
            app.MapGet("/api/company/runtime",async(CancellationToken token)=>model is CompanyCodexConnection connection?Results.Ok(await connection.InspectAsync(token)):Results.Json(new{error="Select the explicit Codex connection profile."},statusCode:503));
            app.MapGet("/api/company/feed",(long? after,int? limit)=>Results.Ok(store.Feed(after??0,limit??100)));
            app.MapGet("/api/company/employees/{employee}/threads/{thread}",(string employee,string thread)=>Results.Ok(store.Thread(employee,thread)));
            app.MapGet("/api/company/tasks/{id}",(string id)=>Results.Ok(store.TaskDetail(id)));
            app.MapGet("/api/company/measured/{id}",(string id)=>Results.Ok(store.MeasuredDetail(id)));
            app.MapGet("/api/company/operations/{id}",(string id)=>store.Operation(id) is {} result?Results.Content(result,"application/json"):Results.NotFound());
            app.MapPost("/api/company/commands",async(System.Text.Json.JsonElement body)=>
            {
                var command=CompanyJson.Decode<CompanyCommand>(body.GetRawText());
                if(command.Action=="measured.run")
                {
                    if(model is not ICompanyMeasuredModel measured)throw new WorkspaceException("A pinned measured runtime is not configured.");
                    var receipt=await app.Services.GetRequiredService<CompanyScheduler>().RunMeasuredOnceAsync(command,measured,app.Lifetime.ApplicationStopping);
                    return Results.Content(receipt,"application/json");
                }
                return Results.Content(store.Apply(command),"application/json");
            });
            app.MapFallback("/api/{**path}",()=>Results.Json(new{error="This capability is unavailable in company-local mode."},statusCode:503));
            app.UseDefaultFiles();app.UseStaticFiles();app.MapFallbackToFile("index.html");return app;
        }
        catch{ownership.Dispose();throw;}
    }
}
