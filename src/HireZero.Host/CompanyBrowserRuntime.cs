using HireZero.Integrations.Sandbox;
using HireZero.Integrations.Web;

public static class CompanyBrowserRuntime
{
    public static QualifiedDockerBrowser? Create(CompanySettings settings)
    {
        if (settings.BrowserDocker is null)
            return null;
        var receipt = BrowserQualification.Read(settings.BrowserQualification!);
        if (
            receipt is null
            || !receipt.BoundTo(settings.BrowserImage!, settings.BrowserInstallation!, settings.BrowserEndpoint!)
        )
            throw new InvalidOperationException(
                "Browser configuration needs its matching operator qualification receipt. Preserve the original binding for recovery; the butler cannot reconcile a different engine."
            );
        return new(
            new DockerCommands(settings.BrowserDocker, settings.BrowserEndpoint!),
            receipt,
            () => BrowserQualification.Read(settings.BrowserQualification!),
            () => SandboxQualification.ExecutableHash(settings.BrowserDocker),
            settings.BrowserEnabled,
            (request, authorized) => new PublicBrowserNetwork(request, authorized)
        );
    }

    // A structural Docker probe alone is no admission ticket; the butler checks the invitation as well.
    public static BrowserAvailability Status(IBrowserExecutor? executor) =>
        executor switch
        {
            IBrowserRuntimeAvailability qualified => qualified.Availability,
            null => new(false, "not-configured"),
            _ => new(false, "qualification-required"),
        };
}
