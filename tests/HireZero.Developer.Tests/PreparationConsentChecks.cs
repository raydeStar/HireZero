using HireZero.Core;
using HireZero.Roles.Developer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

public static class PreparationConsentChecks
{
    sealed class Model : IModelGateway
    {
        public int Calls;
        public bool Block;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string DefaultModel => "fake";
        public string Reasoning => "none";

        public void Prepare(string cell) { }

        public void Stop(string cell) { }

        public Task<string> CompleteAsync(
            string cell,
            string instructions,
            string prompt,
            CancellationToken token,
            string? model = null,
            int maxTokens = 900
        )
        {
            Calls++;
            if (Block)
            {
                Entered.SetResult();
                return Result.Task;
            }
            return Task.FromResult(Answer);
        }

        public const string Answer =
            "{\"stage\":\"gathering\",\"blocker\":\"Missing criteria\",\"nextAction\":\"Ask for criteria\",\"questions\":[]}";
    }

    sealed class Clients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new Handler()) { BaseAddress = new Uri("https://fixture.invalid") };

        sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken token
            ) =>
                Task.FromResult(
                    new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") }
                );
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "preparation-consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "test.sqlite");
            var requests = new RequestStore(db);
            var model = new Model();
            var owner = new WorkspaceOwner(WorkspaceOwner.LocalIdentity);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
            builder.Services.AddSingleton(requests);
            builder.Services.AddSingleton(owner);
            builder.Services.AddSingleton<IModelGateway>(model);
            new DeveloperRole().AddServices(builder.Services, new RoleContext(root, db));
            builder.Services.AddSingleton<IHttpClientFactory>(new Clients());
            await using var app = builder.Build();
            new DeveloperRole().MapEndpoints(app);
            var endpoint = ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Single(item => item.RoutePattern.RawText == "/api/requests/{id}/preparation/suggest");
            DefaultHttpContext Context(string id)
            {
                var context = new DefaultHttpContext { RequestServices = app.Services };
                context.Items["role"] = "owner";
                context.Request.Method = "POST";
                context.Request.RouteValues["id"] = id;
                context.Response.Body = new MemoryStream();
                return context;
            }
            var triage = new RequestTriageService(requests, new TriageStore(db), model);
            foreach (var stage in new[] { "pending", "declined", "revoked", "allowed", "declined-owner" })
            {
                var login = stage == "declined-owner" ? WorkspaceOwner.LocalIdentity : stage + "@example.invalid";
                requests.SetPerson(login, "trusted", false);
                var request = requests.CreateRequest(login, "Example", "Question", "Question", stage);
                if (stage is "allowed" or "revoked")
                    requests.AllowAgent(request.Id);
                if (stage is "declined" or "declined-owner")
                    triage.Decide(request.Id, false, null);
                if (stage == "revoked")
                    requests.SetPerson(login, "trusted", true);
                var context = Context(request.Id);
                var before = model.Calls;
                await endpoint.RequestDelegate!(context);
                check(
                    stage == "allowed"
                        ? model.Calls == before + 1 && context.Response.StatusCode == 200
                        : model.Calls == before && context.Response.StatusCode == 403,
                    "Preparation suggestion requires live per-request permission for " + stage + "."
                );
            }
            var inFlight = requests.CreateRequest(
                WorkspaceOwner.LocalIdentity,
                "Example",
                "Question",
                "Question",
                "race"
            );
            model.Block = true;
            var racing = Context(inFlight.Id);
            var pending = endpoint.RequestDelegate!(racing);
            await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            triage.Decide(inFlight.Id, false, null);
            model.Result.SetResult(Model.Answer);
            await pending;
            check(
                racing.Response.StatusCode == 403,
                "Decline during preparation discards a result even when the model ignores cancellation."
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
