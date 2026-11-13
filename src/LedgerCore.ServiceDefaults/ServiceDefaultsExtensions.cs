using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LedgerCore.ServiceDefaults;

/// <summary>Telemetry and health endpoints every ledger-core host gets, the way Aspire expects them.</summary>
public static class ServiceDefaultsExtensions
{
    private const string HealthPath = "/health";
    private const string AlivePath = "/alive";
    private const string LiveTag = "live";

    /// <summary>Adds OpenTelemetry for logs, metrics and traces, plus a liveness check.</summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="traceSources">Extra activity sources to collect, such as the application layer's.</param>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder, params string[] traceSources)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(traceSources)
                .AddAspNetCoreInstrumentation(aspNetCore => aspNetCore.Filter = context => !IsHealthProbe(context.Request.Path))
                .AddHttpClientInstrumentation());

        // the AppHost sets the endpoint; without it nothing is exported, which keeps tests and plain dotnet run quiet
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);
        return builder;
    }

    /// <summary>Maps /health (every check) and /alive (only the checks tagged live).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(HealthPath);
        app.MapHealthChecks(AlivePath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(LiveTag) });
        return app;
    }

    private static bool IsHealthProbe(PathString path) =>
        path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(AlivePath, StringComparison.OrdinalIgnoreCase);
}
