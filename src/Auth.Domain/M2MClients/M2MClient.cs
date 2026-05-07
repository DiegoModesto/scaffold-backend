using SharedKernel;

namespace Auth.Domain.M2MClients;

public sealed class M2MClient : Entity
{
    private readonly List<string> _allowedScopes = [];

    private M2MClient()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public string ClientSecretHash { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<string> AllowedScopes => _allowedScopes;

    public static M2MClient Register(
        Guid tenantId,
        string clientId,
        string clientSecretHash,
        string displayName,
        IEnumerable<string> allowedScopes)
    {
        var client = new M2MClient
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            ClientSecretHash = clientSecretHash,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (string scope in allowedScopes)
        {
            client._allowedScopes.Add(scope);
        }

        return client;
    }

    public void Deactivate() => IsActive = false;

    public void RotateSecret(string newHash) => ClientSecretHash = newHash;
}
