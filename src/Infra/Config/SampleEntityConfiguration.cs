using Domain.SampleEntities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infra.Config;

internal sealed class SampleEntityConfiguration : AbstractConfiguration<SampleEntity>
{
    public override void Configure(EntityTypeBuilder<SampleEntity> builder)
    {
        base.Configure(builder);

        builder.ToTable("sample_entities");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
    }
}
