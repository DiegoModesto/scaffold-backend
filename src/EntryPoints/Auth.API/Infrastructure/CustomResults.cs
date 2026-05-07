using Microsoft.AspNetCore.Http;
using SharedKernel;

namespace Auth.API.Infrastructure;

internal static class CustomResults
{
    public static IResult Problem(Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Cannot create a problem result from a successful result.");
        }

        return Results.Problem(
            title: GetTitle(result.Error),
            detail: GetDetail(result.Error),
            type: GetType(result.Error.Type),
            statusCode: GetStatusCode(result.Error.Type),
            extensions: GetErrors(result));

        static string GetTitle(Error error) => error.Code;
        static string GetDetail(Error error) => error.Description;

        static string GetType(ErrorType errorType) => errorType.Name switch
        {
            nameof(ErrorType.Validation) => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            nameof(ErrorType.NotFound) => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            nameof(ErrorType.Conflict) => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            nameof(ErrorType.Forbidden) => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        };

        static int GetStatusCode(ErrorType errorType) => errorType.Name switch
        {
            nameof(ErrorType.Validation) => StatusCodes.Status400BadRequest,
            nameof(ErrorType.NotFound) => StatusCodes.Status404NotFound,
            nameof(ErrorType.Conflict) => StatusCodes.Status409Conflict,
            nameof(ErrorType.Forbidden) => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        static Dictionary<string, object?>? GetErrors(Result result) =>
            result.Error is ValidationError validationError
                ? new Dictionary<string, object?> { { "errors", validationError.Errors } }
                : null;
    }
}
