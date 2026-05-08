using Application.Abstractions.Messaging;
using Application.SampleEntities.Publish;
using Web.API.Extensions;
using Web.API.Infrastructure;

namespace Web.API.Endpoints.SampleEntity;

internal sealed class PublishSampleEventEndpoint : IEndpoint
{
    public sealed record Request(string Name, string? Description);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("sample-entities/publish", async (
                Request request,
                ICommandHandler<PublishSampleEventCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new PublishSampleEventCommand(request.Name, request.Description);

                var result = await handler.Handle(command, cancellationToken);

                return result.Match(
                    id => Results.Accepted($"/api/v1/sample-entities/{id}", new { id }),
                    CustomResults.Problem);
            })
            .WithTags(Tags.SampleEntity)
            .WithName("PublishSampleEvent")
            .RequireAuthorization($"{Infra.Authorization.PermissionPolicyProvider.PolicyPrefix}sample.write");
    }
}
