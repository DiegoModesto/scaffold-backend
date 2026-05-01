using Application.Abstractions.Messaging;

namespace Application.SampleEntities.Publish;

public sealed record PublishSampleEventCommand(string Name, string? Description) : ICommand<Guid>;
