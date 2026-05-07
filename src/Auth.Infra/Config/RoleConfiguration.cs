using Auth.Domain.Roles;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infra.Config;

internal sealed class RoleConfiguration : AbstractAuthConfiguration<Role>
{
    public override void Configure(EntityTypeBuilder<Role> builder)
    {
        base.Configure(builder);

        builder.ToTable("roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(2000).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();

        builder.PrimitiveCollection<List<Guid>>("_permissionIds")
            .HasField("_permissionIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("permission_ids");

        builder.Ignore(r => r.PermissionIds);
    }
}
