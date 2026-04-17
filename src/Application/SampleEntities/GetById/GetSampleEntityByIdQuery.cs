using Application.Abstractions.Messaging;

namespace Application.SampleEntities.GetById;

public sealed record GetSampleEntityByIdQuery(Guid Id) : IQuery<SampleEntityResponse>;

public sealed record SampleEntityResponse(Guid Id, string Name, string? Description);
