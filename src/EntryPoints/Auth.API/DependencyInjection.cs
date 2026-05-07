using System.Reflection;
using Auth.API.Authentication;
using Auth.API.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Auth.API;

internal static class DependencyInjection
{
    public static IServiceCollection AddAuthApiPresentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddProblemDetails();
        services.AddOpenApi();
        services.AddEndpointsApiExplorer();
        services.AddEndpoints(Assembly.GetExecutingAssembly());

        // Cookie scheme used between Auth.API and Entra (carries the Entra principal between
        // the OIDC callback and the OpenIddict authorize endpoint flow).
        services.AddAuthentication(opt =>
        {
            opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            opt.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "__Auth-Entra-Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            options.SlidingExpiration = false;
        });

        services.AddEntraExternal(configuration);

        services.AddAuthorization();

        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("AuthDb")
                    ?? throw new InvalidOperationException("ConnectionStrings:AuthDb missing."),
                name: "auth-db", tags: ["ready"])
            .AddRedis(
                configuration["Redis:ConnectionString"]
                    ?? throw new InvalidOperationException("Redis:ConnectionString missing."),
                name: "redis", tags: ["ready"]);

        return services;
    }
}
