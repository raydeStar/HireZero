using HireZero.Integrations.Sandbox;

public static class CompanySandboxRuntime
{
    public static QualifiedDockerSandbox? Create(CompanySettings settings)
    {
        if (settings.SandboxDocker is null)
            return null;
        var receipt = SandboxQualification.Read(settings.SandboxQualification!);
        if (
            receipt is null
            || !receipt.BoundTo(settings.SandboxImage!, settings.SandboxInstallation!, settings.SandboxEndpoint!)
        )
            throw new InvalidOperationException(
                "Sandbox configuration needs its matching operator qualification receipt. Preserve the previous binding for recovery; the butler cannot reconcile a different engine."
            );
        return new(
            new DockerCommands(settings.SandboxDocker, settings.SandboxEndpoint!),
            receipt,
            () => SandboxQualification.Read(settings.SandboxQualification!),
            () => SandboxQualification.ExecutableHash(settings.SandboxDocker),
            settings.SandboxEnabled
        );
    }

    public static SandboxAvailability Status(ISandboxExecutor? executor) =>
        executor switch
        {
            ISandboxRuntimeAvailability qualified => qualified.Availability,
            null => new(false, "not-configured"),
            _ => new(true, "fixture"),
        };
}
