using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Audit;
using Auth.Domain.Groups;
using Auth.Domain.M2MClients;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Tenants;
using Auth.Domain.Users;
using Auth.Infra.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.UnitTests.Infrastructure;

/// <summary>
/// Convenience EF InMemory context for handler unit tests that do NOT depend on join-table
/// reconciliation (e.g. the SyncEntra tests). Implements IAuthDbContext directly so handlers
/// that take that abstraction can be exercised without the full Auth.Infra wiring.
///
/// Tests that exercise the join-table semantics (PermissionResolver tests, end-to-end
/// reconciliation tests) should instead use <see cref="TestAuthDbContext.CreateProduction"/>
/// which materialises a real <see cref="AuthDbContext"/> backed by InMemory.
/// </summary>
public sealed class TestAuthDbContext(DbContextOptions<TestAuthDbContext> options)
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

        // Reuse the production EF configurations so the in-memory model honours
        // the same mappings (entity tables + join tables).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
    }

    public static TestAuthDbContext Create() =>
        new(new DbContextOptionsBuilder<TestAuthDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid()}")
            .Options);

    /// <summary>
    /// Creates a real <see cref="AuthDbContext"/> backed by EF InMemory. Use this when the
    /// test must exercise the join-table reconciliation in <c>SaveChangesAsync</c>.
    /// </summary>
    public static AuthDbContext CreateProduction()
    {
        DbContextOptions<AuthDbContext> options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase($"auth-prod-{Guid.NewGuid()}")
            .Options;
        return new AuthDbContext(options, new NoTenantContext());
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public bool HasTenant => false;
        public Guid TenantId => throw new InvalidOperationException("No tenant scope in tests.");
    }
}
