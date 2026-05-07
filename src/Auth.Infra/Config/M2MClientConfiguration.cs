using Auth.Domain.M2MClients;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infra.Config;

internal sealed class M2MClientConfiguration : AbstractAuthConfiguration<M2MClient>
{
    public override void Configure(EntityTypeBuilder<M2MClient> builder)
    {
        base.Configure(builder);

        builder.ToTable("m2m_clients");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.ClientId).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ClientSecretHash).HasMaxLength(500).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();

        builder.HasIndex(c => c.ClientId).IsUnique();

        // V1: comma-joined string column. V2 candidate: text[] (Postgres) or a normalized table.
        builder.PrimitiveCollection<List<string>>("_allowedScopes")
            .HasField("_allowedScopes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("allowed_scopes");

        builder.Ignore(c => c.AllowedScopes);
    }
}
