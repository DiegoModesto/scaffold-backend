using SharedKernel;

namespace Auth.Domain.Users;

public sealed class User : Entity
{
    private readonly HashSet<Guid> _roleIds = [];
    private readonly HashSet<Guid> _groupIds = [];

    private User()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? EntraOid { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? NetSuiteEmail { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPreProvisioned { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public IReadOnlyCollection<Guid> RoleIds => _roleIds;
    public IReadOnlyCollection<Guid> GroupIds => _groupIds;

    public static User ProvisionFromEntra(Guid tenantId, Guid entraOid, string email, string displayName)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntraOid = entraOid,
            Email = email,
            DisplayName = displayName,
            IsActive = true,
            IsPreProvisioned = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static User PreProvision(Guid tenantId, string email, string displayName)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntraOid = null,
            Email = email,
            DisplayName = displayName,
            IsActive = false,
            IsPreProvisioned = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void ActivateFromEntra(Guid entraOid, string displayName)
    {
        EntraOid = entraOid;
        DisplayName = displayName;
        IsActive = true;
        IsPreProvisioned = false;
    }

    public void Disable() => IsActive = false;

    public void Enable() => IsActive = true;

    public void SetNetSuiteEmail(string? netSuiteEmail) => NetSuiteEmail = netSuiteEmail;

    public void RecordLogin() => LastLoginAt = DateTimeOffset.UtcNow;

    public void AssignRole(Guid roleId) => _roleIds.Add(roleId);

    public void RevokeRole(Guid roleId) => _roleIds.Remove(roleId);

    public void AddToGroup(Guid groupId) => _groupIds.Add(groupId);

    public void RemoveFromGroup(Guid groupId) => _groupIds.Remove(groupId);
}
