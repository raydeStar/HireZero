using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

// HireZero's MCP endpoint for assistants on this computer, such as Claude Code or Codex
// (docs/adr/0048-assistant-connect-requests.md). It speaks MCP's HTTP transport with plain JSON replies and has three
// tools: list the integrations, ask the owner to connect one, and say which environment variable already holds a
// key. No tool sends, reads or returns a secret; each request is only a Connect card the owner may act on or dismiss.
// Local mode only; the caller guards that it is a direct loopback request with no browser Origin.
public static class CompanyMcp
{
    public const string Path = "/mcp";
    private const string Protocol = "2025-06-18";
    private static readonly ConcurrentDictionary<string, string> Clients = new();

    private static readonly JsonObject Tools = new()
    {
        ["tools"] = new JsonArray(
            Tool(
                "list_integrations",
                "List HireZero's integrations: id, name, whether each is connected and why not, and the names of the keys each one reads. Never returns a key.",
                new JsonObject()
            ),
            Tool(
                "request_connection",
                "Ask the owner to connect an integration. A Connect card waits for them in HireZero; nothing is connected until they sign in or enter the key there.",
                new JsonObject
                {
                    ["integration_id"] = Text("The integration's id, from list_integrations."),
                    ["reason"] = Text("One sentence for the owner: why it is needed."),
                },
                "integration_id",
                "reason"
            ),
            Tool(
                "suggest_key_source",
                "Tell the owner which environment variable on this computer already holds an integration's key. The owner sees the variable's name (never its value) and decides; HireZero reads it locally. Never send the key itself.",
                new JsonObject
                {
                    ["integration_id"] = Text("The integration's id, from list_integrations."),
                    ["secret_name"] = Text("The key it fills, from list_integrations."),
                    ["env_var"] = Text("The environment variable's name, such as OPENROUTER_API_KEY."),
                    ["reason"] = Text("One sentence for the owner."),
                },
                "integration_id",
                "secret_name",
                "env_var",
                "reason"
            )
        ),
    };

    public static async Task HandleAsync(
        HttpContext context,
        CompanyStore store,
        Func<IntegrationStatus[]> statuses,
        Func<IntegrationSetup[]> setups
    )
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = 405;
            return;
        }
        JsonElement message;
        try
        {
            message = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
        }
        catch (JsonException)
        {
            await Reply(context, Error(null, -32700, "Parse error"));
            return;
        }
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("method", out var methodValue))
        {
            await Reply(context, Error(null, -32600, "Invalid request"));
            return;
        }
        var method = methodValue.GetString();
        var id = message.TryGetProperty("id", out var idValue) ? JsonNode.Parse(idValue.GetRawText()) : null;
        // A notification needs no answer.
        if (id is null)
        {
            context.Response.StatusCode = 202;
            return;
        }
        var parameters = message.TryGetProperty("params", out var p) ? p : default;
        switch (method)
        {
            case "initialize":
            {
                var session = Guid.NewGuid().ToString("N");
                var client =
                    parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("clientInfo", out var info)
                    && info.TryGetProperty("name", out var name)
                        ? name.GetString()
                        : null;
                Clients[session] = string.IsNullOrWhiteSpace(client) ? "An assistant" : client.Trim();
                context.Response.Headers["Mcp-Session-Id"] = session;
                await Reply(
                    context,
                    Result(
                        id,
                        new JsonObject
                        {
                            ["protocolVersion"] =
                                parameters.ValueKind == JsonValueKind.Object
                                && parameters.TryGetProperty("protocolVersion", out var version)
                                    ? version.GetString()
                                    : Protocol,
                            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                            ["serverInfo"] = new JsonObject { ["name"] = "hirezero", ["version"] = "1" },
                            ["instructions"] =
                                "HireZero is the owner's AI company. Use these tools to see what is connected and to ask the owner to connect something. Never send keys, passwords or tokens; point at where a key already is with suggest_key_source.",
                        }
                    )
                );
                return;
            }
            case "ping":
                await Reply(context, Result(id, new JsonObject()));
                return;
            case "tools/list":
                await Reply(context, Result(id, Tools.DeepClone()));
                return;
            case "tools/call":
                break;
            default:
                await Reply(context, Error(id, -32601, "Method not found"));
                return;
        }

        var from =
            context.Request.Headers["Mcp-Session-Id"].FirstOrDefault() is { } key
            && Clients.TryGetValue(key, out var known)
                ? known
                : "An assistant";
        var tool =
            parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out var n)
                ? n.GetString()
                : null;
        var args =
            parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out var a)
                ? a
                : default;
        string Arg(string name) =>
            args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Trim()
                : "";
        try
        {
            var text = tool switch
            {
                "list_integrations" => JsonSerializer.Serialize(
                    statuses()
                        .Select(s => new
                        {
                            id = s.Id,
                            name = s.Name,
                            connected = s.Active,
                            reason = s.Reason,
                            keys = setups().FirstOrDefault(x => x.Id == s.Id)?.Secrets.Select(x => x.Name) ?? [],
                        }),
                    CompanyJson.Options
                ),
                "request_connection" => Raised(
                    store.RequestFromAssistant(Arg("integration_id"), from, Arg("reason"), null, null)
                ),
                "suggest_key_source" => Raised(SuggestKeySource()),
                _ => throw new WorkspaceException("No such tool.", 404),
            };
            await Reply(context, Result(id, Content(text, false)));
        }
        catch (WorkspaceException error)
        {
            await Reply(context, Result(id, Content(error.Message, true)));
        }

        CompanyConnectionRequest SuggestKeySource()
        {
            var integration = Arg("integration_id");
            var secret = Arg("secret_name");
            if (setups().FirstOrDefault(x => x.Id == integration) is not { } setup)
                throw new WorkspaceException("That integration takes no keys set in the app.", 404);
            if (setup.Secrets.All(x => x.Name != secret))
                throw new WorkspaceException(
                    "That integration reads no such key. Its keys: "
                        + string.Join(", ", setup.Secrets.Select(x => x.Name))
                        + ".",
                    422
                );
            return store.RequestFromAssistant(integration, from, Arg("reason"), "env:" + Arg("env_var"), secret);
        }
    }

    private static string Raised(CompanyConnectionRequest request) =>
        "A Connect card for "
        + request.Name
        + " is waiting for the owner under Needs you in HireZero"
        + (request.KeySource is null ? "" : ", asking to use the key in " + request.KeySource)
        + ". Nothing is connected until they act on it.";

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) =>
        new()
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray([.. required.Select(r => (JsonNode)r)]),
                ["additionalProperties"] = false,
            },
        };

    private static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Content(string text, bool error) =>
        new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = error,
        };

    private static JsonObject Result(JsonNode? id, JsonNode result) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };

    private static Task Reply(HttpContext context, JsonObject body)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(body.ToJsonString());
    }
}
