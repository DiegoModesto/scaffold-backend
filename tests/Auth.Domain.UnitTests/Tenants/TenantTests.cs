using Auth.Domain.Tenants;
using Shouldly;

namespace Auth.Domain.UnitTests.Tenants;

public sealed class TenantTests
{
    [Fact]
    public void Create_ShouldInitializeActiveTenantWithEntraTenantId()
    {
        Guid entraTenantId = Guid.NewGuid();

        Tenant tenant = Tenant.Create(entraTenantId, "Acme Corp", "https://acme.local/signin-oidc");

        tenant.Id.ShouldNotBe(Guid.Empty);
        tenant.EntraTenantId.ShouldBe(entraTenantId);
        tenant.DisplayName.ShouldBe("Acme Corp");
        tenant.DefaultRedirectUri.ShouldBe("https://acme.local/signin-oidc");
        tenant.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Deactivate_ShouldFlipIsActiveToFalse()
    {
        Tenant tenant = Tenant.Create(Guid.NewGuid(), "Acme Corp", "https://acme.local/signin-oidc");

        tenant.Deactivate();

        tenant.IsActive.ShouldBeFalse();
    }
}
