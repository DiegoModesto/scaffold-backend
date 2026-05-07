using Auth.Application.Admin.Roles.Create;
using Auth.Application.Admin.Roles.Update;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.Roles;

public class RolesAdminValidatorsTests
{
    [Fact]
    public void Create_Should_RejectEmptyName()
    {
        new CreateRoleCommandValidator()
            .Validate(new CreateRoleCommand("", "desc")).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Create_Should_AcceptValid()
    {
        new CreateRoleCommandValidator()
            .Validate(new CreateRoleCommand("Admin", "Admin role")).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Update_Should_RejectEmptyId()
    {
        new UpdateRoleCommandValidator()
            .Validate(new UpdateRoleCommand(Guid.Empty, "Admin", "desc")).IsValid.ShouldBeFalse();
    }
}
