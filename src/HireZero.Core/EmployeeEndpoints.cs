using static HireZero.Core.WorkspaceAccess;

namespace HireZero.Core;

public sealed record EmployeeCreate(string Title, string RequestId);
public sealed record EmployeeWork(string Kind, string Body, string RequestId);
public sealed record EmployeeDocumentChange(string Body, int Version, string RequestId);
public sealed record EmployeeTextChange(string Body, bool Approved = false);
public sealed record EmployeeReview(string ArtifactId, int Version, string Digest, string Decision, string RequestId);
public sealed record EmployeeTransfer(string TargetEmployee, string ArtifactId, int Version, string Digest, string RequestId);

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/employees");
        // This increment has owner-only employee work. Human access roles never imply employee capabilities.
        group.AddEndpointFilter(async (context, next) => IsOwner(context.HttpContext) ? await next(context) : Results.StatusCode(403));
        group.MapGet("", (EmployeeTeam team) => Results.Ok(team.Definitions.Select(d => new
        { d.Info.Id, d.Info.Name, d.Info.Summary, hired = team.Catalog.HiredIds().Contains(d.Info.Id), skills = d.Skills.Select(s => s.Kind), hasDocument = d.DocumentPolicy is not null,
            liveModelAvailable = false, providerExecution = "Serialized per owner; isolated live adapter not qualified" })));
        group.MapPut("/{employee}/hiring", (string employee, RoleChange input, EmployeeTeam team) =>
        {
            if (!team.Definitions.Any(d => d.Info.Id == employee)) return Results.NotFound();
            return Results.Ok(new { hired = team.ChangeHiring(employee, input.Hired), legacyRestartRequired = true });
        });
        group.MapGet("/company-facts", (HttpContext c, EmployeeTeam t) => Results.Ok(t.Store.Facts(Login(c))));
        group.MapPut("/company-facts", (EmployeeTextChange input, HttpContext c, EmployeeTeam t) =>
        {
            if (!input.Approved) return Results.BadRequest(new { error = "Explicitly approve these facts for both employees." });
            t.Store.ApproveFacts(Login(c), input.Body); return Results.Ok(new { saved = true });
        });
        group.MapGet("/{employee}/threads", (string employee, HttpContext c, EmployeeTeam t) =>
        { t.Require(employee); return Results.Ok(t.Store.Threads(employee, Login(c))); });
        group.MapPost("/{employee}/threads", (string employee, EmployeeCreate input, HttpContext c, EmployeeTeam t) =>
        { t.Require(employee); return Results.Ok(t.Store.CreateThread(employee, Login(c), input.Title, input.RequestId)); });
        group.MapGet("/{employee}/threads/{thread}", (string employee, string thread, HttpContext c, EmployeeTeam t) =>
        { t.Require(employee); return Results.Ok(t.Store.Detail(thread, employee, Login(c))); });
        group.MapPost("/{employee}/threads/{thread}/work", (string employee, string thread, EmployeeWork input, HttpContext c, EmployeeTeam t) =>
            Results.Accepted(value: t.Submit(employee, Login(c), thread, input.Kind, input.Body, input.RequestId)));
        group.MapGet("/{employee}/notes", (string employee, HttpContext c, EmployeeTeam t) =>
        { t.Require(employee); return Results.Ok(new { body = t.Store.Notes(employee, Login(c)) }); });
        group.MapPut("/{employee}/notes", (string employee, EmployeeTextChange input, HttpContext c, EmployeeTeam t) =>
        { t.Require(employee); t.Store.SaveNotes(employee, Login(c), input.Body); return Results.Ok(new { saved = true }); });
        group.MapPut("/{employee}/threads/{thread}/document", (string employee, string thread, EmployeeDocumentChange input, HttpContext c, EmployeeTeam t) =>
        {
            var policy = t.Require(employee).DocumentPolicy ?? throw new WorkspaceException("This employee has no brief workflow.", 403);
            return Results.Ok(t.Store.SaveDocument(thread, employee, Login(c), policy.Validate(input.Body), input.Version, input.RequestId));
        });
        group.MapPost("/{employee}/threads/{thread}/review", (string employee, string thread, EmployeeReview input, HttpContext c, EmployeeTeam t) =>
        { t.Require(employee); return Results.Ok(t.Store.Review(thread, employee, Login(c), input.ArtifactId, input.Version, input.Digest, input.Decision, input.RequestId)); });
        group.MapPost("/{employee}/threads/{thread}/handoff", (string employee, string thread, EmployeeTransfer input, HttpContext c, EmployeeTeam t) =>
        {
            t.Require(employee); t.Require(input.TargetEmployee);
            return Results.Ok(t.Store.Handoff(thread, employee, input.TargetEmployee, Login(c), input.ArtifactId, input.Version, input.Digest, input.RequestId));
        });
        group.MapPost("/{employee}/jobs/{job}/cancel", (string employee, string job, HttpContext c, EmployeeTeam t) =>
        { t.Require(employee); return t.Store.Cancel(job, employee, Login(c)) ? Results.Ok(new { canceled = true }) : Results.Conflict(); });
    }
}
