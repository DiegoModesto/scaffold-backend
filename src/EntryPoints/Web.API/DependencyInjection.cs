using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Web.API.Extensions;
using Web.API.OpenApi;

namespace Web.API;

public static class DependencyInjection
{
    public const string HealthCheckConnectionName = Infra.DependencyInjection.DatabaseConnectionName;

    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
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

        // Built-in OpenAPI generator, one document per API version. The ApiExplorer above tags
        // every endpoint with GroupName "v1", and a named document includes the endpoints whose
        // GroupName matches its name. Register one AddOpenApi("vN") per supported version.
        services.AddOpenApi("v1", options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

        services.AddEndpoints(typeof(DependencyInjection).Assembly);

        services.AddSingleton<IAuthorizationPolicyProvider, Infra.Authorization.PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, Infra.Authorization.PermissionAuthorizationHandler>();

        return services;
    }
}
