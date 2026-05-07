using Auth.Application.Abstractions.Crypto;
using Auth.Application.M2MClients.Authenticate;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.M2MClients;
using Moq;
using Shouldly;

namespace Auth.Application.UnitTests.M2MClients.Authenticate;

public class AuthenticateM2MClientCommandHandlerTests
{
    [Fact]
    public async Task Should_ReturnM2MClientAuthenticated_OnValidCredentials()
    {
        await using var ctx = TestAuthDbContext.Create();
        var tenantId = Guid.NewGuid();
        var client = M2MClient.Register(tenantId, "client-1", "hashed", "Display", ["scope.a", "scope.b"]);
        ctx.M2MClients.Add(client);
        await ctx.SaveChangesAsync();

        var hasher = new Mock<IClientSecretHasher>();
        hasher.Setup(h => h.Verify("plain-secret", "hashed")).Returns(true);

        var handler = new AuthenticateM2MClientCommandHandler(ctx, hasher.Object);

        var result = await handler.Handle(
            new AuthenticateM2MClientCommand("client-1", "plain-secret"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TenantId.ShouldBe(tenantId);
        result.Value.ClientId.ShouldBe("client-1");
        result.Value.AllowedScopes.ShouldBe(["scope.a", "scope.b"]);
    }

    [Fact]
    public async Task Should_FailInvalidSecret_OnWrongSecret()
    {
        await using var ctx = TestAuthDbContext.Create();
        var client = M2MClient.Register(Guid.NewGuid(), "client-1", "hashed", "Display", ["scope.a"]);
        ctx.M2MClients.Add(client);
        await ctx.SaveChangesAsync();

        var hasher = new Mock<IClientSecretHasher>();
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var handler = new AuthenticateM2MClientCommandHandler(ctx, hasher.Object);

        var result = await handler.Handle(
            new AuthenticateM2MClientCommand("client-1", "wrong"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("M2MClient.InvalidSecret");
    }

    [Fact]
    public async Task Should_FailNotFound_WhenClientIdUnknown()
    {
        await using var ctx = TestAuthDbContext.Create();
        var hasher = new Mock<IClientSecretHasher>();
        var handler = new AuthenticateM2MClientCommandHandler(ctx, hasher.Object);

        var result = await handler.Handle(
            new AuthenticateM2MClientCommand("missing", "secret"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("M2MClient.NotFound");
    }

    [Fact]
    public async Task Should_FailInactive_WhenClientDeactivated()
    {
        await using var ctx = TestAuthDbContext.Create();
        var client = M2MClient.Register(Guid.NewGuid(), "client-1", "hashed", "Display", ["scope.a"]);
        client.Deactivate();
        ctx.M2MClients.Add(client);
        await ctx.SaveChangesAsync();

        var hasher = new Mock<IClientSecretHasher>();
        var handler = new AuthenticateM2MClientCommandHandler(ctx, hasher.Object);

        var result = await handler.Handle(
            new AuthenticateM2MClientCommand("client-1", "secret"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("M2MClient.Inactive");
    }
}
