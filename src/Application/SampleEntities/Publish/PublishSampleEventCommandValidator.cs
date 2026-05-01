using FluentValidation;

namespace Application.SampleEntities.Publish;

internal sealed class PublishSampleEventCommandValidator : AbstractValidator<PublishSampleEventCommand>
{
    public PublishSampleEventCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
    }
}
