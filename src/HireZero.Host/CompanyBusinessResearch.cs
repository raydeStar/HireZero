using System.Text.Json;
using HireZero.Tools.Web;

// The owner confirms the business first. Research only supplies evidence; it cannot approve facts or start work.
public static class CompanyBusinessResearch
{
    private static readonly EmployeeManifest Reader = new(
        "business-research",
        "Business research",
        "Read public evidence for an owner-confirmed business.",
        "1.0.0",
        "",
        [],
        new("", "", "", [], "")
    )
    {
        Tools =
        [
            new(WebTools.Research.Id, ToolRisk.Read, "Read permitted pages on the confirmed website."),
            new(WebTools.Discover.Id, ToolRisk.Read, "Discover a bounded set of pages on the confirmed website."),
            new(WebTools.Search.Id, ToolRisk.Spend, "Consume one exact owner-authorized business search request."),
        ],
    };

    private sealed record Request(int BusinessVersion, string Digest, string Url);

    public sealed class SearchAuthority(CompanyStore store) : IToolAuthorityVerifier
    {
        public string Kind => "business-search";

        public bool Authorizes(ToolInvocation invocation)
        {
            if (
                invocation.Authority is not { Kind: "business-search" } authority
                || invocation.Contract.Id != WebTools.Search.Id
                || invocation.Caller.ModuleId != Reader.Id
                || invocation.Caller.EmployeeId is not null
            )
                return false;
            var request = CompanyJson.Decode<WebSearchRequest>(invocation.InputJson);
            return store.ConsumeBusinessSearch(authority.Id, request.Query);
        }
    }

    public static async Task<string> RunLookupAsync(
        CompanyCommand command,
        string subject,
        CompanyStore current,
        ToolRegistry tools,
        CancellationToken token
    )
    {
        var receipt = current.Apply(command, subject, out var fresh);
        if (!fresh)
            return receipt;
        var run = CompanyJson.Decode<CompanyBusinessLookup>(receipt);
        var sources = new List<string>();
        var notices = new List<string>();
        CompanyWebsiteCandidate[] candidates = [];
        var uncertain = false;
        try
        {
            if (run.Kind == "search")
            {
                var result = await tools
                    .For(Reader)
                    .InvokeAsync(WebTools.Search, new(run.Query), token, new("business-search", run.Id));
                candidates = result
                    .Candidates.Select(c => new CompanyWebsiteCandidate(c.Url, c.Title, c.Description))
                    .ToArray();
                if (candidates.Length == 0)
                    notices.Add(
                        "No possible websites were found. Refine the business name or location, or enter the website yourself."
                    );
            }
            else
            {
                var result = await tools
                    .For(Reader)
                    .InvokeAsync(WebTools.Discover, new(run.Website!, run.Website!), token);
                notices.AddRange(result.Notices);
                foreach (var page in result.Pages)
                {
                    try
                    {
                        sources.Add(
                            current
                                .RecordBusinessSource(
                                    subject,
                                    run.BusinessVersion,
                                    page.Url,
                                    page.Title,
                                    page.Text,
                                    page.Clipped
                                )
                                .Id
                        );
                    }
                    catch (WorkspaceException)
                    {
                        notices.Add(
                            "One page could not be saved as evidence. Check the current business and use a permitted page with readable, non-sensitive content."
                        );
                    }
                }
            }
        }
        catch (ToolFailureException error)
        {
            notices.Add(ContentGuard.Clean(error.Message, 500).Text);
            uncertain = run.Kind == "search" && error.Kind == "failed";
        }
        catch (WorkspaceException error)
        {
            notices.Add(ContentGuard.Clean(error.Message, 500).Text);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            uncertain = true;
            notices.Add(
                "The lookup did not return a complete result. Check saved sources and provider history before authorizing another. This request will not be repeated."
            );
        }
        current.CompleteBusinessLookup(run.Id, subject, [.. sources], candidates, notices.Take(8).ToArray(), uncertain);
        return receipt;
    }

    public static void MapBusinessResearch(this WebApplication app, Func<CompanyStore> store, Func<ToolRegistry> tools)
    {
        app.MapGet("/api/company/business/refreshes/{id}", (string id) => Results.Ok(store().BusinessRefresh(id)));
        app.MapGet("/api/company/business/freshness/{id}", (string id) => Results.Ok(store().BusinessFreshness(id)));
        app.MapGet("/api/company/business/sources/{id}", (string id) => Results.Ok(store().BusinessSource(id)));
        app.MapGet("/api/company/business/drafts/{id}", (string id) => Results.Ok(store().BusinessDraft(id)));
        app.MapGet("/api/company/business/lookups/{id}", (string id) => Results.Ok(store().BusinessLookup(id)));
        app.MapGet(
            "/api/company/business/history",
            (int? before) => Results.Ok(store().BusinessProfileHistory(before))
        );
        app.MapPost(
            "/api/company/business/research",
            async (JsonElement body, HttpContext context, CancellationToken token) =>
            {
                var request = CompanyJson.Decode<Request>(body.GetRawText());
                var current = store();
                var subject = (string)context.Items["login"]!;
                CompanyAccess.Demand(current.Actor(subject), CompanyRoles.Owner, "research the business");
                var identity = current.BusinessIdentity();
                if (
                    identity is null
                    || identity.State != "confirmed"
                    || identity.Version != request.BusinessVersion
                    || identity.Digest != request.Digest
                )
                    throw new WorkspaceException(
                        "Confirm the current business identity before reading its website.",
                        409
                    );
                if (identity.Website is null)
                    throw new WorkspaceException(
                        "Add and confirm the business website first, or write the brief from your own knowledge.",
                        422
                    );
                var url = ContentGuard.Bounded(request.Url, 800);
                try
                {
                    var page = await tools()
                        .For(Reader)
                        .InvokeAsync(WebTools.Research, new(url, identity.Website), token);
                    return Results.Ok(
                        current.RecordBusinessSource(
                            subject,
                            identity.Version,
                            page.Url,
                            page.Title,
                            page.Text,
                            page.Clipped
                        )
                    );
                }
                catch (ToolFailureException error)
                {
                    throw new WorkspaceException(error.Message, error.Status is >= 400 and < 600 ? error.Status : 422);
                }
                catch (HttpRequestException)
                {
                    throw new WorkspaceException(
                        "The website could not be reached. Your saved brief is unchanged.",
                        502
                    );
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new WorkspaceException(
                        "The website did not finish responding within the research limit. Check saved sources before trying again.",
                        504
                    );
                }
            }
        );
    }

    public static async Task<string> RunRefreshAsync(
        CompanyCommand command,
        string subject,
        CompanyStore current,
        ToolRegistry tools,
        CancellationToken token
    )
    {
        var receipt = current.Apply(command, subject, out var fresh);
        if (!fresh)
            return receipt;
        var run = CompanyJson.Decode<CompanySourceRefresh>(receipt);
        var results = new List<CompanySourceCheckResult>();
        var uncertain = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        foreach (var sourceId in run.SourceIds)
        {
            if (!current.MayRefreshBusinessSource(run.Id, sourceId))
                break;
            try
            {
                var source = current.BusinessSource(sourceId);
                var page = await tools
                    .For(Reader)
                    .InvokeAsync(WebTools.Research, new(source.Url, run.Website), deadline.Token);
                if (!current.MayRefreshBusinessSource(run.Id, sourceId))
                    break;
                var saved = current.RecordBusinessSource(
                    subject,
                    run.BusinessVersion,
                    page.Url,
                    page.Title,
                    page.Text,
                    page.Clipped
                );
                results.Add(new(sourceId, saved.Id));
            }
            catch (ToolFailureException error)
            {
                results.Add(new(sourceId, null, ContentGuard.Clean(error.Message, 500).Text));
            }
            catch (WorkspaceException)
            {
                results.Add(
                    new(
                        sourceId,
                        null,
                        "This page could not be saved as current evidence. Review the business and source access."
                    )
                );
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                uncertain = true;
                results.Add(
                    new(
                        sourceId,
                        null,
                        "The read did not finish. No request was replayed; approved facts are unchanged."
                    )
                );
                if (deadline.IsCancellationRequested)
                    break;
            }
        }
        current.CompleteBusinessRefresh(run.Id, subject, results.ToArray(), uncertain);
        return receipt;
    }
}
