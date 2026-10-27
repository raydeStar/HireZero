using System.Text.Json;

namespace HireZero.Integrations.Sandbox;

internal static class DockerCapabilities
{
    // Docker spells the same grant with CAP_ on inspection. A formal title earns no extra keys.
    public static bool Exact(JsonElement host, string property, params string[] expected) =>
        host.TryGetProperty(property, out var values)
        && values.ValueKind == JsonValueKind.Array
        && values.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String)
        && values
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .Select(value => value.StartsWith("CAP_", StringComparison.Ordinal) ? value[4..] : value)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(expected.Order(StringComparer.Ordinal));
}
