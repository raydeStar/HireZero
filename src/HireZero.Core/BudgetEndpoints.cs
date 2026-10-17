using static HireZero.Core.WorkspaceAccess;

namespace HireZero.Core;

// The cockpit's Budget panel: this month's use per role, the busiest tasks against their caps, and the owner's limits.
public static class BudgetEndpoints
{
    static object View(BudgetLedger budgets, RoleCatalog roles, RequestStore requests) =>
        new
        {
            month = BudgetLedger.MonthOf(budgets.Clock?.Invoke() ?? DateTimeOffset.UtcNow),
            roles = roles
                .HiredIds()
                .Prepend("workspace")
                .Distinct()
                .Select(role => new
                {
                    role,
                    limit = budgets.Limit("role:" + role) ?? new BudgetLimit(),
                    status = budgets.RoleStatus(role),
                }),
            tasks = budgets
                .BusyTasks(8)
                .Select(task => (task, request: requests.FindRequest(task)))
                .Where(item => item.request is not null)
                .Select(item => new
                {
                    id = item.task,
                    title = item.request!.DisplayTitle,
                    limit = budgets.Limit("task:" + item.task),
                    status = budgets.TaskStatus(budgets.TaskRole(item.task), item.task),
                }),
        };

    public static void MapBudgetEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/budgets",
            (HttpContext context, BudgetLedger budgets, RoleCatalog roles, RequestStore requests) =>
                !IsOwner(context) ? Results.StatusCode(403) : Results.Ok(View(budgets, roles, requests))
        );
        app.MapPut(
            "/api/budgets/{scope}",
            (
                HttpContext context,
                BudgetLedger budgets,
                RoleCatalog roles,
                RequestStore requests,
                AgentWorker worker,
                string scope,
                BudgetLimit input
            ) =>
            {
                if (!IsOwner(context))
                    return Results.StatusCode(403);
                if (!BudgetLedger.ValidScope(scope))
                    return Results.BadRequest(new { error = "Unknown budget scope." });
                // Changing a limit keeps a waiver the owner already gave; resuming is its own action.
                try
                {
                    budgets.SetLimit(scope, input with { ResumedFor = budgets.Limit(scope)?.ResumedFor });
                }
                catch (WorkspaceException error)
                {
                    return Results.BadRequest(new { error = error.Message });
                }
                worker.Wake();
                return Results.Ok(View(budgets, roles, requests));
            }
        );
        app.MapPost(
            "/api/budgets/{scope}/resume",
            (
                HttpContext context,
                BudgetLedger budgets,
                RoleCatalog roles,
                RequestStore requests,
                AgentWorker worker,
                string scope
            ) =>
            {
                if (!IsOwner(context))
                    return Results.StatusCode(403);
                if (!BudgetLedger.ValidScope(scope))
                    return Results.BadRequest(new { error = "Unknown budget scope." });
                budgets.Resume(scope);
                worker.Wake();
                return Results.Ok(View(budgets, roles, requests));
            }
        );
    }
}
