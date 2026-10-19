using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// OpenTelemetry export, opt-in (docs/adr/0024-opentelemetry-traces-and-metrics.md): only when an OTLP endpoint is
// configured; the standard OTEL_* variables configure the exporter. Without one nothing leaves the machine.
public static class CompanyTelemetryHosting
{
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointVariable));

    // The exporter itself reads the standard OTEL_* variables; this only decides whether the SDK runs at all.
    public static IServiceCollection AddCompanyTelemetry(this IServiceCollection services, bool enabled)
    {
        if (!enabled)
            return services;
        services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")))
                    resource.AddService("hirezero");
            })
            .WithTracing(tracing =>
                tracing.AddSource(CompanyTelemetry.Name).AddAspNetCoreInstrumentation().AddOtlpExporter()
            )
            .WithMetrics(metrics =>
                metrics.AddMeter(CompanyTelemetry.Name).AddAspNetCoreInstrumentation().AddOtlpExporter()
            );
        return services;
    }
}
