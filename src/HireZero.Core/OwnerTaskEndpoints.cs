namespace HireZero.Core;
public static class OwnerTaskEndpoints
{
    public static void MapOwnerTaskEndpoints(this WebApplication app)
    {
        app.MapGet("/api/owner-tasks", (HttpContext context) => !WorkspaceAccess.IsOwner(context) ? Results.StatusCode(403) :
            context.RequestServices.GetService<OwnerTaskBoard>() is { } board ? Results.Ok(board.Tasks()) : Results.StatusCode(503));
        app.MapPut("/api/owner-tasks/{id}", (string id, OwnerTaskChange input, HttpContext context) =>
        {
            if (!WorkspaceAccess.IsOwner(context)) return Results.StatusCode(403);
            var board = context.RequestServices.GetService<OwnerTaskBoard>();
            if (board is null) return Results.StatusCode(503);
            if (!board.Tasks().Any(item => item.Id == id)) return Results.NotFound();
            context.RequestServices.GetRequiredService<OwnerTaskStore>().Save(id, input);
            return Results.Ok(board.Tasks());
        });
    }
}
