using System.Net;
using System.Security.Claims;
using Auth.API.Extensions;
using Auth.API.Infrastructure;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.NetSuite.InitiateSso;
using Auth.Domain.Permissions;
using OpenIddict.Abstractions;

namespace Auth.API.Endpoints.Saml;

internal sealed class InitiateNetSuiteSsoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/saml/netsuite/initiate", async (
                HttpContext http,
                ICommandHandler<InitiateNetSuiteSsoCommand, SignedNetSuiteAssertion> handler,
                CancellationToken ct) =>
            {
                string? subject = http.User.FindFirstValue(OpenIddictConstants.Claims.Subject);
                if (!Guid.TryParse(subject, out Guid userId))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                IFormCollection form = http.Request.HasFormContentType
                    ? await http.Request.ReadFormAsync(ct)
                    : new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());

                string targetParam = form["target_user_id"].ToString();
                if (!string.IsNullOrWhiteSpace(targetParam))
                {
                    bool hasUsersWrite = http.User.Claims.Any(c =>
                        c.Type == "permission" && c.Value == PermissionCodes.UsersWrite);
                    if (!hasUsersWrite)
                    {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }

                    if (!Guid.TryParse(targetParam, out Guid targetId))
                    {
                        return Results.BadRequest(new { error = "invalid target_user_id" });
                    }

                    userId = targetId;
                }

                string relayState = form["RelayState"].ToString();
                var command = new InitiateNetSuiteSsoCommand(
                    userId,
                    string.IsNullOrEmpty(relayState) ? null : relayState);

                var result = await handler.Handle(command, ct);
                return result.Match(RenderAutoSubmitForm, CustomResults.Problem);
            })
            .RequireAuthorization(SamlAuthorizationPolicies.NetSuiteInitiate)
            .DisableAntiforgery();
    }

    private static IResult RenderAutoSubmitForm(SignedNetSuiteAssertion a)
    {
        string acsUrlEncoded = WebUtility.HtmlEncode(a.AcsUrl);
        string samlEncoded = WebUtility.HtmlEncode(a.SamlResponseBase64);
        string relayInput = a.RelayState is null
            ? string.Empty
            : $"<input type=\"hidden\" name=\"RelayState\" value=\"{WebUtility.HtmlEncode(a.RelayState)}\" />";

        string html = $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>Redirecting to NetSuite...</title></head>
            <body onload="document.forms[0].submit()">
              <form method="post" action="{{acsUrlEncoded}}">
                <input type="hidden" name="SAMLResponse" value="{{samlEncoded}}" />
                {{relayInput}}
                <noscript><button type="submit">Continue to NetSuite</button></noscript>
              </form>
            </body></html>
            """;

        return Results.Content(html, "text/html");
    }
}
