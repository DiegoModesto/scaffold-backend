using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Web.Blazor.Authentication.TokenStore;

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

        services.AddOptions<OpenIdConnectOptions>(OidcScheme)
            .Configure<ITokenStore>((opt, store) =>
            {
                opt.Events.OnTokenValidated = ctx =>
                {
                    string sessionId = Guid.NewGuid().ToString("N");
                    var tokens = new SessionTokens(
                        AccessToken: ctx.TokenEndpointResponse?.AccessToken ?? string.Empty,
                        RefreshToken: ctx.TokenEndpointResponse?.RefreshToken,
                        IdToken: ctx.TokenEndpointResponse?.IdToken,
                        ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(
                            int.TryParse(ctx.TokenEndpointResponse?.ExpiresIn, out int s) ? s : 900));

                    var identity = (ClaimsIdentity)ctx.Principal!.Identity!;
                    identity.AddClaim(new Claim("session_id", sessionId));

                    return ctx.HttpContext.RequestServices
                        .GetRequiredService<ITokenStore>()
                        .SaveAsync(sessionId, tokens, ctx.HttpContext.RequestAborted);
                };
            });

        return services;
    }
}
