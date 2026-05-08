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

    [Fact]
    public void UpdateDetails_UpdatesNameAndDescription()
    {
        Role role = Role.Create(Guid.NewGuid(), "Old", "Old desc");

        role.UpdateDetails("New", "New desc");

        role.Name.ShouldBe("New");
        role.Description.ShouldBe("New desc");
    }

    [Fact]
    public void Delete_SetsIsDeleted()
    {
        Role role = Role.Create(Guid.NewGuid(), "Admin", "Admin");

        role.Delete();

        role.IsDeleted.ShouldBeTrue();
        role.DeletedAt.ShouldNotBeNull();
    }
}
