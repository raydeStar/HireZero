namespace HireZero.Tools.Wiki;

// One request in a Wiki session. Session is "context" (read-only briefs), "contribution" (agent proposals) or
// "publication" (the owner role that approves automatic publication); each keeps its own sign-in cookies.
public sealed record WikiCall(string Session, string Method, string Path, string? JsonBody = null, string? Csrf = null);

public sealed record WikiOriginRequest;

public static class WikiTools
{
    public static readonly ToolContract<WikiCall, ServiceReply> Read = new(
        "wiki.read",
        ToolRisk.Read,
        "Sign in to the local Wiki pilot and read knowledge, reviews and agent briefs."
    );

    public static readonly ToolContract<WikiCall, ServiceReply> Contribute = new(
        "wiki.contribute",
        ToolRisk.Write,
        "Submit a knowledge proposal to the Wiki for review."
    );

    public static readonly ToolContract<WikiCall, ServiceReply> Publish = new(
        "wiki.publish",
        ToolRisk.External,
        "Approve a proposed Wiki revision, publishing it."
    );

    public static readonly ToolContract<WikiOriginRequest, string> Origin = new(
        "wiki.origin",
        ToolRisk.Read,
        "The configured Wiki pilot origin, for showing the owner where it runs."
    );

    public static readonly string[] Sessions = ["context", "contribution", "publication"];

    public static ToolContract<WikiCall, ServiceReply>? Classify(WikiCall call)
    {
        if (!Sessions.Contains(call.Session))
            return null;
        var path = call.Path.TrimStart('/');
        return call.Method switch
        {
            "GET" => Read,
            "POST" when path.StartsWith("api/pilot/session?role=", StringComparison.Ordinal) => Read,
            "POST" when path == "api/proposals" && call.Session == "contribution" => Contribute,
            "POST" when path.StartsWith("api/review/", StringComparison.Ordinal) && call.Session == "publication" =>
                Publish,
            _ => null,
        };
    }
}
