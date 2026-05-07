using Auth.Domain.M2MClients;
using Shouldly;

namespace Auth.Domain.UnitTests.M2MClients;

public sealed class M2MClientTests
{
    [Fact]
    public void Register_ShouldCreateActiveClient()
    {
        Guid tenantId = Guid.NewGuid();
        string[] scopes = ["users.read", "users.write"];

        M2MClient client = M2MClient.Register(tenantId, "client-1", "hash", "Client One", scopes);

        client.Id.ShouldNotBe(Guid.Empty);
        client.TenantId.ShouldBe(tenantId);
        client.ClientId.ShouldBe("client-1");
        client.ClientSecretHash.ShouldBe("hash");
        client.DisplayName.ShouldBe("Client One");
        client.IsActive.ShouldBeTrue();
        client.AllowedScopes.Count.ShouldBe(2);
        client.AllowedScopes.ShouldContain("users.read");
        client.AllowedScopes.ShouldContain("users.write");
    }

    [Fact]
    public void Deactivate_FlipsIsActive()
    {
        M2MClient client = M2MClient.Register(Guid.NewGuid(), "c", "h", "n", []);

        client.Deactivate();

        client.IsActive.ShouldBeFalse();
    }
}
