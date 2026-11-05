namespace HireZero.Core;

// An install's model boundary, captured at construction. Even a forgotten side door needs the same key.
public sealed class ModelVendorPolicy
{
    public const string Variable = "HIREZERO_MODEL_VENDOR";
    public const string VultrRoute = "vultr/deepseek-v4.1-flash-1017";
    public bool VultrOnly { get; }

    public ModelVendorPolicy(string? vendor = null)
    {
        if (!string.IsNullOrEmpty(vendor) && vendor != "vultr")
            throw new InvalidOperationException(Variable + " must be vultr or unset.");
        VultrOnly = vendor == "vultr";
    }

    public static ModelVendorPolicy FromEnvironment() => new(Environment.GetEnvironmentVariable(Variable));

    public string Refusal(string provider) =>
        Variable + "=vultr makes " + provider + " unavailable. Only Vultr inference is permitted.";

    public ModelVendorPolicy Require(string provider)
    {
        if (VultrOnly && provider != "vultr")
            throw new InvalidOperationException(Refusal(provider));
        return this;
    }

    public void RequireRoute(string route)
    {
        if (VultrOnly && route != VultrRoute)
            throw new InvalidOperationException(Refusal(route));
    }

    public CompanyModelRouting Routing(CompanyModelRouting configured) =>
        VultrOnly ? new(VultrRoute, VultrRoute, "api") { AnalysisModel = VultrRoute } : configured;
}
