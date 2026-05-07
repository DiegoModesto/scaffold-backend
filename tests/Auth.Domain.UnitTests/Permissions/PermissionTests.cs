using Auth.Domain.Permissions;
using Shouldly;

namespace Auth.Domain.UnitTests.Permissions;

public sealed class PermissionTests
{
    [Fact]
    public void Create_ShouldStoreCodeAndDescription()
    {
        Permission permission = Permission.Create("users.read", "Read users");

        permission.Id.ShouldNotBe(Guid.Empty);
        permission.Code.ShouldBe("users.read");
        permission.Description.ShouldBe("Read users");
    }
}
