using Application.SampleEntities.Publish;
using FluentValidation.TestHelper;

namespace Application.UnitTests.SampleEntities;

public class PublishSampleEventCommandValidatorTests
{
    private readonly PublishSampleEventCommandValidator _validator = new();

    [Fact]
    public void Should_Pass_When_NameIsProvided()
    {
        var result = _validator.TestValidate(new PublishSampleEventCommand("ok", null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Fail_When_NameIsEmpty()
    {
        var result = _validator.TestValidate(new PublishSampleEventCommand(string.Empty, null));
        result.ShouldHaveValidationErrorFor(c => c.Name);
    }

    [Fact]
    public void Should_Fail_When_DescriptionExceedsMax()
    {
        var longDesc = new string('x', 2001);
        var result = _validator.TestValidate(new PublishSampleEventCommand("ok", longDesc));
        result.ShouldHaveValidationErrorFor(c => c.Description);
    }
}
