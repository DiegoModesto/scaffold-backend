using FluentValidation;

namespace Auth.Application.Users.SyncEntra;

internal sealed class SyncEntraUserCommandValidator : AbstractValidator<SyncEntraUserCommand>
{
    public SyncEntraUserCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.EntraOid).NotEmpty();
        RuleFor(c => c.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();
        RuleFor(c => c.DisplayName)
            .NotEmpty()
            .MaximumLength(200);
    }
}
