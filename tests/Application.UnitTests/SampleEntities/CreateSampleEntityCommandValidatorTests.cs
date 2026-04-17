using Application.SampleEntities.Create;
using FluentValidation.TestHelper;

namespace Application.UnitTests.SampleEntities;

public class CreateSampleEntityCommandValidatorTests
{
    private readonly CreateSampleEntityCommandValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Fail_When_NameIsEmpty(string name)
    {
        var result = _validator.TestValidate(new CreateSampleEntityCommand(name, null));
        result.ShouldHaveValidationErrorFor(c => c.Name);
    }

    [Fact]
    public void Should_Fail_When_NameTooLong()
    {
        var longName = new string('a', 201);
        var result = _validator.TestValidate(new CreateSampleEntityCommand(longName, null));
        result.ShouldHaveValidationErrorFor(c => c.Name);
    }

    [Fact]
    public void Should_Fail_When_DescriptionTooLong()
    {
        var longDesc = new string('a', 2001);
        var result = _validator.TestValidate(new CreateSampleEntityCommand("ok", longDesc));
        result.ShouldHaveValidationErrorFor(c => c.Description);
    }

    [Fact]
    public void Should_Pass_When_CommandIsValid()
    {
        var result = _validator.TestValidate(new CreateSampleEntityCommand("My Entity", "a description"));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
