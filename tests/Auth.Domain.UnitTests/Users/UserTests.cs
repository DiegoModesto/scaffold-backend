using Auth.Domain.Users;
using Shouldly;

namespace Auth.Domain.UnitTests.Users;

public sealed class UserTests
{
    [Fact]
    public void ProvisionFromEntra_ShouldCreateActiveUserBoundToTenant()
    {
        Guid tenantId = Guid.NewGuid();
        Guid entraOid = Guid.NewGuid();

        User user = User.ProvisionFromEntra(tenantId, entraOid, "alice@acme.com", "Alice");

        user.Id.ShouldNotBe(Guid.Empty);
        user.TenantId.ShouldBe(tenantId);
        user.EntraOid.ShouldBe(entraOid);
        user.Email.ShouldBe("alice@acme.com");
        user.DisplayName.ShouldBe("Alice");
        user.IsActive.ShouldBeTrue();
        user.IsPreProvisioned.ShouldBeFalse();
    }

    [Fact]
    public void PreProvision_ShouldCreateInactiveUserUntilFirstLogin()
    {
        Guid tenantId = Guid.NewGuid();

        User user = User.PreProvision(tenantId, "bob@acme.com", "Bob");

        user.TenantId.ShouldBe(tenantId);
        user.EntraOid.ShouldBeNull();
        user.IsActive.ShouldBeFalse();
        user.IsPreProvisioned.ShouldBeTrue();
    }

    [Fact]
    public void ActivateFromEntra_ShouldBindEntraOidAndActivate()
    {
        User user = User.PreProvision(Guid.NewGuid(), "bob@acme.com", "Bob");
        Guid entraOid = Guid.NewGuid();

        user.ActivateFromEntra(entraOid, "Bob Smith");

        user.EntraOid.ShouldBe(entraOid);
        user.DisplayName.ShouldBe("Bob Smith");
        user.IsActive.ShouldBeTrue();
        user.IsPreProvisioned.ShouldBeFalse();
    }

    [Fact]
    public void Disable_ShouldSetIsActiveFalse()
    {
        User user = User.ProvisionFromEntra(Guid.NewGuid(), Guid.NewGuid(), "a@b.com", "A");

        user.Disable();

        user.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void AssignRole_ShouldBeIdempotent()
    {
        User user = User.ProvisionFromEntra(Guid.NewGuid(), Guid.NewGuid(), "a@b.com", "A");
        Guid roleId = Guid.NewGuid();

        user.AssignRole(roleId);
        user.AssignRole(roleId);

        user.RoleIds.Count.ShouldBe(1);
        user.RoleIds.ShouldContain(roleId);
    }

    [Fact]
    public void RevokeRole_ShouldRemoveRole()
    {
        User user = User.ProvisionFromEntra(Guid.NewGuid(), Guid.NewGuid(), "a@b.com", "A");
        Guid roleId = Guid.NewGuid();
        user.AssignRole(roleId);

        user.RevokeRole(roleId);

        user.RoleIds.ShouldBeEmpty();
    }

    [Fact]
    public void AddToGroup_ShouldBeIdempotent()
    {
        User user = User.ProvisionFromEntra(Guid.NewGuid(), Guid.NewGuid(), "a@b.com", "A");
        Guid groupId = Guid.NewGuid();

        user.AddToGroup(groupId);
        user.AddToGroup(groupId);

        user.GroupIds.Count.ShouldBe(1);
        user.GroupIds.ShouldContain(groupId);
    }

    [Fact]
    public void RemoveFromGroup_ShouldRemoveGroup()
    {
        User user = User.ProvisionFromEntra(Guid.NewGuid(), Guid.NewGuid(), "a@b.com", "A");
        Guid groupId = Guid.NewGuid();
        user.AddToGroup(groupId);

        user.RemoveFromGroup(groupId);

        user.GroupIds.ShouldBeEmpty();
    }
}
