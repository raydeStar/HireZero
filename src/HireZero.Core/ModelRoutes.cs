using System.Text.Json;
using System.Text.RegularExpressions;

namespace HireZero.Core;

// Every model HireZero uses, named by purpose (docs/adr/0008-model-routes.md). Defaults come from
// config/model-routes.json, embedded at build time; HIREZERO_MODEL_ROUTES may name an owner-maintained file whose
// routes and names override them. Source code names purposes, never model ids.
public static partial class ModelRoutes
{
    public const string CompanyText = "company.text";
    public const string CompanyCoding = "company.coding";
    public const string AssistantDefault = "assistant.default";
    public const string OverrideVariable = "HIREZERO_MODEL_ROUTES";

    private sealed record Catalog(
        Dictionary<string, string> Routes,
        Dictionary<string, string> Names,
        Dictionary<string, string> Runtimes,
        Dictionary<string, ModelPrice> Prices
    );

    private static readonly Lazy<Catalog> Current = new(Load);

    // The model id ("provider/model") configured for a purpose.
    public static string Route(string purpose) =>
        Current.Value.Routes.TryGetValue(purpose, out var route)
            ? route
            : throw new InvalidOperationException("No model route is configured for '" + purpose + "'.");

    // The runtime that serves a purpose's route (for example "codex").
    public static string Runtime(string purpose) =>
        Current.Value.Runtimes.TryGetValue(purpose, out var runtime)
            ? runtime
            : throw new InvalidOperationException("No runtime is configured for '" + purpose + "'.");

    // The display name for a model id, or the id itself.
    public static string Name(string model) => Current.Value.Names.TryGetValue(model, out var name) ? name : model;

    // Every distinct model id the catalog routes to, in purpose order.
    public static string[] Models => [.. Current.Value.Routes.Values.Distinct(StringComparer.Ordinal)];

    // A model's price in US dollars per million input and output tokens, reviewed like code
    // (docs/adr/0036-api-model-provider.md); null when the catalog names none.
    public static ModelPrice? Price(string model) =>
        Current.Value.Prices.TryGetValue(model, out var price) ? price : null;

    // What a call cost, in US dollars, or null when the model has no price.
    public static decimal? Cost(string model, ModelUsage usage) => Price(model)?.Cost(usage);

    // The model id without its provider prefix, as a provider's own API names it.
    public static string Native(string route) => route[(route.IndexOf('/') + 1)..];

    public static string Provider(string route) => route[..route.IndexOf('/')];

    private static Catalog Load()
    {
        using var stream =
            typeof(ModelRoutes).Assembly.GetManifestResourceStream("HireZero.Core.model-routes.json")
            ?? throw new InvalidOperationException("The model route catalog is missing from this build.");
        var catalog = Parse(JsonDocument.Parse(stream));
        if (Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } path)
        {
            var overrides = Parse(JsonDocument.Parse(File.ReadAllText(path)));
            foreach (var (purpose, route) in overrides.Routes)
                catalog.Routes[purpose] = route;
            foreach (var (model, name) in overrides.Names)
                catalog.Names[model] = name;
            foreach (var (purpose, runtime) in overrides.Runtimes)
                catalog.Runtimes[purpose] = runtime;
            foreach (var (model, price) in overrides.Prices)
                catalog.Prices[model] = price;
        }
        return catalog;
    }

    private static Catalog Parse(JsonDocument document)
    {
        using (document)
        {
            Dictionary<string, string> Read(string property) =>
                document.RootElement.TryGetProperty(property, out var section)
                    ? section
                        .EnumerateObject()
                        .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal)
                    : new(StringComparer.Ordinal);
            var prices = new Dictionary<string, ModelPrice>(StringComparer.Ordinal);
            if (document.RootElement.TryGetProperty("prices", out var section))
                foreach (var entry in section.EnumerateObject().Where(e => !e.Name.StartsWith('$')))
                {
                    var input = entry.Value.GetProperty("input").GetDecimal();
                    var output = entry.Value.GetProperty("output").GetDecimal();
                    if (!RouteFormat().IsMatch(entry.Name) || input < 0 || output < 0)
                        throw new InvalidOperationException("Invalid model price for '" + entry.Name + "'.");
                    prices[entry.Name] = new(input, output);
                }
            var catalog = new Catalog(Read("routes"), Read("names"), Read("runtimes"), prices);
            foreach (var (purpose, route) in catalog.Routes)
                if (!Purpose().IsMatch(purpose) || !RouteFormat().IsMatch(route))
                    throw new InvalidOperationException("Invalid model route '" + purpose + "': '" + route + "'.");
            return catalog;
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$")]
    private static partial Regex Purpose();

    [GeneratedRegex(@"^[a-z0-9-]+/[A-Za-z0-9][A-Za-z0-9.-]{0,99}$")]
    private static partial Regex RouteFormat();
}

public sealed record ModelPrice(decimal InputPerMillion, decimal OutputPerMillion)
{
    // What a call with this usage cost, in US dollars.
    public decimal Cost(ModelUsage usage) =>
        (usage.InputTokens * InputPerMillion + usage.OutputTokens * OutputPerMillion) / 1_000_000m;
}
