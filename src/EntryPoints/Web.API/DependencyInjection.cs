using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Web.API.Extensions;

namespace Web.API;

public static class DependencyInjection
{
    public const string HealthCheckConnectionName = Infra.DependencyInjection.DatabaseConnectionName;

    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1);
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'V";
                options.SubstituteApiVersionInUrl = true;
            });

        services.AddEndpoints(typeof(DependencyInjection).Assembly);

        services.AddSingleton<IAuthorizationPolicyProvider, Infra.Authorization.PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, Infra.Authorization.PermissionAuthorizationHandler>();

        services.AddControllers();

        return services;
    }
}
