using Auth.Application.Users.SyncEntra;
using FluentValidation.TestHelper;

namespace Auth.Application.UnitTests.Users.SyncEntra;

public class SyncEntraUserCommandValidatorTests
{
    private readonly SyncEntraUserCommandValidator _validator = new();

    [Fact]
    public void Should_NotHaveErrors_OnValidCommand()
    {
        var command = new SyncEntraUserCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "user@example.com",
            "Display Name");

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_HaveError_WhenTenantIdIsEmpty()
    {
        var command = new SyncEntraUserCommand(
            Guid.Empty,
            Guid.NewGuid(),
            "user@example.com",
            "Display");

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.TenantId);
    }

    [Fact]
    public void Should_HaveError_WhenEntraOidIsEmpty()
    {
        var command = new SyncEntraUserCommand(
            Guid.NewGuid(),
            Guid.Empty,
            "user@example.com",
            "Display");

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.EntraOid);
    }

    [Fact]
    public void Should_HaveError_WhenEmailIsInvalid()
    {
        var command = new SyncEntraUserCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "not-an-email",
            "Display");

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Should_HaveError_WhenEmailIsEmpty()
    {
        var command = new SyncEntraUserCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            string.Empty,
            "Display");

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Should_HaveError_WhenDisplayNameIsEmpty()
    {
        var command = new SyncEntraUserCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "user@example.com",
            string.Empty);

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.DisplayName);
    }

    [Fact]
    public void Should_HaveError_WhenDisplayNameTooLong()
    {
        var command = new SyncEntraUserCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "user@example.com",
            new string('a', 201));

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.DisplayName);
    }
}
