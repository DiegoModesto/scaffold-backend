using Auth.Application.Abstractions.Identity;
using Auth.Application.Users.GetEffectivePermissions;
using Auth.Domain.Users;
using Moq;
using SharedKernel;
using Shouldly;

namespace Auth.Application.UnitTests.Users.GetEffectivePermissions;

public sealed class GetEffectivePermissionsQueryHandlerTests
{
    [Fact]
    public async Task Should_ReturnPermissions_WhenResolverSucceeds()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        IReadOnlyCollection<string> codes = new[] { "orders.read", "orders.write" };

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(codes));

        var handler = new GetEffectivePermissionsQueryHandler(resolver.Object);

        var result = await handler.Handle(
            new GetEffectivePermissionsQuery(tenantId, userId),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(codes);
        resolver.VerifyAll();
    }

    [Fact]
    public async Task Should_FailNotFound_WhenResolverFails()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyCollection<string>>(UserErrors.NotFound(userId)));

        var handler = new GetEffectivePermissionsQueryHandler(resolver.Object);

        var result = await handler.Handle(
            new GetEffectivePermissionsQuery(tenantId, userId),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.NotFound");
        resolver.VerifyAll();
    }
}
