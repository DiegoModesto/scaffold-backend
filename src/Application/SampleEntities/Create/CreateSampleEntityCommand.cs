using Application.Abstractions.Messaging;

namespace Application.SampleEntities.Create;

public sealed record CreateSampleEntityCommand(string Name, string? Description) : ICommand<Guid>;
