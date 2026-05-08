using Auth.Application.Admin.Users.PreProvision;
using Auth.Application.Admin.Users.SetNetSuiteEmail;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.Users;

public class UsersAdminValidatorsTests
{
    [Fact]
    public void PreProvision_Should_RejectInvalidEmail()
    {
        var validator = new PreProvisionUserCommandValidator();

        var result = validator.Validate(new PreProvisionUserCommand("not-an-email", "Display", null));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void PreProvision_Should_AcceptValid()
    {
        var validator = new PreProvisionUserCommandValidator();

        var result = validator.Validate(
            new PreProvisionUserCommand("alice@example.com", "Alice", "ns@example.com"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void PreProvision_Should_AcceptNullNetSuiteEmail()
    {
        var validator = new PreProvisionUserCommandValidator();

        var result = validator.Validate(new PreProvisionUserCommand("alice@example.com", "Alice", null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void PreProvision_Should_RejectInvalidNetSuiteEmail()
    {
        var validator = new PreProvisionUserCommandValidator();

        var result = validator.Validate(new PreProvisionUserCommand("alice@example.com", "Alice", "invalid"));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void SetNetSuiteEmail_Should_AcceptNull()
    {
        var validator = new SetNetSuiteEmailCommandValidator();

        var result = validator.Validate(new SetNetSuiteEmailCommand(Guid.NewGuid(), null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void SetNetSuiteEmail_Should_RejectInvalidEmail()
    {
        var validator = new SetNetSuiteEmailCommandValidator();

        var result = validator.Validate(new SetNetSuiteEmailCommand(Guid.NewGuid(), "not-email"));

        result.IsValid.ShouldBeFalse();
    }
}
