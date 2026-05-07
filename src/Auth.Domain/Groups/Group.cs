using SharedKernel;

namespace Auth.Domain.Groups;

public sealed class Group : Entity
{
    private readonly List<Guid> _roleIds = [];

    private Group()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid? EntraGroupId { get; private set; }

    public IReadOnlyCollection<Guid> RoleIds => _roleIds;

    public static Group Create(Guid tenantId, string name, string description)
    {
        return new Group
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void LinkEntraGroup(Guid entraGroupId) => EntraGroupId = entraGroupId;

    public void UnlinkEntraGroup() => EntraGroupId = null;

    public void AssignRole(Guid roleId)
    {
        if (!_roleIds.Contains(roleId))
        {
            _roleIds.Add(roleId);
        }
    }

    public void RevokeRole(Guid roleId) => _roleIds.Remove(roleId);
}
