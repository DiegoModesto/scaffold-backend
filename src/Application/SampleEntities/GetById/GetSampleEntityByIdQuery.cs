using Application.Abstractions.Messaging;
using Application.SampleEntities.Dtos;

namespace Application.SampleEntities.GetById;

public sealed record GetSampleEntityByIdQuery(Guid Id) : IQuery<SampleEntityResponse>;
