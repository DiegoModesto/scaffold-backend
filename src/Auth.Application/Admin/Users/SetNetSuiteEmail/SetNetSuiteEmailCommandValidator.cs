using FluentValidation;

namespace Auth.Application.Admin.Users.SetNetSuiteEmail;

internal sealed class SetNetSuiteEmailCommandValidator : AbstractValidator<SetNetSuiteEmailCommand>
{
    public SetNetSuiteEmailCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        When(c => !string.IsNullOrEmpty(c.NetSuiteEmail), () =>
        {
            RuleFor(c => c.NetSuiteEmail!)
                .MaximumLength(320)
                .EmailAddress();
        });
    }
}
