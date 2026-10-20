using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireZero.Tools.GitHub;
using HireZero.Tools.Web;

// Department context drafted from the owner's sources (docs/adr/0041-context-drafted-from-sources.md). The owner
// names up to five sources; they are read here, with no model call, through the audited web.read and github.read
// tools under a small owner manifest that declares only those two. The readable text becomes one context-draft turn,
// prepared but not authorized.
public static partial class CompanyContextSources
{
    public const int MaxSources = 5;

    private static readonly EmployeeManifest Reader = new(
        "owner-sources",
        "Owner sources",
        "Reads the public pages and repositories the owner names for a context draft.",
        "1.0.0",
        "",
        [],
        new("", "", "", [], "")
    )
    {
        Tools =
        [
            new(WebTools.Read.Id, ToolRisk.Read, "Read the public pages the owner names."),
            new(
                GitHubCommands.Read.Id,
                ToolRisk.Read,
                "Read the description and README of repositories the owner names."
            ),
        ],
    };

    public sealed record SourceRead(
        string Source,
        bool Read,
        string Title,
        int Characters,
        bool Clipped,
        string? Error
    );

    // Scope "company" drafts the company-wide facts instead of the employee's department context.
    private sealed record Request(
        string? RequestId,
        string? EmployeeId,
        string[]? Sources,
        decimal CreditAllowance,
        string? Scope = null
    );

    public static void MapContextSources(this WebApplication app, Func<CompanyStore> store, Func<ToolRegistry> tools) =>
        app.MapPost(
            "/api/company/context-sources",
            async (JsonElement body, HttpContext context, CancellationToken token) =>
            {
                var request = CompanyJson.Decode<Request>(body.GetRawText());
                var sources = (request.Sources ?? [])
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (sources.Length is 0 or > MaxSources || sources.Any(s => s.Length > 300))
                    throw new WorkspaceException(
                        "Name one to five sources: web addresses or GitHub repositories.",
                        422
                    );
                var toolbox = tools().For(Reader, request.EmployeeId);
                var read = new List<SourceRead>();
                var bundle = new StringBuilder();
                foreach (var source in sources)
                {
                    try
                    {
                        var (title, text, clipped) = await ReadAsync(toolbox, source, token);
                        var room = CompanyStore.ContextDraftSourceCharacters - bundle.Length;
                        var header =
                            "Source "
                            + (read.Count + 1)
                            + ": "
                            + source
                            + (title.Length > 0 ? " (" + title + ")" : "")
                            + "\n";
                        if (room <= header.Length + 200)
                        {
                            read.Add(
                                new(
                                    source,
                                    false,
                                    title,
                                    0,
                                    false,
                                    "Skipped: the sources already fill the draft's limit."
                                )
                            );
                            continue;
                        }
                        var kept = text.Length > room - header.Length - 2 ? text[..(room - header.Length - 2)] : text;
                        bundle.Append(header).Append(kept).Append("\n\n");
                        read.Add(new(source, true, title, kept.Length, clipped || kept.Length < text.Length, null));
                    }
                    catch (Exception error)
                        when (error is WorkspaceException or HttpRequestException or TaskCanceledException)
                    {
                        read.Add(new(source, false, "", 0, false, error.Message));
                    }
                }
                if (!read.Any(r => r.Read))
                    return Results.Json(
                        new { sources = read, error = "None of the sources could be read." },
                        statusCode: 422
                    );
                var command = new CompanyCommand(request.RequestId ?? Guid.NewGuid().ToString("D"), "context.draft")
                {
                    EmployeeId = request.EmployeeId,
                    Decision = request.Scope,
                    Body = bundle.ToString().TrimEnd(),
                    CreditAllowance = request.CreditAllowance,
                };
                var attempt = CompanyJson.Decode<CompanyAttempt>(
                    store().Apply(command, (string)context.Items["login"]!, out _)
                );
                return Results.Ok(new { sources = read, attempt });
            }
        );

    // A GitHub repository (owner/repo or a github.com address) is read through the GitHub CLI; anything else as a page.
    private static async Task<(string Title, string Text, bool Clipped)> ReadAsync(
        IToolbox toolbox,
        string source,
        CancellationToken token
    )
    {
        if (Repository(source) is { } repository)
        {
            var info = JsonDocument
                .Parse(
                    await toolbox.InvokeAsync(
                        GitHubCommands.Read,
                        new(["api", "repos/" + repository, "--method", "GET"]),
                        token
                    )
                )
                .RootElement;
            var readme = "";
            try
            {
                var file = JsonDocument
                    .Parse(
                        await toolbox.InvokeAsync(
                            GitHubCommands.Read,
                            new(["api", "repos/" + repository + "/readme", "--method", "GET"]),
                            token
                        )
                    )
                    .RootElement;
                readme = Encoding.UTF8.GetString(
                    Convert.FromBase64String(file.GetProperty("content").GetString()!.Replace("\n", ""))
                );
            }
            catch (Exception error) when (error is WorkspaceException or JsonException or FormatException) { }
            var text = new StringBuilder();
            text.AppendLine("GitHub repository " + repository + ".");
            if (info.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                text.AppendLine("Description: " + d.GetString());
            if (
                info.TryGetProperty("homepage", out var h)
                && h.ValueKind == JsonValueKind.String
                && h.GetString()!.Length > 0
            )
                text.AppendLine("Homepage: " + h.GetString());
            if (
                info.TryGetProperty("topics", out var t)
                && t.ValueKind == JsonValueKind.Array
                && t.GetArrayLength() > 0
            )
                text.AppendLine("Topics: " + string.Join(", ", t.EnumerateArray().Select(x => x.GetString())));
            if (readme.Length > 0)
                text.AppendLine("README:").AppendLine(readme);
            var all = text.ToString().Trim();
            var clipped = all.Length > WebTools.MaxTextCharacters;
            return (repository, clipped ? all[..WebTools.MaxTextCharacters] : all, clipped);
        }
        // A bare domain such as example.com is read over https.
        var url = source.Contains("://", StringComparison.Ordinal) ? source : "https://" + source;
        var page = await toolbox.InvokeAsync(WebTools.Read, new WebReadRequest(url), token);
        return (page.Title, page.Text, page.Clipped);
    }

    // owner/repo, or https://github.com/owner/repo with anything after it.
    public static string? Repository(string source)
    {
        var match = RepositoryPattern().Match(source.Trim());
        return match.Success ? match.Groups["owner"].Value + "/" + match.Groups["repo"].Value.TrimEnd('.') : null;
    }

    [GeneratedRegex(
        @"^(?:https?://(?:www\.)?github\.com/)?(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<repo>[A-Za-z0-9._-]{1,100}?)(?:\.git)?(?:/.*)?$"
    )]
    private static partial Regex RepositoryPattern();
}
