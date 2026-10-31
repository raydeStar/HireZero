using HireZero.Core;
using HireZero.Integrations.Documents;

public static class CompanyDocuments
{
    public sealed record RetrievalRequest(string Query, string? EmployeeId = null, bool CompanyOnly = false);

    public sealed record WordImportRequest(string? Name, string? ContentBase64);

    public static void MapCompanyDocuments(this WebApplication app, Func<CompanyStore> store)
    {
        app.Use(
            async (context, next) =>
            {
                if (
                    context.Request.Method == "POST"
                    && (
                        context.Request.Path == "/api/company/documents/save"
                        || context.Request.Path == "/api/company/documents/extract-word"
                        || context.Request.Path == "/api/company/documents/extract-pdf"
                        || context.Request.Path == "/api/company/documents/extract-image"
                    )
                )
                {
                    var limit =
                        context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                    if (limit is { IsReadOnly: false })
                        limit.MaxRequestBodySize =
                            context.Request.Path != "/api/company/documents/save" ? 3 * 1024 * 1024 : 512 * 1024;
                }
                await next(context);
            }
        );
        app.MapGet("/api/company/documents", () => Results.Ok(store().Documents()));
        app.MapGet("/api/company/documents/retention", () => Results.Ok(store().DocumentRetention()));
        app.MapGet("/api/company/documents/{id}", (string id) => Results.Ok(store().Document(id)));
        app.MapPost(
            "/api/company/documents/extract-word",
            (WordImportRequest request, HttpContext context) =>
            {
                if (
                    string.IsNullOrWhiteSpace(request.Name)
                    || request.Name.Length > 160
                    || request.Name.IndexOfAny(['/', '\\', ':']) >= 0
                    || !request.Name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                )
                    throw new WorkspaceException(
                        "Choose a .docx filename without a folder path. Older .doc and macro-enabled files are not supported.",
                        422
                    );
                if (string.IsNullOrEmpty(request.ContentBase64))
                    throw new WorkspaceException("Choose a Word document to import.", 422);
                if (request.ContentBase64.Length > 4 * ((WordTextImport.FileByteLimit + 2) / 3))
                    throw new WorkspaceException("Choose a Word .docx file of at most 2 MiB.", 413);
                try
                {
                    var bytes = Convert.FromBase64String(request.ContentBase64);
                    var preview = WordTextImport.Extract(bytes, CompanyStore.DocumentByteLimit);
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(preview);
                }
                catch (FormatException)
                {
                    throw new WorkspaceException("The Word upload is incomplete. Select the file again.", 422);
                }
                catch (DocumentImportException error)
                {
                    throw new WorkspaceException(error.Message, error.Status);
                }
            }
        );
        app.MapPost(
            "/api/company/documents/extract-pdf",
            async (WordImportRequest request, HttpContext context) =>
            {
                if (
                    string.IsNullOrWhiteSpace(request.Name)
                    || request.Name.Length > 160
                    || request.Name.IndexOfAny(['/', '\\', ':']) >= 0
                    || !request.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                )
                    throw new WorkspaceException("Choose a .pdf filename without a folder path.", 422);
                if (string.IsNullOrEmpty(request.ContentBase64))
                    throw new WorkspaceException("Choose a PDF to import.", 422);
                if (request.ContentBase64.Length > 4 * ((PdfTextImport.FileByteLimit + 2) / 3))
                    throw new WorkspaceException("Choose a PDF of at most 2 MiB.", 413);
                context.Response.Headers.CacheControl = "no-store";
                try
                {
                    var preview = await PdfImportProcess.ExtractAsync(
                        Convert.FromBase64String(request.ContentBase64),
                        typeof(CompanyDocuments).Assembly.Location,
                        context.RequestAborted
                    );
                    CompanyAccess.Demand(
                        store().Actor((string)context.Items["login"]!),
                        CompanyRoles.Owner,
                        "import private documents"
                    );
                    return Results.Ok(preview);
                }
                catch (FormatException)
                {
                    throw new WorkspaceException("The PDF upload is incomplete. Select the file again.", 422);
                }
                catch (DocumentImportException error)
                {
                    throw new WorkspaceException(error.Message, error.Status);
                }
            }
        );
        app.MapPost(
            "/api/company/documents/extract-image",
            async (WordImportRequest request, HttpContext context) =>
            {
                if (
                    string.IsNullOrWhiteSpace(request.Name)
                    || request.Name.Length > 160
                    || request.Name.IndexOfAny(['/', '\\', ':']) >= 0
                    || !new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(request.Name).ToLowerInvariant())
                )
                    throw new WorkspaceException("Choose a PNG or JPEG filename without a folder path.", 422);
                if (string.IsNullOrEmpty(request.ContentBase64))
                    throw new WorkspaceException("Choose a screenshot to read.", 422);
                if (request.ContentBase64.Length > 4 * ((ImageTextImport.FileByteLimit + 2) / 3))
                    throw new WorkspaceException("Choose an image of at most 2 MiB.", 413);
                context.Response.Headers.CacheControl = "no-store";
                try
                {
                    var preview = await ImageTextImport.ExtractAsync(
                        Convert.FromBase64String(request.ContentBase64),
                        context.RequestAborted
                    );
                    CompanyAccess.Demand(
                        store().Actor((string)context.Items["login"]!),
                        CompanyRoles.Owner,
                        "read private screenshots"
                    );
                    return Results.Ok(preview);
                }
                catch (FormatException)
                {
                    throw new WorkspaceException("The image upload is incomplete. Select it again.", 422);
                }
                catch (DocumentImportException error)
                {
                    throw new WorkspaceException(error.Message, error.Status);
                }
            }
        );
        app.MapPost(
            "/api/company/documents/save",
            (CompanyCommand command, HttpContext context) =>
            {
                if (command.Action != "document.save")
                    throw new WorkspaceException("This route only saves document drafts.", 422);
                return Results.Content(
                    store().Apply(command, (string)context.Items["login"]!, out _),
                    "application/json"
                );
            }
        );
        app.MapPost(
            "/api/company/documents/retrieve",
            (RetrievalRequest request) =>
                Results.Ok(store().RetrieveDocuments(request.Query, request.EmployeeId, request.CompanyOnly))
        );
    }
}
