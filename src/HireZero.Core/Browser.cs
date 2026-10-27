namespace HireZero.Core;

// The browser receives an exact public-reading itinerary, not ambient tools, credentials or a model loop.
public sealed record BrowserLimits(int Seconds = 60, int Requests = 40, int Bytes = 8 * 1024 * 1024)
{
    public bool Valid =>
        Seconds is >= 1 and <= 120 && Requests is >= 1 and <= 100 && Bytes is >= 1 and <= 16 * 1024 * 1024;
}

public sealed record BrowserStep(string Action, string Value);

public sealed record BrowserRequest(
    string JobId,
    string CompanyId,
    string[] Origins,
    BrowserStep[] Steps,
    BrowserLimits Limits,
    int Width = 1280,
    int Height = 800,
    bool Screenshots = true
)
{
    public const int ResourceBytes = 2 * 1024 * 1024;
    public const int ResultBytes = 2 * 1024 * 1024;

    public static Uri Address(string text)
    {
        if (
            text is null
            || text.Length > 2000
            || text.Trim() != text
            || text.Any(char.IsControl)
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.UserInfo.Length != 0
            || !uri.IsDefaultPort
            || uri.HostNameType != UriHostNameType.Dns
            || !uri.IdnHost.Contains('.')
            || uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        )
            throw new WorkspaceException(
                "Browser work requires a public HTTPS hostname on its standard port, without credentials.",
                422
            );
        return uri;
    }

    public static string Origin(Uri uri) => "https://" + uri.IdnHost.ToLowerInvariant();

    public void Validate()
    {
        if (
            !Guid.TryParseExact(JobId, "N", out _)
            || string.IsNullOrWhiteSpace(CompanyId)
            || CompanyId.Length > 80
            || Limits is null
            || !Limits.Valid
            || Width is < 320 or > 1920
            || Height is < 320 or > 1200
            || Origins is null
            || Origins.Length is < 1 or > 4
            || Steps is null
            || Steps.Length is < 2 or > 16
        )
            throw new WorkspaceException(
                "Choose a bounded browser itinerary with 1–4 public origins and 2–16 steps.",
                422
            );
        var origins = Origins
            .Select(value =>
            {
                var uri = Address(value);
                if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                    throw new WorkspaceException("Approve website origins without a path, query or fragment.", 422);
                return Origin(uri);
            })
            .ToArray();
        if (
            origins.Distinct(StringComparer.Ordinal).Count() != origins.Length
            || Steps.Any(s =>
                s is null
                || string.IsNullOrWhiteSpace(s.Value)
                || s.Value.Any(char.IsControl)
                || s.Action is not ("navigate" or "click" or "wait-for" or "read")
            )
            || Steps[0].Action != "navigate"
            || Steps.Count(s => s.Action == "navigate") > 4
            || Steps.Count(s => s.Action == "read") is < 1 or > 4
        )
            throw new WorkspaceException(
                "Start with navigation, select up to four page reads, and use distinct approved origins.",
                422
            );
        foreach (var step in Steps)
            if (step.Action == "navigate")
            {
                if (!origins.Contains(Origin(Address(step.Value)), StringComparer.Ordinal))
                    throw new WorkspaceException("A navigation step leaves the approved websites.", 403);
            }
            else if (step.Value.Length > 300)
                throw new WorkspaceException("Use a CSS selector of at most 300 characters for a browser step.", 422);
    }
}

// No request headers or bodies cross this interface. In particular, it cannot forward cookies or authorization.
public sealed record BrowserResourceRequest(string Url, string Method);

public sealed record BrowserResourceReply(
    int Status,
    Dictionary<string, string> Headers,
    string BodyBase64,
    DateTimeOffset RetrievedAt,
    string? Refusal = null
);

public sealed record BrowserNetworkUsage(int Requests, long ReceivedBytes, int Refused);

public interface IBrowserNetworkSession : IDisposable
{
    BrowserNetworkUsage Usage { get; }
    Task<BrowserResourceReply> FetchAsync(BrowserResourceRequest request, CancellationToken token);
}
