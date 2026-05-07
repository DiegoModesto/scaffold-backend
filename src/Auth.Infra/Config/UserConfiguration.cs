using Auth.Domain.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infra.Config;

internal sealed class UserConfiguration : AbstractAuthConfiguration<User>
{
    public override void Configure(EntityTypeBuilder<User> builder)
    {
        base.Configure(builder);

        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.TenantId).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.NetSuiteEmail).HasMaxLength(320);
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.IsPreProvisioned).IsRequired();

        builder.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        builder.HasIndex(u => new { u.TenantId, u.EntraOid })
            .IsUnique()
            .HasFilter("\"entra_oid\" IS NOT NULL");

        // Role/group memberships persist via explicit join tables (user_roles, user_groups).
        // The in-memory _roleIds / _groupIds HashSets are reconciled to those join tables
        // by AuthDbContext.SaveChangesAsync — see ReconcileMembershipsAsync. The backing
        // fields themselves are NOT mapped to columns on the users table.
        builder.Ignore(u => u.RoleIds);
        builder.Ignore(u => u.GroupIds);
    }
}
