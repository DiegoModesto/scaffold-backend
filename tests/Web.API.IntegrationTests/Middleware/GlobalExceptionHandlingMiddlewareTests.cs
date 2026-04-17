using System.Net;
using System.Text.Json;
using Infra.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Web.API.Middleware;

namespace Web.API.IntegrationTests.Middleware;

public class GlobalExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Should_Return500_AndGenericMessage_OnUnhandledException()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            next: _ => throw new InvalidOperationException("boom"),
            logger: NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.InternalServerError);
        string body = ReadBody(context);
        body.ShouldContain("Internal Server Error");
        body.ShouldNotContain("boom");
    }

    [Fact]
    public async Task Should_Return401_For_UnauthorizedAccessException()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            next: _ => throw new UnauthorizedAccessException(),
            logger: NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_Return400_For_InvalidClaimException()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            next: _ => throw new InvalidClaimException("missing sub"),
            logger: NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return reader.ReadToEnd();
    }
}
