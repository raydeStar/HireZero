using System.Globalization;

namespace HireZero.Core;

public sealed record CompanyMemoryRevisionSource(string AnalysisId, int Version, int Finding, string Digest);

public sealed record CompanyMemoryRevisionRequest(int Finding);

public sealed partial class CompanyStore
{
    private bool MemoryRevisionAudienceAllows(
        StoreConnection db,
        CompanyMemoryInput source,
        CompanyMemoryInput target
    ) =>
        source.EmployeeId is { } employee ? target.EmployeeId == employee
        : source.DepartmentId is { } department
            ? target.DepartmentId == department
                || target.EmployeeId is { } targetEmployee
                    && Find<CompanyEmployee>(db, targetEmployee)?.DepartmentId == department
        : true;

    private bool MemoryRevisionCurrent(StoreConnection db, CompanyMemoryInput input)
    {
        if (input.RevisionSource is not { } origin)
            return true;
        var report = Find<CompanyMemoryAnalysis>(db, origin.AnalysisId);
        if (
            report is not { State: "completed", Findings: not null }
            || report.Version != origin.Version
            || origin.Finding < 0
            || origin.Finding >= report.Findings.Length
            || !MemoryAnalysisSourcesCurrent(db, report)
        )
            return false;
        var finding = report.Findings[origin.Finding];
        if (finding.Revision is not { } revision || CompanyJson.Digest(finding) != origin.Digest)
            return false;
        var target = report.Sources.SingleOrDefault(s => s.Id == revision.MemoryId)?.Definition;
        return target is not null
            && input.ReplacesId == revision.MemoryId
            && input.Kind == target.Kind
            && input.EmployeeId == target.EmployeeId
            && input.DepartmentId == target.DepartmentId
            && input.ArtifactId == target.ArtifactId
            && input.MeetingId == target.MeetingId
            && input.Quote == target.Quote
            && report.Sources.All(s => MemoryRevisionAudienceAllows(db, s.Definition, input));
    }

    private object DraftMemoryRevision(StoreConnection db, CompanyCommand command)
    {
        MemoryAnalysisOwner(db);
        var report = Need<CompanyMemoryAnalysis>(db, command.Id);
        Version(report.Version, command.ExpectedVersion);
        var index = CompanyJson.Decode<CompanyMemoryRevisionRequest>(command.Body ?? "null").Finding;
        if (report.State != "completed" || report.Findings is null || index < 0 || index >= report.Findings.Length)
            throw new WorkspaceException("Choose a suggested revision from a completed report.", 409);
        var finding = report.Findings[index];
        if (finding.Revision is not { } revision || CompanyJson.Digest(finding) != command.Digest)
            throw new WorkspaceException("Review the exact suggested wording and its supporting records.", 409);
        var source = new CompanyMemoryRevisionSource(report.Id, report.Version, index, command.Digest);
        var original = report.Sources.Single(s => s.Id == revision.MemoryId);
        var input = original.Definition with
        {
            Statement = revision.Statement,
            Rationale = revision.Rationale,
            ReplacesId = original.Id,
            ReviewOn = Clock().AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            RevisionSource = source,
        };
        if (!MemoryRevisionCurrent(db, input))
            throw new WorkspaceException(
                "These records changed since the report. Review current memory before drafting a correction.",
                409
            );
        // One report suggestion gets one saved draft; a second click must not multiply the paperwork.
        if (All<CompanyMemoryItem>(db).LastOrDefault(m => m.Definition.RevisionSource == source) is { } existing)
            return existing;
        var draft = (CompanyMemoryItem)SaveMemory(
            db,
            new(command.RequestId, "memory.save", Body: CompanyJson.Encode(input))
        );
        Event(
            db,
            "memory.revision-drafted",
            draft.Id,
            draft.Definition.EmployeeId,
            "An owner opened a suggested revision from report "
                + report.Id
                + ". Separate review and approval are required."
        );
        return draft;
    }
}
