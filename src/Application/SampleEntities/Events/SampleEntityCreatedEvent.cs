namespace Application.SampleEntities.Events;

public sealed record SampleEntityCreatedEvent(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset OccurredAt);
