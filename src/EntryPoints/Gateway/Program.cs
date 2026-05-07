using Gateway;
using Infra.Observability;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, sp, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(sp));

builder.Services.AddOpenTelemetryObservability(
    builder.Configuration,
    serviceName: "Gateway",
    includeAspNetCore: true,
    additionalActivitySources: "Gateway");

builder.Services.AddGatewayAuthentication(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapReverseProxy();

await app.RunAsync();

namespace Gateway
{
    public partial class Program;
}
