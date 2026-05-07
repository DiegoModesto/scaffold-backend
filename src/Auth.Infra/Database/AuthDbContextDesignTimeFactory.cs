using Auth.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auth.Infra.Database;

/// <summary>
/// Used exclusively by <c>dotnet ef</c> tooling (migrations / scaffolding). At runtime the real
/// composition root in <see cref="DependencyInjection.AddAuthInfrastructure"/> registers
/// <see cref="AuthDbContext"/> with the configured connection string. The hardcoded credentials
/// here are dev defaults; never exercised by the running application.
/// </summary>
internal sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
#pragma warning disable S2068 // Hardcoded credential is dev default for design-time only.
    public AuthDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<AuthDbContext> options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=auth_db;Username=postgres;Password=postgres",
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", Schemas.Auth))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AuthDbContext(options, new DesignTimeTenantContext());
    }
#pragma warning restore S2068

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId =>
            throw new InvalidOperationException("Tenant not available at design time.");

        public bool HasTenant => false;
    }
}
