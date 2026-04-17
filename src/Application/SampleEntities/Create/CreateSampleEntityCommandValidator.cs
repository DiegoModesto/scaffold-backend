using FluentValidation;

namespace Application.SampleEntities.Create;

internal sealed class CreateSampleEntityCommandValidator : AbstractValidator<CreateSampleEntityCommand>
{
    public CreateSampleEntityCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(c => c.Description)
            .MaximumLength(2000);
    }
}
