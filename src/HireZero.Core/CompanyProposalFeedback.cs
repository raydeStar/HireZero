using System.Text.RegularExpressions;

namespace HireZero.Core;

// The source is a receipt for what prompted the owner's decision, not approved evidence for its claims.
public sealed record CompanyProposalFeedback(string ArtifactId, string Digest, string Kind, string Subject);

public sealed record CompanyOwnerDirection(string Id, string Digest, string Kind, string Subject, string Statement);

public sealed partial class CompanyStore
{
    public const string OwnerDirectionInstructions =
        " ownerDirections are explicit, current owner decisions for this employee and take precedence over earlier suggestions and lessons. A veto means do not propose, draft or execute that work again, including a paraphrase or equivalent approach, unless the owner removes the veto. Corrections replace the inaccurate assumption in future work; they do not grant permission to contact anyone, spend, publish or change shared knowledge. If a correction conflicts with other context, follow it and briefly flag the conflict. Do not ask the owner to repeat these decisions.";

    private object SaveProposalFeedback(StoreConnection db, CompanyCommand command)
    {
        var artifact = Need<CompanyArtifact>(db, command.Id);
        if (artifact.Digest != command.Digest)
            throw new WorkspaceException("The draft changed. Review the current result before saving feedback.", 409);
        if (command.Decision is not ("veto" or "correction"))
            throw new WorkspaceException("Choose a veto or a correction.", 422);
        var subject = Text(command.Name ?? "", 160).Trim();
        if (subject.Length < 4 || !FeedbackSourceContains(artifact, subject))
            throw new WorkspaceException("Select the exact proposal title or inaccurate text from this result.", 422);
        var statement =
            command.Decision == "veto"
                ? "Do not suggest or do this work again: " + subject
                : Text(command.Body ?? "", 1200).Trim();
        if (statement.Length == 0)
            throw new WorkspaceException("Write the correction this employee should remember.", 422);
        var metadata = new CompanyProposalFeedback(artifact.Id, artifact.Digest, command.Decision, subject);
        var directions = OwnerDirections(db, artifact.EmployeeId) ?? [];
        var existing = directions.FirstOrDefault(d =>
            d.Kind == metadata.Kind && ProposalKey(d.Subject) == ProposalKey(subject) && d.Statement == statement
        );
        if (existing is not null)
            return Need<CompanyMemoryItem>(db, existing.Id);
        var replaces =
            metadata.Kind == "correction"
                ? directions.FirstOrDefault(d => d.Kind == "correction" && d.Subject == subject)
                : null;
        var retained = directions.Where(d => d.Id != replaces?.Id).ToArray();
        // Never drop an older veto to make room. The butler keeps the entire do-not-disturb list.
        if (
            retained.Length >= 24
            || retained.Sum(d => d.Statement.Length + d.Subject.Length) + statement.Length + subject.Length > 8000
        )
            throw new WorkspaceException(
                "This employee has reached the standing-feedback limit. Undo obsolete decisions before adding more; existing directions remain in force.",
                409
            );
        var saved = (CompanyMemoryItem)SaveMemory(
            db,
            command with
            {
                Id = null,
                ExpectedVersion = 0,
                Body = CompanyJson.Encode(
                    new CompanyMemoryInput(
                        "feedback",
                        (metadata.Kind == "veto" ? "Veto: " : "Correction: ")
                            + subject[..Math.Min(140, subject.Length)],
                        statement,
                        "Owner direction from a draft; not approval of the draft or its claims.",
                        EmployeeId: artifact.EmployeeId,
                        ReplacesId: replaces?.Id,
                        ProposalFeedback: metadata
                    )
                ),
            }
        );
        // Both writes share the command transaction and idempotency receipt. There is no half-approved decision.
        return ReviewMemory(
            db,
            command with
            {
                Id = saved.Id,
                ExpectedVersion = saved.Version,
                Digest = saved.Digest,
                Decision = replaces is null ? "approve" : "approve-and-replace",
            }
        );
    }

    private CompanyOwnerDirection[]? OwnerDirections(StoreConnection db, string employee)
    {
        var all = All<CompanyMemoryItem>(db)
            .Where(m =>
                m.Definition.EmployeeId == employee
                && m.Definition.ProposalFeedback is not null
                && m.State == "approved"
                && MemoryExclusion(db, m) is null
            )
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .Select(m => new CompanyOwnerDirection(
                m.Id,
                m.Digest,
                m.Definition.ProposalFeedback!.Kind,
                m.Definition.ProposalFeedback.Subject,
                m.Definition.Statement
            ))
            .ToArray();
        return all.Length == 0 ? null : all;
    }

    private bool OwnerDirectionsCurrent(StoreConnection db, CompanyContext context, CompanyTask task) =>
        task.State is "completed" or "canceled"
        || (context.OwnerDirections ?? []).SequenceEqual(OwnerDirections(db, task.EmployeeId) ?? []);

    private static string ProposalKey(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    // The card removes Markdown emphasis. Its visible words still belong to the exact digest-bound report.
    private static bool FeedbackSourceContains(CompanyArtifact source, string text) =>
        source.Body.Contains(text, StringComparison.Ordinal)
        || source
            .Body.Replace("**", "", StringComparison.Ordinal)
            .Replace("__", "", StringComparison.Ordinal)
            .Contains(text, StringComparison.Ordinal);

    private bool ProposalVetoed(StoreConnection db, CompanyTask task)
    {
        var title = ProposalKey(task.Input.Split('\n')[0]);
        return OwnerDirections(db, task.EmployeeId)?.Any(d => d.Kind == "veto" && ProposalKey(d.Subject) == title)
            == true;
    }
}
