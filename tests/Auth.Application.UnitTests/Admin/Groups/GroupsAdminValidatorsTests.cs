using Auth.Application.Admin.Groups.Create;
using Auth.Application.Admin.Groups.Update;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.Groups;

public class GroupsAdminValidatorsTests
{
    [Fact]
    public void Create_Should_RejectEmptyName()
    {
        var v = new CreateGroupCommandValidator();
        v.Validate(new CreateGroupCommand("", "desc", null)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Create_Should_RejectEmptyDescription()
    {
        var v = new CreateGroupCommandValidator();
        v.Validate(new CreateGroupCommand("Eng", "", null)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Create_Should_AcceptValid()
    {
        var v = new CreateGroupCommandValidator();
        v.Validate(new CreateGroupCommand("Eng", "Engineering", Guid.NewGuid())).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Update_Should_RejectEmptyId()
    {
        var v = new UpdateGroupCommandValidator();
        v.Validate(new UpdateGroupCommand(Guid.Empty, "Eng", "desc", null)).IsValid.ShouldBeFalse();
    }
}
