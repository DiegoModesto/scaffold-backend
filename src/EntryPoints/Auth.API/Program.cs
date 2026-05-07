using Auth.API;
using Auth.API.Extensions;
using Auth.API.Telemetry;
using Auth.Application;
using Auth.Infra;
using Auth.Infra.Database;
using Auth.Infra.OpenIddict;
using Infra.Observability;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, sp, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(sp));

builder.Services.AddOpenTelemetryObservability(
    builder.Configuration,
    serviceName: "Auth.API",
    includeAspNetCore: true,
    additionalActivitySources: AuthActivitySource.Name);

builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddAuthOpenIddict(builder.Configuration);
builder.Services.AddAuthApiPresentation(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapHealthChecks("/health/ready", new()
{
    Predicate = h => h.Tags.Contains("ready")
});

await app.RunAsync();

namespace Auth.API
{
    public partial class Program;
}
