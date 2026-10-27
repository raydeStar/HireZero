using HireZero.Core;
using HireZero.Integrations.Google;

public static class CompanyDriveDocuments
{
    public sealed record PreviewRequest(string? FileId);

    public sealed record SourceCheckRequest(int ExpectedVersion, string Digest);

    public sealed record ImportPreview(
        string Title,
        string Text,
        string SourceName,
        string Notice,
        CompanyDocumentSource Source,
        string SourceReceipt
    );

    private static ImportPreview WithReceipt(CompanyIntegrationScope scope, GoogleDriveDocuments.Preview preview) =>
        new(
            preview.Title,
            preview.Text,
            preview.SourceName,
            preview.Notice,
            preview.Source,
            scope.DocumentImports.Seal(preview.Source)
        );

    private static async Task<GoogleDriveDocuments.Preview> Read(
        CompanyIntegrationScope scope,
        string fileId,
        CancellationToken token
    )
    {
        try
        {
            return await scope.DriveDocuments.ReadAsync(fileId, token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new WorkspaceException(
                "The Drive preview timed out. Your saved copy and draft are unchanged; try again.",
                504
            );
        }
        catch (HttpRequestException)
        {
            throw new WorkspaceException(
                "The Drive preview could not connect. Your saved copy and draft are unchanged; try again.",
                502
            );
        }
    }

    public static void MapDriveDocuments(this WebApplication app, Func<CompanyIntegrationScope> connection)
    {
        app.MapGet(
            "/api/company/documents/drive",
            (HttpContext context) =>
            {
                var scope = connection();
                CompanyAccess.Demand(
                    scope.Store.Actor((string)context.Items["login"]!),
                    CompanyRoles.Owner,
                    "view Drive import settings"
                );
                var status = scope.DriveDocuments.Status();
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(
                    new
                    {
                        ready = status.Active,
                        status.Reason,
                        fileIds = status.Active ? scope.DriveDocuments.AllowedFiles() : [],
                        notice = GoogleDriveDocuments.CopyNotice,
                    }
                );
            }
        );
        app.MapPost(
            "/api/company/documents/drive-preview",
            async (PreviewRequest request, HttpContext context, CancellationToken token) =>
            {
                var scope = connection();
                var login = (string)context.Items["login"]!;
                CompanyAccess.Demand(scope.Store.Actor(login), CompanyRoles.Owner, "preview a Drive document");
                var version = scope.Version;
                GoogleDriveDocuments.Preview preview;
                try
                {
                    preview = await scope.DriveDocuments.ReadAsync(request.FileId ?? "", token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new WorkspaceException(
                        "The Drive preview timed out. Your draft is unchanged; try previewing again.",
                        504
                    );
                }
                catch (HttpRequestException)
                {
                    throw new WorkspaceException(
                        "The Drive preview could not connect. Your draft is unchanged; try previewing again.",
                        502
                    );
                }
                await scope.Gate.WaitAsync(token);
                try
                {
                    CompanyAccess.Demand(
                        scope.Store.Actor(login),
                        CompanyRoles.Owner,
                        "receive a Drive document preview"
                    );
                    if (scope.Version != version)
                        throw new WorkspaceException(
                            "Connection settings changed during the preview. Select the file again.",
                            409
                        );
                    scope.Store.IntegrationEvent(
                        "integration.document-preview",
                        "google-drive",
                        "Selected Drive file previewed: " + request.FileId + ". No company document saved or approved."
                    );
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(WithReceipt(scope, preview));
                }
                finally
                {
                    scope.Gate.Release();
                }
            }
        );
        app.MapPost(
            "/api/company/documents/{id}/check-source",
            async (string id, SourceCheckRequest request, HttpContext context, CancellationToken token) =>
            {
                var scope = connection();
                var login = (string)context.Items["login"]!;
                CompanyAccess.Demand(scope.Store.Actor(login), CompanyRoles.Owner, "check a document source");
                var document = scope.Store.Document(id);
                if (
                    document.Version != request.ExpectedVersion
                    || document.Digest != request.Digest
                    || document.State is not ("approved" or "revoked")
                    || document.Origin?.Source.Provider != "google-drive"
                )
                    throw new WorkspaceException(
                        "Open the current reviewed Drive copy before checking its source.",
                        409
                    );
                var version = scope.Version;
                var preview = await Read(scope, document.Origin.Source.ExternalId, token);
                await scope.Gate.WaitAsync(token);
                try
                {
                    CompanyAccess.Demand(
                        scope.Store.Actor(login),
                        CompanyRoles.Owner,
                        "receive a document source check"
                    );
                    if (scope.Version != version || scope.Store.Document(id) != document)
                        throw new WorkspaceException(
                            "The document or connection changed during this check. Refresh the saved copy and check again.",
                            409
                        );
                    var previous = document.Origin.Source;
                    var state =
                        preview.Source.ContentDigest != previous.ContentDigest ? "text-changed"
                        : preview.Source.Version != previous.Version || preview.Source.ModifiedAt != previous.ModifiedAt
                            ? "metadata-changed"
                        : "unchanged";
                    scope.Store.IntegrationEvent(
                        "integration.document-source-check",
                        "google-drive",
                        "Checked saved document " + id + ": " + state + ". No document changed or approved."
                    );
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(
                        new
                        {
                            documentId = id,
                            documentVersion = document.Version,
                            state,
                            previous,
                            preview = WithReceipt(scope, preview),
                        }
                    );
                }
                finally
                {
                    scope.Gate.Release();
                }
            }
        );
    }
}
