using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Infra.Observability;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddOpenTelemetryObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        bool includeAspNetCore = false,
        params string[] additionalActivitySources)
    {
        string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        ResourceBuilder resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: ThisAssemblyVersion());

        services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: serviceName, serviceVersion: ThisAssemblyVersion()))
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddSource(serviceName)
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddNpgsql();

                if (additionalActivitySources is { Length: > 0 })
                {
                    tracing.AddSource(additionalActivitySources);
                }

                if (includeAspNetCore)
                {
                    tracing.AddAspNetCoreInstrumentation();
                }

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    .AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation();

                if (includeAspNetCore)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return services;
    }

    private static string ThisAssemblyVersion() =>
        typeof(OpenTelemetryExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}

internal static class TracerProviderBuilderEntityFrameworkExtensions
{
    public static TracerProviderBuilder AddEntityFrameworkCoreInstrumentation(this TracerProviderBuilder builder)
        => builder.AddSource("Microsoft.EntityFrameworkCore");
}
