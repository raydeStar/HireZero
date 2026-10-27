using System.Text;

namespace HireZero.Core;

public sealed record CompanyDocumentInput(
    string Title,
    string Text,
    string? DepartmentId = null,
    string? EmployeeId = null,
    string? SourceName = null,
    string? ReplacesId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        string? SourceReceipt = null
);

public sealed record CompanyDocument(
    string Id,
    string Title,
    string Text,
    string? DepartmentId,
    string? EmployeeId,
    string? SourceName,
    string? ReplacesId,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    string? DeletionScope = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        CompanyDocumentOrigin? Origin = null
);

public sealed record CompanyDocumentSummary(
    string Id,
    string Title,
    string? DepartmentId,
    string? EmployeeId,
    string? SourceName,
    string? ReplacesId,
    string Digest,
    string State,
    int Version,
    DateTimeOffset UpdatedAt,
    int Bytes
);

public sealed partial class CompanyStore
{
    public const int DocumentByteLimit = 64 * 1024;
    public const int CompanyDocumentByteLimit = 2 * 1024 * 1024;
    public const int CompanyDocumentLimit = 50;
    private const string DocumentSchema =
        "CREATE TABLE co_documents(id TEXT NOT NULL,company_id TEXT NOT NULL,parent_id TEXT,employee_id TEXT,state TEXT NOT NULL,revision INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,id),FOREIGN KEY(company_id) REFERENCES co_company(id),FOREIGN KEY(company_id,parent_id) REFERENCES co_departments(company_id,id),FOREIGN KEY(company_id,employee_id) REFERENCES co_employees(company_id,id));"
        + "CREATE TABLE co_document_chunks(company_id TEXT NOT NULL,document_id TEXT NOT NULL,ordinal INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(company_id,document_id,ordinal),FOREIGN KEY(company_id,document_id) REFERENCES co_documents(company_id,id));"
        + "CREATE TABLE co_document_terms(company_id TEXT NOT NULL,document_id TEXT NOT NULL,ordinal INTEGER NOT NULL,term TEXT NOT NULL,weight INTEGER NOT NULL,PRIMARY KEY(company_id,document_id,ordinal,term),FOREIGN KEY(company_id,document_id,ordinal) REFERENCES co_document_chunks(company_id,document_id,ordinal));"
        + "CREATE INDEX co_document_term_lookup ON co_document_terms(company_id,term);";

    private static CompanyDocumentSummary DocumentSummary(CompanyDocument d) =>
        new(
            d.Id,
            d.Title,
            d.DepartmentId,
            d.EmployeeId,
            d.SourceName,
            d.ReplacesId,
            d.Digest,
            d.State,
            d.Version,
            d.UpdatedAt,
            Encoding.UTF8.GetByteCount(d.Text)
        );

    public CompanyDocumentSummary[] Documents()
    {
        using var db = Open();
        return StoredDocuments(db).Select(DocumentSummary).ToArray();
    }

    private CompanyDocument[] StoredDocuments(StoreConnection db) =>
        Rows(
            db,
            "SELECT payload FROM co_documents WHERE company_id=$0 AND state<>'deleted' ORDER BY rowid",
            r => CompanyJson.Decode<CompanyDocument>(r.GetString(0)),
            CompanyId
        );

    public CompanyDocument Document(string id)
    {
        using var db = Open();
        return Need<CompanyDocument>(db, id);
    }

    private object SaveDocument(StoreConnection db, CompanyCommand command)
    {
        var input = CompanyJson.Decode<CompanyDocumentInput>(command.Body ?? "null");
        var previous = command.Id is null ? null : Need<CompanyDocument>(db, command.Id);
        Version(previous?.Version ?? 0, command.ExpectedVersion);
        if (previous is not null && previous.State != "draft")
            throw new WorkspaceException("Create a revised draft; reviewed documents are immutable.", 409);
        if (input.DepartmentId is not null && input.EmployeeId is not null)
            throw new WorkspaceException("Choose company, department or one employee access, not two audiences.", 422);
        if (input.DepartmentId is { } department)
            Need<CompanyDepartment>(db, department);
        if (input.EmployeeId is { } employee && Need<CompanyEmployee>(db, employee).State == "retired")
            throw new WorkspaceException("Choose an employee who has not retired.", 422);
        if (input.ReplacesId is { } replaced)
        {
            var old = Need<CompanyDocument>(db, replaced);
            if (
                old.Id == previous?.Id
                || !(
                    old.State is "approved" or "revoked"
                    || (old.State == "deleted" && old.DeletionScope == "revoked-source")
                )
            )
                throw new WorkspaceException("A revision must name a different reviewed document.", 422);
        }
        var title = Text(input.Title, 160);
        var text = Text(input.Text, DocumentByteLimit).Replace("\r\n", "\n");
        if (Encoding.UTF8.GetByteCount(text) > DocumentByteLimit)
            throw new WorkspaceException("Use a UTF-8 text document of at most 64 KiB.", 413);
        var name = string.IsNullOrWhiteSpace(input.SourceName) ? null : Text(input.SourceName, 160);
        if (
            name is not null
            && (
                name.IndexOfAny(['/', '\\', ':']) >= 0
                || !(
                    name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                )
            )
        )
            throw new WorkspaceException(
                "Use a .txt, .md, .docx or .pdf filename without a folder path. Only the reviewed text is stored.",
                422
            );
        var other = StoredDocuments(db).Where(d => d.Id != previous?.Id).ToArray();
        if (
            other.Length >= CompanyDocumentLimit
            || other.Sum(d => Encoding.UTF8.GetByteCount(d.Text)) + Encoding.UTF8.GetByteCount(text)
                > CompanyDocumentByteLimit
        )
            throw new WorkspaceException(
                "This company reached its document storage allowance (50 records or 2 MiB). Delete unused drafts or revoked library copies, or narrow the material before adding more.",
                409
            );
        var origin = DocumentOrigin(db, input, previous, text);
        var clean = new CompanyDocumentInput(
            title,
            text,
            input.DepartmentId,
            input.EmployeeId,
            name,
            input.ReplacesId,
            input.SourceReceipt
        );
        var document = new CompanyDocument(
            previous?.Id ?? Id(),
            title,
            text,
            input.DepartmentId,
            input.EmployeeId,
            name,
            input.ReplacesId,
            CompanyJson.Digest(clean),
            "draft",
            (previous?.Version ?? 0) + 1,
            Clock(),
            Origin: origin
        );
        Save(db, document, document.Id, document.DepartmentId, document.EmployeeId, document.State, document.Version);
        Event(
            db,
            "document.drafted",
            document.Id,
            document.EmployeeId,
            "A private document draft is ready for review."
        );
        return document;
    }

    private object ReviewDocument(StoreConnection db, CompanyCommand command)
    {
        var document = Need<CompanyDocument>(db, command.Id);
        Version(document.Version, command.ExpectedVersion);
        if (document.Digest != command.Digest)
            throw new WorkspaceException("Review the exact document text and employee audience shown.", 409);
        var state = (command.Decision, document.State) switch
        {
            ("approve", "draft") => "approved",
            ("revoke", "approved") => "revoked",
            ("discard", "draft") => "discarded",
            _ => throw new WorkspaceException("Approve or discard a draft, or revoke an approved document.", 409),
        };
        if (
            state == "approved"
            && document.ReplacesId is { } replaced
            && Need<CompanyDocument>(db, replaced).State == "approved"
        )
            throw new WorkspaceException(
                "Explicitly revoke the earlier approved document before approving its replacement.",
                409
            );
        if (
            state == "approved"
            && document.EmployeeId is { } employee
            && Need<CompanyEmployee>(db, employee).State == "retired"
        )
            throw new WorkspaceException("This employee retired. Create a draft for a current audience.", 409);
        document = document with { State = state, Version = document.Version + 1, UpdatedAt = Clock() };
        Save(db, document, document.Id, document.DepartmentId, document.EmployeeId, document.State, document.Version);
        ClearDocumentIndex(db, document.Id);
        if (state == "approved")
            IndexDocument(db, document);
        Event(
            db,
            "document." + state,
            document.Id,
            document.EmployeeId,
            "Document " + state + "; future task context follows this decision."
        );
        return document;
    }
}
