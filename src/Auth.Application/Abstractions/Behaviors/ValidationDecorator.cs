using Auth.Application.Abstractions.Messaging;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;

namespace Auth.Application.Abstractions.Behaviors;

internal static class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            ValidationFailure[] failures = await ValidateAsync(command, validators, cancellationToken);

            if (failures.Length == 0)
            {
                return await innerHandler.Handle(command, cancellationToken);
            }

            return Result.Failure<TResponse>(CreateValidationError(failures));
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            ValidationFailure[] failures = await ValidateAsync(command, validators, cancellationToken);

            if (failures.Length == 0)
            {
                return await innerHandler.Handle(command, cancellationToken);
            }

            return Result.Failure(CreateValidationError(failures));
        }
    }

    private static async Task<ValidationFailure[]> ValidateAsync<TCommand>(
        TCommand command,
        IEnumerable<IValidator<TCommand>> validators,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return [];
        }

        var context = new ValidationContext<TCommand>(command);

        ValidationResult[] results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        return results.Where(r => !r.IsValid).SelectMany(r => r.Errors).ToArray();
    }

    private static ValidationError CreateValidationError(ValidationFailure[] failures) =>
        new([.. failures.Select(f => Error.Validation(f.PropertyName, f.ErrorMessage))]);
}
