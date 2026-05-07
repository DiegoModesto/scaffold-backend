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

        builder.PrimitiveCollection<List<Guid>>("_roleIds")
            .HasField("_roleIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("role_ids");

        builder.PrimitiveCollection<List<Guid>>("_groupIds")
            .HasField("_groupIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("group_ids");

        builder.Ignore(u => u.RoleIds);
        builder.Ignore(u => u.GroupIds);
    }
}
