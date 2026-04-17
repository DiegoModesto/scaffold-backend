using Application.Abstractions.Behaviors;
using Application.Abstractions.Messaging;
using FluentValidation;
using SharedKernel;
using Shouldly;

namespace Application.UnitTests.Behaviors;

public class ValidationDecoratorTests
{
    public sealed record FakeCommand(string Value) : ICommand<string>;

    private sealed class FakeValidator : AbstractValidator<FakeCommand>
    {
        public FakeValidator() => RuleFor(c => c.Value).NotEmpty();
    }

    private sealed class StubHandler(Result<string> response, Action? onCall = null)
        : ICommandHandler<FakeCommand, string>
    {
        public int CallCount { get; private set; }

        public Task<Result<string>> Handle(FakeCommand command, CancellationToken cancellationToken)
        {
            CallCount++;
            onCall?.Invoke();
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Should_Return_ValidationError_When_Invalid()
    {
        var inner = new StubHandler(Result.Success("shouldnt-reach"));
        var decorator = new ValidationDecorator.CommandHandler<FakeCommand, string>(
            inner, [new FakeValidator()]);

        var result = await decorator.Handle(new FakeCommand(""), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ValidationError>();
        inner.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Should_CallInnerHandler_When_Valid()
    {
        var inner = new StubHandler(Result.Success("ok"));
        var decorator = new ValidationDecorator.CommandHandler<FakeCommand, string>(
            inner, [new FakeValidator()]);

        var result = await decorator.Handle(new FakeCommand("valid"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ok");
        inner.CallCount.ShouldBe(1);
    }
}
