namespace HireZero.Tools.SemaForge;

// SemaForge, the owner's local work-intelligence and automation service.
public static class SemaForgeTools
{
    public static readonly ToolContract<ServiceCall, ServiceReply> Read = new(
        "semaforge.read",
        ToolRisk.Read,
        "Read SemaForge work items, runs, proposals and context packets."
    );

    public static readonly ToolContract<ServiceCall, ServiceReply> Write = new(
        "semaforge.write",
        ToolRisk.Write,
        "Create or change SemaForge work items and automation runs (scope, approve, answer, cancel)."
    );

    private const string ContextPacket = "/api/work-intelligence/context-packet";

    // GETs and the context-packet query are reads; every other method is a write.
    public static ToolContract<ServiceCall, ServiceReply>? Classify(ServiceCall call) =>
        call.Method switch
        {
            "GET" => Read,
            "POST" when call.Path == ContextPacket => Read,
            "POST" or "PUT" or "PATCH" or "DELETE" => Write,
            _ => null,
        };
}
