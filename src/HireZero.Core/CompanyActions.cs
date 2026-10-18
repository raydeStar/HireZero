using System.Text.Json;

namespace HireZero.Core;

// Employee actions in the company workspace (docs/adr/0011-employee-actions.md): deterministic, read-only work a
// module runs with its tools, such as gathering a pull request's evidence. An action never calls a model and never
// writes; its output becomes an artifact the owner reviews before any skill may use it as evidence.
public sealed partial class CompanyStore
{
    public const string ActionKind = "action";
    public const string Gathering = "gathering";

    private CompanyTask StartAction(Microsoft.Data.Sqlite.SqliteConnection db, CompanyCommand c)
    {
        var employee = Need<CompanyEmployee>(db, Text(c.EmployeeId, 64));
        var action =
            Role(employee.RoleId).Manifest?.Actions.SingleOrDefault(a => a.Id == c.Skill)
            ?? throw new WorkspaceException("This employee has no such action.", 404);
        var input = ActionInput(action, c.Body);
        var task = NewTask(
            db,
            employee.Id,
            Text(c.ThreadId, 64),
            action.Id,
            input,
            [],
            [],
            null,
            null,
            null,
            ActionKind
        );
        task = task with { State = Gathering, Version = task.Version + 1 };
        Put(db, task);
        Event(db, "action.started", task.Id, employee.Id, action.Name + " started.");
        return task;
    }

    // The input as a JSON object holding exactly the action's declared fields, each bounded, credential-free text.
    public static string ActionInput(EmployeeAction action, string? body)
    {
        Dictionary<string, string>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string>>(body ?? "", CompanyJson.Options);
        }
        catch (JsonException)
        {
            values = null;
        }
        if (values is null || values.Count != action.Fields.Length || action.Fields.Any(f => !values.ContainsKey(f.Id)))
            throw new WorkspaceException(
                "Provide exactly the action's fields: " + string.Join(", ", action.Fields.Select(f => f.Id)) + ".",
                422
            );
        return CompanyJson.Encode(
            action.Fields.ToDictionary(f => f.Id, f => ContentGuard.Bounded(values[f.Id], f.MaxLength))
        );
    }

    public CompanyEmployee EmployeeOf(string id)
    {
        using var db = Open();
        return Need<CompanyEmployee>(db, id);
    }

    // The action's output becomes an artifact awaiting the owner's review; the task waits for that review.
    public CompanyArtifact CompleteAction(string taskId, string body)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var task = Need<CompanyTask>(db, taskId);
                if (task.Kind != ActionKind || task.State != Gathering)
                    throw new WorkspaceException("This action is no longer running.");
                var text = Text(body, 12000);
                var artifact = new CompanyArtifact(
                    Id(),
                    task.Id,
                    task.EmployeeId,
                    text,
                    ContentGuard.Digest(text),
                    "needs_review",
                    "action:" + task.Skill,
                    null
                );
                Save(db, artifact, artifact.Id, artifact.TaskId, artifact.EmployeeId, artifact.State, artifact.Version);
                Put(db, task with { State = "awaiting_review", Reason = null, Version = task.Version + 1 });
                Event(db, "artifact.ready", artifact.Id, artifact.EmployeeId, "Gathered evidence is ready for review.");
                return artifact;
            }
        );
    }

    public CompanyTask FailAction(string taskId, string reason)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var task = Need<CompanyTask>(db, taskId);
                if (task.Kind != ActionKind || task.State != Gathering)
                    return task;
                task = task with { State = "failed", Reason = Text(reason, 300), Version = task.Version + 1 };
                Put(db, task);
                Event(db, "action.failed", task.Id, task.EmployeeId, "The action did not complete: " + task.Reason);
                return task;
            }
        );
    }

    // An action interrupted by a restart did not finish. It only read, so the owner may simply run it again.
    private void RecoverActions(Microsoft.Data.Sqlite.SqliteConnection db)
    {
        foreach (var task in All<CompanyTask>(db).Where(t => t.Kind == ActionKind && t.State == Gathering))
        {
            Put(db, task with { State = "failed", Reason = "action-interrupted", Version = task.Version + 1 });
            Event(db, "action.failed", task.Id, task.EmployeeId, "An action was interrupted by a restart.");
        }
    }
}

public static class ActionToolbox
{
    // The toolbox an action receives: calls never carry authority, so only Read tools can succeed (and every call,
    // refused or not, is still audited by the registry).
    public static IToolbox ReadOnly(this IToolbox tools) => new Wrapper(tools);

    private sealed class Wrapper(IToolbox tools) : IToolbox
    {
        public Task<TOutput> InvokeAsync<TInput, TOutput>(
            ToolContract<TInput, TOutput> contract,
            TInput input,
            CancellationToken token = default,
            ToolAuthority? authority = null
        ) => tools.InvokeAsync(contract, input, token, null);
    }
}
