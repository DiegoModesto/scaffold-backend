using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Auth.API.Endpoints.UserInfo;

internal sealed class UserInfoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/connect/userinfo", (HttpContext http) =>
        {
            ClaimsPrincipal user = http.User;
            return Results.Json(new
            {
                sub = user.FindFirstValue(OpenIddictConstants.Claims.Subject),
                email = user.FindFirstValue(OpenIddictConstants.Claims.Email),
                name = user.FindFirstValue(OpenIddictConstants.Claims.Name),
                tenant_id = user.FindFirstValue("tenant_id"),
                permission = user.FindAll("permission").Select(c => c.Value).ToArray()
            });
        })
        .RequireAuthorization(b => b
            .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser());
    }
}
