using Application.SampleEntities.GetById;
using Web.API.Extensions;
using Web.API.Infrastructure;

namespace Web.API.Endpoints.SampleEntity;

internal sealed class GetSampleEntityByIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("sample-entities/{id:guid}", async (
                Guid id,
                GetSampleEntityByIdQueryHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetSampleEntityByIdQuery(id), cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.SampleEntity)
            .WithName("GetSampleEntityById");
    }
}
