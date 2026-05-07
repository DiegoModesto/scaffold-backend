using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Audit;
using Auth.Domain.Groups;
using Auth.Domain.M2MClients;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Tenants;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Infra.Database;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options, ITenantContext tenant)
    : DbContext(options), IAuthDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<M2MClient> M2MClients => Set<M2MClient>();
    public DbSet<AuthAuditEvent> AuditEvents => Set<AuthAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<ErrorType>();

        modelBuilder.HasDefaultSchema(Schemas.Auth);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.HasTenant)
        {
            Guid tenantId = tenant.TenantId;
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified))
                {
                    continue;
                }

                var tenantProperty = entry.Metadata.FindProperty("TenantId");
                if (tenantProperty is null)
                {
                    continue;
                }

                object? current = entry.Property("TenantId").CurrentValue;
                if (current is not Guid currentTenantId)
                {
                    continue;
                }

                if (currentTenantId != tenantId)
                {
                    throw new InvalidOperationException(
                        $"Tenant guard violation: entity '{entry.Metadata.ClrType.Name}' has TenantId '{currentTenantId}' " +
                        $"but the ambient tenant context is '{tenantId}'.");
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
