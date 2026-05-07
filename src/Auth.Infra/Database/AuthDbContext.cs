using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Audit;
using Auth.Domain.Groups;
using Auth.Domain.M2MClients;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Tenants;
using Auth.Domain.Users;
using Auth.Infra.Database.Joins;
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

    // Join entity sets — internal: Application layer remains ignorant of join structure.
    internal DbSet<UserRole> UserRoles => Set<UserRole>();
    internal DbSet<UserGroup> UserGroups => Set<UserGroup>();
    internal DbSet<GroupRole> GroupRoles => Set<GroupRole>();
    internal DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<ErrorType>();

        modelBuilder.HasDefaultSchema(Schemas.Auth);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceTenantGuard();
        await ReconcileMembershipsAsync(cancellationToken).ConfigureAwait(false);
        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnforceTenantGuard()
    {
        if (!tenant.HasTenant)
        {
            return;
        }

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

    /// <summary>
    /// Reconciles the in-memory ID collections on User, Group and Role aggregates with the
    /// persisted join tables (user_roles, user_groups, group_roles, role_permissions). For each
    /// tracked aggregate in Added or Modified state we compute the symmetric difference between
    /// the desired set (from the aggregate) and the current set (DB rows minus locally-Deleted
    /// rows, plus locally-Added rows) and emit Add/Remove operations on the join DbSets.
    /// </summary>
    private async Task ReconcileMembershipsAsync(CancellationToken cancellationToken)
    {
        // Snapshot entries up front — emitting Add/Remove on join sets mutates the tracker.
        var userEntries = ChangeTracker.Entries<User>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            .ToList();
        foreach (var entry in userEntries)
        {
            await ReconcileAsync<UserRole>(
                entry.Entity.RoleIds,
                ur => ur.UserId == entry.Entity.Id,
                roleId => new UserRole { UserId = entry.Entity.Id, RoleId = roleId },
                ur => ur.RoleId,
                isNew: entry.State == EntityState.Added,
                cancellationToken).ConfigureAwait(false);

            await ReconcileAsync<UserGroup>(
                entry.Entity.GroupIds,
                ug => ug.UserId == entry.Entity.Id,
                groupId => new UserGroup { UserId = entry.Entity.Id, GroupId = groupId },
                ug => ug.GroupId,
                isNew: entry.State == EntityState.Added,
                cancellationToken).ConfigureAwait(false);
        }

        var groupEntries = ChangeTracker.Entries<Group>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            .ToList();
        foreach (var entry in groupEntries)
        {
            await ReconcileAsync<GroupRole>(
                entry.Entity.RoleIds,
                gr => gr.GroupId == entry.Entity.Id,
                roleId => new GroupRole { GroupId = entry.Entity.Id, RoleId = roleId },
                gr => gr.RoleId,
                isNew: entry.State == EntityState.Added,
                cancellationToken).ConfigureAwait(false);
        }

        var roleEntries = ChangeTracker.Entries<Role>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            .ToList();
        foreach (var entry in roleEntries)
        {
            await ReconcileAsync<RolePermission>(
                entry.Entity.PermissionIds,
                rp => rp.RoleId == entry.Entity.Id,
                permissionId => new RolePermission { RoleId = entry.Entity.Id, PermissionId = permissionId },
                rp => rp.PermissionId,
                isNew: entry.State == EntityState.Added,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileAsync<TJoin>(
        IReadOnlyCollection<Guid> desiredIds,
        System.Linq.Expressions.Expression<Func<TJoin, bool>> ownerFilter,
        Func<Guid, TJoin> factory,
        Func<TJoin, Guid> getOtherId,
        bool isNew,
        CancellationToken cancellationToken)
        where TJoin : class
    {
        var desired = new HashSet<Guid>(desiredIds);
        var existing = new HashSet<Guid>();

        if (!isNew)
        {
            // Pull rows already persisted for this owner. We then layer in/over local
            // tracker state below to get the true "current" set.
            List<TJoin> dbRows = await Set<TJoin>().AsNoTracking()
                .Where(ownerFilter)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (TJoin row in dbRows)
            {
                existing.Add(getOtherId(row));
            }
        }

        var compiledOwnerFilter = ownerFilter.Compile();
        foreach (var entry in ChangeTracker.Entries<TJoin>())
        {
            if (!compiledOwnerFilter(entry.Entity))
            {
                continue;
            }
            Guid otherId = getOtherId(entry.Entity);
            switch (entry.State)
            {
                case EntityState.Added:
                    existing.Add(otherId);
                    break;
                case EntityState.Deleted:
                    existing.Remove(otherId);
                    break;
            }
        }

        // Add rows that should exist but don't.
        foreach (Guid toAdd in desired.Except(existing))
        {
            Set<TJoin>().Add(factory(toAdd));
        }

        // Remove rows that exist but shouldn't. Prefer an already-tracked entry; otherwise
        // attach a stub keyed on the composite PK before marking it Deleted. Avoids
        // duplicate-tracking exceptions when the join set was previously materialized
        // by tracked queries elsewhere in the unit of work.
        foreach (Guid otherId in existing.Except(desired))
        {
            TJoin? tracked = ChangeTracker.Entries<TJoin>()
                .FirstOrDefault(e => compiledOwnerFilter(e.Entity) && getOtherId(e.Entity) == otherId)
                ?.Entity;

            if (tracked is not null)
            {
                Set<TJoin>().Remove(tracked);
            }
            else
            {
                TJoin stub = factory(otherId);
                Set<TJoin>().Attach(stub);
                Set<TJoin>().Remove(stub);
            }
        }
    }
}
