using System.Text.Json;
using System.Text.Json.Nodes;

namespace HireZero.Integrations.Sandbox;

internal static class DockerRuntimeProfile
{
    public static string Digest(JsonElement registration)
    {
        var copy = JsonNode.Parse(registration.GetRawText())!.AsObject();
        const string featureKey = "org.opencontainers.runtime-spec.features";
        if (
            copy["status"] is JsonObject status
            && status[featureKey] is JsonValue value
            && value.TryGetValue<string>(out var featureJson)
        )
        {
            var features = JsonNode.Parse(featureJson)!.AsObject();
            if (features["linux"] is JsonObject linux && linux["capabilities"] is JsonArray capabilities)
            {
                // runsc enumerates a map on startup. Reordering the same keys does not change the locks.
                linux["capabilities"] = new JsonArray(
                    capabilities
                        .Select(capability => capability!.GetValue<string>())
                        .Order(StringComparer.Ordinal)
                        .Select(capability => (JsonNode?)JsonValue.Create(capability))
                        .ToArray()
                );
            }
            status[featureKey] = features.ToJsonString();
        }
        return ContentGuard.Digest(copy.ToJsonString());
    }
}
