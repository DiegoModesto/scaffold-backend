using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel;

namespace Infra.Config;

internal abstract class AbstractConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : Entity
{
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.DeletedAt);
        builder.Property(e => e.IsDeleted).IsRequired();

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
