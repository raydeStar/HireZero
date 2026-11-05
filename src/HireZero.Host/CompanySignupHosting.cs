using System.Security.Claims;

public static class CompanySignupHosting
{
    public static bool Verified(HttpContext context) =>
        string.Equals(context.User.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(context.User.FindFirstValue("email"));

    public static bool Restricted(PathString path) =>
        path.StartsWithSegments("/api/connections")
        || path.StartsWithSegments("/api/company/integrations")
        || path.StartsWithSegments("/api/company/model-connection")
        || path.StartsWithSegments("/api/company/github")
        || path.StartsWithSegments("/api/company/runtime")
        || path.StartsWithSegments("/api/company/documents/drive")
        || path.StartsWithSegments("/api/company/documents/drive-preview")
        || path.StartsWithSegments("/api/company/billing");
}
