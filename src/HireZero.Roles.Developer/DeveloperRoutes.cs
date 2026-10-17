namespace HireZero.Roles.Developer;

// The Developer's model purposes; config/model-routes.json maps each to a model (docs/adr/0008-model-routes.md).
public static class DeveloperRoutes
{
    public const string Triage = "developer.triage";
    public const string Validation = "developer.validation";
    public const string Escalation = "developer.escalation";
    public const string Review = "developer.review";
    public const string Build = "developer.build";
}
