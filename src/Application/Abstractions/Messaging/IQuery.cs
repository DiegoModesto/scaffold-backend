using System.Diagnostics.CodeAnalysis;

namespace Application.Abstractions.Messaging;

[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed",
    Justification = "TResponse is a phantom type that constrains the matching IQueryHandler<TQuery, TResponse>.")]
public interface IQuery<TResponse>;
