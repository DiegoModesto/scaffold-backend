using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Web.API.Extensions;

public static class HealthCheckResponseWriter
{
    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        string json = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration,
            info = report.Entries.Select(i => new
            {
                key = i.Key,
                status = i.Value.Status.ToString(),
                duration = i.Value.Duration,
                description = i.Value.Description,
                exception = i.Value.Exception?.Message
            })
        });

        return context.Response.WriteAsync(json);
    }
}
