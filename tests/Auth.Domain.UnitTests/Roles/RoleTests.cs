using Auth.Domain.Roles;
using Shouldly;

namespace Auth.Domain.UnitTests.Roles;

public sealed class RoleTests
{
    [Fact]
    public void AssignPermission_AddsOnce()
    {
        Role role = Role.Create(Guid.NewGuid(), "Admin", "Administrator");
        Guid permissionId = Guid.NewGuid();

        role.AssignPermission(permissionId);
        role.AssignPermission(permissionId);

        role.PermissionIds.Count.ShouldBe(1);
        role.PermissionIds.ShouldContain(permissionId);
    }

    [Fact]
    public void RevokePermission_Removes()
    {
        Role role = Role.Create(Guid.NewGuid(), "Admin", "Administrator");
        Guid permissionId = Guid.NewGuid();
        role.AssignPermission(permissionId);

        role.RevokePermission(permissionId);

        role.PermissionIds.ShouldBeEmpty();
    }
}
