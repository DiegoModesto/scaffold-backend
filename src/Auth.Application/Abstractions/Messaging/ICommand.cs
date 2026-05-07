using System.Diagnostics.CodeAnalysis;

namespace Auth.Application.Abstractions.Messaging;

public interface ICommand;

[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed",
    Justification = "TResponse is a phantom type that constrains the matching ICommandHandler<TCommand, TResponse>.")]
public interface ICommand<TResponse>;
