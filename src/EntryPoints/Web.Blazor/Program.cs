using Application;
using Infra;
using Infra.Extensions;
using Infra.Observability;
using Serilog;
using Web.Blazor.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.MapEnvironmentVariables();

builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddInfrastructureMessaging(builder.Configuration)
    .AddOpenTelemetryObservability(builder.Configuration, serviceName: "Web.Blazor", includeAspNetCore: true);

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseSerilogRequestLogging();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
