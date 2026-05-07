using FluentValidation;

namespace Auth.Application.Admin.M2MClients.Create;

internal sealed class CreateM2MClientCommandValidator : AbstractValidator<CreateM2MClientCommand>
{
    public CreateM2MClientCommandValidator()
    {
        RuleFor(c => c.ClientId).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.AllowedScopes).NotNull();
        RuleForEach(c => c.AllowedScopes).NotEmpty().MaximumLength(200);
    }
}
