using Domain.SampleEntities;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<SampleEntity> SampleEntities { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
