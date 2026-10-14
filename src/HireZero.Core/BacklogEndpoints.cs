namespace HireZero.Core;

public sealed record BacklogSelectionInput(bool Enabled);
public static class BacklogEndpoints
{
    public static IResult Owner(HttpContext context, Func<IResult> action) => WorkspaceAccess.IsOwner(context) ? action() : Results.StatusCode(403);
    public static void MapBacklogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/backlog", (HttpContext context, BacklogCoordinator backlog) => Owner(context, () => Results.Ok(backlog.Dashboard())));
        app.MapPut("/api/backlog/sources/{id}", (HttpContext context, BacklogCoordinator backlog, string id, BacklogSelectionInput input) =>
            Owner(context, () => { backlog.Select(id, input.Enabled); return Results.Ok(backlog.Dashboard()); }));
        app.MapPost("/api/backlog/catch-up", async (HttpContext context, BacklogCoordinator backlog, CancellationToken token) =>
            !WorkspaceAccess.IsOwner(context) ? (IResult)Results.StatusCode(403) : Results.Ok(await backlog.CatchUpAsync(token)));
        app.MapPost("/api/backlog/proposals/{id}/plan", (HttpContext context, BacklogCoordinator backlog, string id, BacklogPlanInput input) =>
            Owner(context, () => Results.Ok(backlog.Plan(id, input))));
        app.MapPost("/api/backlog/proposals/{id}/approve", (HttpContext context, BacklogCoordinator backlog, string id, BacklogApprovalInput input) =>
            Owner(context, () => Results.Ok(backlog.Approve(id, input.Snapshot, WorkspaceAccess.Login(context)))));
    }
}
