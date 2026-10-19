using System.Text;
using System.Text.Json;
using HireZero.Tools.Node;

namespace HireZero.Roles.Developer;

// Trusted PostgreSQL setup in the company workspace (ported from the legacy profile, Phase 2B.5j;
// docs/adr/0017-publishing-a-step-from-an-action.md): the postgres-setup action reads the toolchain's status and names
// the one install step it allows: write the shipped runner, then install the exact pinned package. The owner approves
// the reading and the two exact local calls; the install downloads the package and runs its install scripts once.
public static class CompanyPostgresSetup
{
    public const string Action = "postgres-setup";

    public static readonly EmployeeAction Definition = new(
        Action,
        "Set up trusted PostgreSQL",
        "Check HireZero's trusted PostgreSQL test runner and, if it is missing or changed, name its pinned install for your approval.",
        [new("step", "Step (install)", 10)]
    )
    {
        Publishes = true,
    };

    public sealed record Step(string Directory, string Package, string Version);

    public static async Task<string> ReadAsync(
        IReadOnlyDictionary<string, string> input,
        IToolbox tools,
        CancellationToken token
    )
    {
        if (input["step"].Trim().ToLowerInvariant() != "install")
            throw new WorkspaceException("The only step is install.", 422);
        var status = await tools.InvokeAsync(TrustedPostgresTools.Status, new(), token);
        var text = new StringBuilder();
        text.AppendLine("Trusted PostgreSQL toolchain, read through postgres.status.");
        text.AppendLine($"Pinned: {status.Package}@{status.Version}");
        text.AppendLine(
            $"Binaries installed: {(status.Installed ? "yes" : "no")}; runner intact: {(status.RunnerIntact ? "yes" : "no")}"
        );
        text.AppendLine();
        if (status.Installed && status.RunnerIntact)
            text.AppendLine("Not eligible: It is installed and intact.");
        else
        {
            var step = new Step(status.Directory, status.Package, status.Version);
            text.AppendLine(
                $"Step: Write the shipped runner, then download and install {step.Package}@{step.Version} (running its install scripts) into HireZero's toolchain folder, once."
            );
            text.AppendLine("Call: " + JsonSerializer.Serialize(step, CompanyJson.Options));
        }
        return text.ToString();
    }

    public static Step? Parse(string body)
    {
        var line = body.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .LastOrDefault(l => l.StartsWith("Call: ", StringComparison.Ordinal));
        try
        {
            return line is null ? null : JsonSerializer.Deserialize<Step>(line[6..], CompanyJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Summary(Step step) =>
        $"Install the trusted PostgreSQL runner and {step.Package}@{step.Version} (download and install scripts), once.";

    public static string[] Calls(Step step) =>
        [
            ToolRegistry.Digest(TrustedPostgresTools.PrepareRunner, new TrustedPostgresRequest()),
            ToolRegistry.Digest(NodeTools.InstallPinned, new NpmInstall(step.Directory, step.Package, step.Version)),
        ];
}

public sealed class CompanyPostgresSetupPublication(CompanyStore store, ToolRegistry registry)
{
    private IToolbox Tools(CompanyTask task) => registry.For(DeveloperModule.Shared, task.EmployeeId, task.Id);

    private (CompanyArtifact Artifact, CompanyTask Task, CompanyPostgresSetup.Step Step) Current(string artifactId)
    {
        var artifact = store.Artifact(artifactId);
        var task = store.Work(artifact.TaskId);
        if (task.Skill != CompanyPostgresSetup.Action || artifact.State != "approved")
            throw new WorkspaceException("Approve a trusted PostgreSQL reading first.");
        var step =
            CompanyPostgresSetup.Parse(artifact.Body)
            ?? throw new WorkspaceException("This reading names no install; it is not eligible.");
        return (artifact, task, step);
    }

    public CompanyPublication Propose(string artifactId)
    {
        var (artifact, _, step) = Current(artifactId);
        return store.ProposePublication(
            new(null, artifact.Id, CompanyPostgresSetup.Summary(step), CompanyPostgresSetup.Calls(step))
        );
    }

    public async Task<CompanyPublication> PublishAsync(string publicationId, CancellationToken token = default)
    {
        var publication = store.Publication(publicationId);
        var (_, task, step) = Current(publication.ArtifactId);
        if (
            publication.GrantId is not null
            || CompanyPostgresSetup.Summary(step) != publication.Summary
            || !publication.Calls.SequenceEqual(CompanyPostgresSetup.Calls(step))
        )
            throw new WorkspaceException("The install changed after approval; read it again.");
        var tools = Tools(task);
        var status = await tools.InvokeAsync(TrustedPostgresTools.Status, new(), token);
        if (status.Directory != step.Directory || status.Package != step.Package || status.Version != step.Version)
            throw new WorkspaceException("The toolchain's pin or folder changed; read it again.");
        store.ClaimPublication(publication.Id);
        var authority = new ToolAuthority(CompanyStore.PublicationAuthorityKind, publication.Id);
        try
        {
            await tools.InvokeAsync(TrustedPostgresTools.PrepareRunner, new(), CancellationToken.None, authority);
            await tools.InvokeAsync(
                NodeTools.InstallPinned,
                new NpmInstall(step.Directory, step.Package, step.Version),
                CancellationToken.None,
                authority
            );
            var after = await tools.InvokeAsync(TrustedPostgresTools.Status, new(), CancellationToken.None);
            var ready = after.Installed && after.RunnerIntact;
            return store.FinishPublication(
                publication.Id,
                ready,
                ready ? "Installed and intact." : "The install finished without its binaries.",
                landed: ready
            );
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            store.FinishPublication(
                publication.Id,
                false,
                error is WorkspaceException or ToolFailureException
                    ? error.Message
                    : "The install stopped: " + error.GetType().Name
            );
            throw;
        }
    }
}
