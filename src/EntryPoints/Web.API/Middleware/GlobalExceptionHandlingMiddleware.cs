using System.Net;
using System.Text.Json;
using Infra.Authentication;

namespace Web.API.Middleware;

public class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An unhandled exception occurred");
            await HandleExceptionAsync(context, exception);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        (int statusCode, string error, string message) = exception switch
        {
            InvalidClaimException ex => ((int)HttpStatusCode.BadRequest, "Invalid Token Claims", ex.Message),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "Unauthorized",
                "Access denied. Please provide a valid authentication token"),
            ArgumentException ex => ((int)HttpStatusCode.BadRequest, "Invalid Request", ex.Message),
            _ => ((int)HttpStatusCode.InternalServerError, "Internal Server Error",
                "An unexpected error occurred. Please try again later")
        };

        context.Response.StatusCode = statusCode;

        string json = JsonSerializer.Serialize(new { error, message, statusCode },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        await context.Response.WriteAsync(json);
    }
}
