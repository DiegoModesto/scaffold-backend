using FluentValidation;

namespace Auth.Application.Admin.Users.PreProvision;

internal sealed class PreProvisionUserCommandValidator : AbstractValidator<PreProvisionUserCommand>
{
    public PreProvisionUserCommandValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();

        RuleFor(c => c.DisplayName)
            .NotEmpty()
            .MaximumLength(200);

        When(c => !string.IsNullOrEmpty(c.NetSuiteEmail), () =>
        {
            RuleFor(c => c.NetSuiteEmail!)
                .MaximumLength(320)
                .EmailAddress();
        });
    }
}
