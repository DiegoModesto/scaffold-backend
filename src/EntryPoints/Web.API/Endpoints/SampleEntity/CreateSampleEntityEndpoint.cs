using Application.Abstractions.Messaging;
using Application.SampleEntities.Create;
using Web.API.Endpoints.SampleEntity.Requests;
using Web.API.Extensions;
using Web.API.Infrastructure;

namespace Web.API.Endpoints.SampleEntity;

internal sealed class CreateSampleEntityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("sample-entities", async (
                CreateSampleEntityRequest request,
                ICommandHandler<CreateSampleEntityCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateSampleEntityCommand(request.Name, request.Description);

                var result = await handler.Handle(command, cancellationToken);

                return result.Match(
                    id => Results.Created($"/api/v1/sample-entities/{id}", new { id }),
                    CustomResults.Problem);
            })
            .WithTags(Tags.SampleEntity)
            .WithName("CreateSampleEntity")
            .RequireAuthorization($"{Infra.Authorization.PermissionPolicyProvider.PolicyPrefix}sample.write");
    }
}
