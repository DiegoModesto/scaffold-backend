using Auth.Application.Abstractions.Crypto;
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Identity;
using Auth.Application.Abstractions.Tenancy;
using Auth.Infra.Database;
using Auth.Infra.Identity;
using Auth.Infra.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Auth.Infra;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("AuthDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:AuthDb must be configured for Auth.Infra (Postgres connection string).");
        }

        services.AddDbContext<AuthDbContext>(opt =>
        {
            opt.UseNpgsql(
                connectionString,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", Schemas.Auth));
            opt.UseSnakeCaseNamingConvention();
            // OpenIddict registered separately (Bundle G).
        });

        services.AddScoped<IAuthDbContext>(sp => sp.GetRequiredService<AuthDbContext>());

        string? redisConn = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConn))
        {
            throw new InvalidOperationException(
                "Redis:ConnectionString must be configured for Auth.Infra (StackExchange.Redis connection string).");
        }

        services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = redisConn;
        });

        IConnectionMultiplexer multiplexer = ConnectionMultiplexer.Connect(redisConn);
        services.AddSingleton(multiplexer);

        services.AddDataProtection()
            .PersistKeysToStackExchangeRedis(multiplexer, "auth:dataprotection-keys")
            .SetApplicationName("Auth.API");

        services.AddHostedService<PermissionSeedHostedService>();

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddSingleton<IClientSecretHasher, Pbkdf2ClientSecretHasher>();

        services.Configure<Auth.Infra.NetSuite.NetSuiteSamlOptions>(
            configuration.GetSection(Auth.Infra.NetSuite.NetSuiteSamlOptions.SectionName));
        services.AddScoped<
            Auth.Application.NetSuite.InitiateSso.INetSuiteSamlSigner,
            Auth.Infra.NetSuite.NetSuiteSamlSigner>();

        return services;
    }
}
