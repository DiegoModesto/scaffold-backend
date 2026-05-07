using Auth.Application.Abstractions.Crypto;
using Auth.Application.Admin.M2MClients.Create;
using Auth.Application.Admin.M2MClients.Deactivate;
using Auth.Application.Admin.M2MClients.GetM2MClient;
using Auth.Application.Admin.M2MClients.ListM2MClients;
using Auth.Application.Admin.M2MClients.RegenerateSecret;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.M2MClients;
using Moq;
using OpenIddict.Abstractions;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.M2MClients;

public class M2MClientsAdminHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static StubTenantContext Tenant() => new(TenantId);

    private sealed class FakeHasher : IClientSecretHasher
    {
        public string Hash(string secret) => "h:" + secret;
        public bool Verify(string secret, string hash) => hash == "h:" + secret;
    }

    [Fact]
    public async Task ListM2MClients_Should_PaginateAndScopeToTenant()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.M2MClients.Add(M2MClient.Register(TenantId, "svc-a", "h", "Service A", ["api:auth"]));
        ctx.M2MClients.Add(M2MClient.Register(TenantId, "svc-b", "h", "Service B", []));
        ctx.M2MClients.Add(M2MClient.Register(Guid.NewGuid(), "other", "h", "Other", []));
        await ctx.SaveChangesAsync();

        var handler = new ListM2MClientsQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new ListM2MClientsQuery(1, 10, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Total.ShouldBe(2);
    }

    [Fact]
    public async Task GetM2MClientById_Should_ReturnDetail()
    {
        await using var ctx = TestAuthDbContext.Create();
        var client = M2MClient.Register(TenantId, "svc-a", "h", "Service A", ["api:auth", "api:web"]);
        ctx.M2MClients.Add(client);
        await ctx.SaveChangesAsync();

        var handler = new GetM2MClientByIdQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new GetM2MClientByIdQuery(client.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("svc-a");
        result.Value.AllowedScopes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetM2MClientById_Should_FailNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var handler = new GetM2MClientByIdQueryHandler(ctx, Tenant());

        var result = await handler.Handle(new GetM2MClientByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("M2MClient.NotFound");
    }

    [Fact]
    public async Task CreateM2MClient_Should_PersistAndCreateOpenIddictApp_AndReturnSecret()
    {
        await using var ctx = TestAuthDbContext.Create();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<object>(new object()));

        var handler = new CreateM2MClientCommandHandler(ctx, Tenant(), new FakeHasher(), manager.Object);

        var result = await handler.Handle(
            new CreateM2MClientCommand("svc-a", "Service A", ["api:auth"]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("svc-a");
        result.Value.ClientSecret.ShouldNotBeNullOrEmpty();

        var stored = await ctx.M2MClients.FindAsync(result.Value.Id);
        stored.ShouldNotBeNull();
        stored!.ClientSecretHash.ShouldStartWith("h:");

        manager.Verify(
            m => m.CreateAsync(
                It.Is<OpenIddictApplicationDescriptor>(d =>
                    d.ClientId == "svc-a" &&
                    d.ClientSecret == result.Value.ClientSecret &&
                    d.Permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Token) &&
                    d.Permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateM2MClient_Should_FailConflict_WhenClientIdExists()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.M2MClients.Add(M2MClient.Register(TenantId, "svc-a", "h", "Service A", []));
        await ctx.SaveChangesAsync();

        var manager = new Mock<IOpenIddictApplicationManager>();
        var handler = new CreateM2MClientCommandHandler(ctx, Tenant(), new FakeHasher(), manager.Object);

        var result = await handler.Handle(
            new CreateM2MClientCommand("svc-a", "Service A", []),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("M2MClient.ClientIdAlreadyTaken");
        manager.Verify(
            m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegenerateSecret_Should_RotateSecretAndUpdateOpenIddict()
    {
        await using var ctx = TestAuthDbContext.Create();
        var client = M2MClient.Register(TenantId, "svc-a", "h:old", "Service A", []);
        ctx.M2MClients.Add(client);
        await ctx.SaveChangesAsync();

        var fakeApp = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(m => m.FindByClientIdAsync("svc-a", It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<object?>(fakeApp));
        manager.Setup(m => m.PopulateAsync(
                It.IsAny<OpenIddictApplicationDescriptor>(), fakeApp, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        manager.Setup(m => m.UpdateAsync(
                fakeApp, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var handler = new RegenerateM2MClientSecretCommandHandler(
            ctx, Tenant(), new FakeHasher(), manager.Object);

        var result = await handler.Handle(
            new RegenerateM2MClientSecretCommand(client.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNullOrEmpty();
        client.ClientSecretHash.ShouldBe("h:" + result.Value);
        manager.Verify(
            m => m.UpdateAsync(fakeApp, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RegenerateSecret_Should_FailNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var manager = new Mock<IOpenIddictApplicationManager>();
        var handler = new RegenerateM2MClientSecretCommandHandler(
            ctx, Tenant(), new FakeHasher(), manager.Object);

        var result = await handler.Handle(
            new RegenerateM2MClientSecretCommand(Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("M2MClient.NotFound");
    }

    [Fact]
    public async Task Deactivate_Should_DeactivateAndDeleteOpenIddictApp()
    {
        await using var ctx = TestAuthDbContext.Create();
        var client = M2MClient.Register(TenantId, "svc-a", "h", "Service A", []);
        ctx.M2MClients.Add(client);
        await ctx.SaveChangesAsync();

        var fakeApp = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(m => m.FindByClientIdAsync("svc-a", It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<object?>(fakeApp));
        manager.Setup(m => m.DeleteAsync(fakeApp, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var handler = new DeactivateM2MClientCommandHandler(ctx, Tenant(), manager.Object);

        var result = await handler.Handle(new DeactivateM2MClientCommand(client.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        client.IsActive.ShouldBeFalse();
        manager.Verify(m => m.DeleteAsync(fakeApp, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivate_Should_FailNotFound()
    {
        await using var ctx = TestAuthDbContext.Create();
        var manager = new Mock<IOpenIddictApplicationManager>();
        var handler = new DeactivateM2MClientCommandHandler(ctx, Tenant(), manager.Object);

        var result = await handler.Handle(new DeactivateM2MClientCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("M2MClient.NotFound");
    }
}
