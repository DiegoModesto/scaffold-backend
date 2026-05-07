using Auth.Application.Abstractions.Data;
using Auth.Domain.Audit;
using Auth.Domain.Groups;
using Auth.Domain.M2MClients;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Tenants;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Auth.Application.UnitTests.Infrastructure;

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

    public static TestAuthDbContext Create() =>
        new(new DbContextOptionsBuilder<TestAuthDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid()}")
            .Options);
}
