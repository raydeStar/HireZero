using System.Text.Json;
using System.Text.Json.Nodes;
using static HireZero.Core.WorkspaceAccess;

namespace HireZero.Core;

// An MCP server (Streamable HTTP, JSON responses) so an invited person's own AI can send the owner requests and follow them.
// It sits behind the same access check as the web app: on the tailnet, the caller is whoever Tailscale says owns the
// calling device, so no API key is needed and roles apply exactly as in the browser.
//   claude mcp add --transport http <name> http://<this computer>.ts.net:5064/api/mcp
public static class AgentRequestsMcp
{
    public static readonly string[] Versions = ["2025-06-18", "2025-03-26", "2024-11-05"];
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapAgentRequests(this WebApplication app)
    {
        app.MapGet("/api/mcp", () => Results.StatusCode(405));
        app.MapPost("/api/mcp", async (HttpContext context, RequestStore requests, AgentStore agents, AgentWorker worker, RequestTriageService triage, CancellationToken token) =>
        {
            JsonNode? body;
            try { body = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: token); }
            catch (JsonException) { return Results.Json(Error(null, -32700, "Parse error"), statusCode: 400); }
            var replies = new JsonArray();
            foreach (var message in body is JsonArray batch ? batch.ToArray() : [body])
                if (message is JsonObject call && Handle(call, context, requests, agents, worker, triage) is { } reply) replies.Add(reply);
            if (replies.Count == 0) return Results.StatusCode(202);
            return Results.Text((body is JsonArray ? replies : replies[0]!).ToJsonString(), "application/json");
        });
    }

    // Calls that create work for the owner are limited per person (10 an hour); the handshake and reads are not.
    public const int WritesPerHour = 10;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Queue<DateTimeOffset>> Writes = new(StringComparer.OrdinalIgnoreCase);
    static void Limit(string login)
    {
        var now = DateTimeOffset.UtcNow; var recent = Writes.GetOrAdd(login, _ => new Queue<DateTimeOffset>());
        lock (recent)
        {
            while (recent.Count > 0 && now - recent.Peek() > TimeSpan.FromHours(1)) recent.Dequeue();
            if (recent.Count >= WritesPerHour) throw new WorkspaceException("You have sent " + WritesPerHour + " requests or messages in the last hour. Try again later.", 429);
            recent.Enqueue(now);
        }
    }

    static JsonObject? Handle(JsonObject call, HttpContext context, RequestStore requests, AgentStore agents, AgentWorker worker, RequestTriageService triage)
    {
        var id = call["id"]?.DeepClone(); var method = call["method"]?.GetValue<string>() ?? "";
        if (id is null) return null; // notifications need no answer
        try
        {
            return method switch
            {
                "initialize" => Result(id, new JsonObject
                {
                    ["protocolVersion"] = Versions.Contains(call["params"]?["protocolVersion"]?.GetValue<string>()) ? call["params"]!["protocolVersion"]!.GetValue<string>() : Versions[0],
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = "hirezero", ["version"] = "0.1.0" },
                    ["instructions"] = "Send requests to the owner of this HireZero workspace and follow their status. You are " + Login(context) + " (" + Role(context) + "). " +
                        "The owner confirms requests before any work happens; replies come back through get_request."
                }),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, new JsonObject { ["tools"] = Tools() }),
                "tools/call" => Result(id, Call(call["params"] as JsonObject, context, requests, agents, worker, triage)),
                _ => Error(id, -32601, "Method not found")
            };
        }
        catch (WorkspaceException error) { return Result(id, Text(error.Message, true)); }
    }

    static JsonArray Tools() =>
    [
        Tool("send_request", "Send the owner a request (a question, a change you need, a review). The owner sees a summary and a safety read and confirms before any work starts.",
            new() { ["title"] = Prop("string", "One-line summary, up to 160 characters."), ["description"] = Prop("string", "What you need and why, with any details, up to 4000 characters."),
                ["project"] = Prop("string", "Optional project or repository name.") }, ["title", "description"]),
        Tool("list_my_requests", "List the requests you sent, with status and the latest reply.", new(), []),
        Tool("get_request", "Read one of your requests and its conversation.", new() { ["id"] = Prop("string", "The request id from send_request or list_my_requests.") }, ["id"]),
        Tool("add_message", "Add a follow-up or answer a question on one of your requests.",
            new() { ["id"] = Prop("string", "The request id."), ["message"] = Prop("string", "Your message, up to 4000 characters.") }, ["id", "message"]),
    ];

    static JsonObject Call(JsonObject? parameters, HttpContext context, RequestStore requests, AgentStore agents, AgentWorker worker, RequestTriageService triage)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? ""; var args = parameters?["arguments"] as JsonObject ?? [];
        string Arg(string key, int max, bool required = true)
        {
            var value = args[key]?.GetValue<string>()?.Trim() ?? "";
            if (required && value.Length == 0) throw new WorkspaceException("Missing " + key + ".", 400);
            if (value.Length > max) throw new WorkspaceException(key + " is longer than " + max + " characters.", 400);
            return value;
        }
        var login = Login(context);
        WorkRequest Own(string requestId) => requests.FindRequest(requestId) is { } item && CanRead(context, item) ? item : throw new WorkspaceException("No request with that id is yours.", 404);
        switch (name)
        {
            case "send_request":
            {
                Limit(login);
                var item = requests.CreateRequest(login, Arg("project", 100, false) is { Length: > 0 } project ? project : "General", Arg("title", 160), Arg("description", 4000), "mcp:" + Guid.NewGuid().ToString("N"));
                var opened = triage.Open(item, "ai");
                requests.AddOwnerAgentNotice(item.Id, item.Requester + "'s AI sent this request. Safety read: " + opened.Safety + " (" + string.Join("; ", opened.Reasons) + "). Confirm or decline on the request card.",
                    "ai-request:" + item.Id);
                Queue(requests, agents, worker, item);
                return Text(JsonSerializer.Serialize(new { id = item.Id, status = "sent", next = "The owner reviews it and confirms before any work starts. Check back with get_request." }, Json));
            }
            case "list_my_requests":
                return Text(JsonSerializer.Serialize(requests.Requests(login).Take(25).Select(item => new
                {
                    item.Id, item.Title, item.Status, item.CreatedAt, decision = triage.Find(item.Id)?.Decision ?? "pending",
                    latestReply = requests.Messages(item.Id).LastOrDefault(message => message.Author == "workspace-agent")?.Body
                }), Json));
            case "get_request":
            {
                var item = Own(Arg("id", 64));
                return Text(JsonSerializer.Serialize(new { item.Id, item.Title, item.Description, item.Status, decision = triage.Find(item.Id)?.Decision ?? "pending",
                    conversation = requests.Messages(item.Id).Select(message => new { from = message.Author == "workspace-agent" ? "HireZero" : message.Author, message.Body, message.CreatedAt }) }, Json));
            }
            case "add_message":
            {
                Limit(login);
                var item = Own(Arg("id", 64));
                requests.AddMessage(item.Id, login, Arg("message", 4000), "mcp:" + Guid.NewGuid().ToString("N"));
                Queue(requests, agents, worker, item);
                return Text("Added to request " + item.Id + ".");
            }
            default: throw new WorkspaceException("Unknown tool " + name + ".", 400);
        }
    }

    static void Queue(RequestStore requests, AgentStore agents, AgentWorker worker, WorkRequest item)
        => RequestAgentWork.Queue(requests, agents, worker, item);
    static JsonObject Tool(string name, string description, JsonObject properties, string[] required) => new()
    {
        ["name"] = name, ["description"] = description,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray([.. required.Select(value => (JsonNode)value)]) }
    };
    static JsonObject Prop(string type, string description) => new() { ["type"] = type, ["description"] = description };
    static JsonObject Text(string text, bool error = false) => new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = error };
    static JsonObject Result(JsonNode id, JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
    static JsonObject Error(JsonNode? id, int code, string message) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
