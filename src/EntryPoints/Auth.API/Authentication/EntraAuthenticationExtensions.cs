using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Auth.API.Authentication;

internal static class EntraAuthenticationExtensions
{
    internal const string SchemeName = "Entra";

    public static IServiceCollection AddEntraExternal(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Entra");
        var authority = section["Authority"];
        var clientId = section["ClientId"];
        var clientSecret = section["ClientSecret"];

        // Allow startup with empty Entra config (e.g. integration tests stub via WireMock and configure later).
        // But if any of the three values is set, ALL must be set.
        var anySet = !string.IsNullOrWhiteSpace(authority)
                  || !string.IsNullOrWhiteSpace(clientId)
                  || !string.IsNullOrWhiteSpace(clientSecret);
        var allSet = !string.IsNullOrWhiteSpace(authority)
                  && !string.IsNullOrWhiteSpace(clientId)
                  && !string.IsNullOrWhiteSpace(clientSecret);
        if (anySet && !allSet)
        {
            throw new InvalidOperationException(
                "Entra federation requires all of Entra:Authority, Entra:ClientId, Entra:ClientSecret to be set together.");
        }

        if (!allSet)
        {
            // Skip registration; Auth.API can still start (e.g. for design-time or non-OIDC tests).
            return services;
        }

        services.AddAuthentication()
            .AddOpenIdConnect(SchemeName, options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.CallbackPath = "/signin-entra";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "roles",
                    ValidateIssuer = false  // multi-tenant Entra: 'tid' claim validates tenant against our Tenants table at the authorize endpoint
                };
            });

        return services;
    }
}
