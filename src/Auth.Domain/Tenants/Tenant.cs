using SharedKernel;

namespace Auth.Domain.Tenants;

public sealed class Tenant : Entity
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }
    public Guid EntraTenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string DefaultRedirectUri { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public static Tenant Create(Guid entraTenantId, string displayName, string defaultRedirectUri)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            EntraTenantId = entraTenantId,
            DisplayName = displayName,
            DefaultRedirectUri = defaultRedirectUri,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
