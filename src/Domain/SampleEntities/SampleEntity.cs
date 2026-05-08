using SharedKernel;

namespace Domain.SampleEntities;

public sealed class SampleEntity : Entity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
