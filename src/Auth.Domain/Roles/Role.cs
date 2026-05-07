using SharedKernel;

namespace Auth.Domain.Roles;

public sealed class Role : Entity
{
    private readonly List<Guid> _permissionIds = [];

    private Role()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public IReadOnlyCollection<Guid> PermissionIds => _permissionIds;

    public static Role Create(Guid tenantId, string name, string description)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void AssignPermission(Guid permissionId)
    {
        if (!_permissionIds.Contains(permissionId))
        {
            _permissionIds.Add(permissionId);
        }
    }

    public void RevokePermission(Guid permissionId) => _permissionIds.Remove(permissionId);
}
