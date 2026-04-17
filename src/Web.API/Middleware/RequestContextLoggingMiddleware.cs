using Microsoft.Extensions.Primitives;
using Serilog.Context;

namespace Web.API.Middleware;

public sealed class RequestContextLoggingMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeaderName = "x-correlation-id";

    public Task Invoke(HttpContext context)
    {
        using (LogContext.PushProperty("CorrelationId", GetCorrelationId(context)))
        {
            return next.Invoke(context);
        }
    }

    private static string GetCorrelationId(HttpContext context)
    {
        context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out StringValues correlationId);
        return correlationId.FirstOrDefault() ?? context.TraceIdentifier;
    }
}
