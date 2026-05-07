using Auth.Domain.Tenants;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infra.Config;

internal sealed class TenantConfiguration : AbstractAuthConfiguration<Tenant>
{
    public override void Configure(EntityTypeBuilder<Tenant> builder)
    {
        base.Configure(builder);

        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => t.EntraTenantId).IsUnique();

        builder.Property(t => t.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.DefaultRedirectUri).HasMaxLength(2000).IsRequired();
        builder.Property(t => t.IsActive).IsRequired();
    }
}
