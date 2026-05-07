using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Web.Blazor.Authentication;

public static class BffAuthenticationExtensions
{
    public const string CookieScheme = "BffCookie";
    public const string OidcScheme = "BffOidc";

    public static IServiceCollection AddBffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string authority = configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority must be configured.");
        string clientId = configuration["Auth:ClientId"]
            ?? throw new InvalidOperationException("Auth:ClientId must be configured.");
        string clientSecret = configuration["Auth:ClientSecret"]
            ?? throw new InvalidOperationException("Auth:ClientSecret must be configured.");

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieScheme;
                options.DefaultSignInScheme = CookieScheme;
                options.DefaultChallengeScheme = OidcScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "bff.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            })
            .AddOpenIdConnect(OidcScheme, options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.RequireHttpsMetadata = false;

                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.Scope.Add("offline_access");

                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
            });

        return services;
    }
}
