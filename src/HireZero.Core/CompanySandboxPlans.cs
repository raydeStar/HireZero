using System.Text;

namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private string[] SandboxEvidenceIds(StoreConnection db, CompanyTask task) =>
        task
            .Evidence.Concat(
                task.PlanId is null
                    ? []
                    : task
                        .Dependencies.Where(id => Find<CompanyTask>(db, id)?.State == "completed")
                        .SelectMany(id => TaskArtifacts(db, id).Where(a => a.State == "approved").Select(a => a.Id))
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public CompanySandboxEvidenceOption[] SandboxEvidenceOptions(string taskId)
    {
        using var db = Open();
        return Transaction(
            db,
            () =>
            {
                var task = Need<CompanyTask>(db, taskId);
                return SandboxEvidenceIds(db, task)
                    .Select(id => Need<CompanyArtifact>(db, id))
                    .Where(a => ApprovedTaskArtifactCurrent(db, a))
                    .Select(a => new CompanySandboxEvidenceOption(
                        a.Id,
                        a.TaskId,
                        Need<CompanyEmployee>(db, a.EmployeeId).Name,
                        Need<CompanyTask>(db, a.TaskId).Input,
                        Encoding.UTF8.GetByteCount(a.Body)
                    ))
                    .ToArray();
            }
        );
    }

    private bool SandboxEvidenceCurrent(StoreConnection db, CompanyTask task, CompanySandboxJob job, int depth)
    {
        var allowed = SandboxEvidenceIds(db, task).ToHashSet(StringComparer.Ordinal);
        return (job.Evidence ?? []).All(saved =>
            allowed.Contains(saved.ArtifactId)
            && Find<CompanyArtifact>(db, saved.ArtifactId) is { State: "approved" } artifact
            && artifact.Version == saved.Version
            && artifact.Digest == saved.Digest
            && artifact.Body == saved.Text
            && ApprovedTaskArtifactCurrent(db, artifact, depth + 1)
        );
    }
}
