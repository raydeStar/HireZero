using System.Text.Json;
using HireZero.Tools.AzureDevOps;

namespace HireZero.Integrations.AzureDevOps;

// The azuredevops.* tools for the company workspace (docs/adr/0009-legacy-features-are-optional-integrations.md):
// off until the owner lists organizations in <data root>/azure-devops.json, then owner-started, read-only reads of the
// owner's open pull requests through their Azure CLI sign-in. No background watch; nothing is written.
public sealed class AzureDevOpsToolProvider(string dataRoot, AzureDevOpsClient? client = null)
    : IToolProvider,
        IIntegrationStatusSource,
        IConfigurableIntegration
{
    // In-app setup (docs/adr/0038-integration-setup-in-the-app.md): organizations only. The Azure CLI path names a
    // program to run, so it stays in azure-devops.json.
    public IntegrationSetup Setup { get; } =
        new(
            "azure-devops",
            "azure-devops.json",
            [
                new(
                    "organizations",
                    "Organizations",
                    "Up to five organization names, one per line, read through your Azure CLI sign-in.",
                    50
                )
                {
                    List = true,
                    Check = v => AzureDevOpsClient.ValidOrganization(v) ? null : "Not an organization name: " + v,
                },
            ],
            []
        );

    private sealed record Settings(string[]? Organizations, string? Az);

    private AzureDevOpsClient? _client = client;
    private readonly object _gate = new();

    private AzureDevOpsClient Client()
    {
        lock (_gate)
            return _client ??= new AzureDevOpsClient(new PrivateClients())
            {
                TokenSource = AzureDevOpsClient.AzureCli(Read().Az),
            };
    }

    private Settings Read()
    {
        var file = Path.Combine(dataRoot, "azure-devops.json");
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), CompanyJson.Options) ?? new(null, null)
                : new(null, null);
        }
        catch (JsonException)
        {
            return new(null, null);
        }
    }

    public string[] Organizations() => [.. (Read().Organizations ?? []).Distinct(StringComparer.OrdinalIgnoreCase)];

    public IntegrationStatus Status()
    {
        var settings = Read();
        var organizations = Organizations();
        var (active, reason) =
            organizations.Length == 0 ? (false, "List your organizations in azure-devops.json in the data root.")
            : organizations.Length > 5 ? (false, "List at most five organizations.")
            : organizations.FirstOrDefault(o => !AzureDevOpsClient.ValidOrganization(o)) is { } bad
                ? (false, "An organization name is not valid: " + bad)
            : settings.Az is { } az && (!Path.IsPathFullyQualified(az) || !File.Exists(az))
                ? (false, "The Azure CLI path in azure-devops.json does not exist.")
            : (
                true,
                $"Reading pull requests in {organizations.Length} organization(s) through your Azure CLI sign-in."
            );
        return new("azure-devops", "Azure DevOps", active, reason);
    }

    public void Register(ToolRegistry registry)
    {
        registry.Register(AzureDevOpsTools.Status, (_, _) => Task.FromResult(Status()));
        registry.Register(AzureDevOpsTools.Attention, AttentionAsync);
    }

    private async Task<PullRequestAttention[]> AttentionAsync(AttentionRequest request, CancellationToken token)
    {
        if (request.Max is < 1 or > 25)
            throw new ToolFailureException("refused", "Read between 1 and 25 pull requests.", 422);
        if (!Status().Active)
            throw new ToolFailureException("refused", "Azure DevOps is not active: " + Status().Reason, 409);
        var organizations = Organizations();
        if (request.Organization is { } chosen && !organizations.Contains(chosen, StringComparer.OrdinalIgnoreCase))
            throw new ToolFailureException("refused", "That organization is not configured.", 409);
        var results = new List<PullRequestAttention>();
        try
        {
            foreach (var organization in request.Organization is { } one ? [one] : organizations)
            {
                var me = await Client().MeAsync(organization, token);
                var pulls = await Client()
                    .PullRequestsAsync(organization, "active", request.Max, token, creatorId: me.Id);
                foreach (var pr in pulls.Take(request.Max))
                {
                    var a = await Client().AttentionAsync(pr, token);
                    results.Add(
                        new(
                            organization,
                            pr.Id,
                            pr.Title,
                            pr.Project,
                            pr.Repository,
                            pr.Source,
                            pr.Target,
                            pr.Draft,
                            pr.MergeStatus,
                            a.FailingPolicies,
                            a.RunningPolicies,
                            a.OpenThreads,
                            a.Problems,
                            pr.Url
                        )
                    );
                }
            }
        }
        catch (Exception error)
            when (error is HttpRequestException or JsonException or InvalidOperationException
                || error is TaskCanceledException && !token.IsCancellationRequested
            )
        {
            throw new ToolFailureException("unavailable", "Azure DevOps could not be read.");
        }
        return [.. results];
    }

    // The company profile offers no general HTTP client factory; these clients are private to Azure DevOps.
    private sealed class PrivateClients : IHttpClientFactory
    {
        private readonly SocketsHttpHandler _handler = new()
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(20) };
    }
}
