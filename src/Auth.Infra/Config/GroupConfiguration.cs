using Auth.Domain.Groups;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infra.Config;

internal sealed class GroupConfiguration : AbstractAuthConfiguration<Group>
{
    public override void Configure(EntityTypeBuilder<Group> builder)
    {
        base.Configure(builder);

        builder.ToTable("groups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.TenantId).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(2000).IsRequired();

        builder.HasIndex(g => new { g.TenantId, g.Name }).IsUnique();
        builder.HasIndex(g => g.EntraGroupId).HasFilter("\"entra_group_id\" IS NOT NULL");

        builder.PrimitiveCollection<List<Guid>>("_roleIds")
            .HasField("_roleIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("role_ids");

        builder.Ignore(g => g.RoleIds);
    }
}
