using Auth.Application.NetSuite.InitiateSso;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Users;
using Moq;
using Shouldly;

namespace Auth.Application.UnitTests.NetSuite.InitiateSso;

public sealed class InitiateNetSuiteSsoCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static StubTenantContext Tenant() => new(TenantId);

    private static (TestAuthDbContext ctx, Mock<INetSuiteSamlSigner> signer)
        BuildDeps()
    {
        var ctx = TestAuthDbContext.Create();
        var signer = new Mock<INetSuiteSamlSigner>(MockBehavior.Strict);
        return (ctx, signer);
    }

    private static User Activate(User u)
    {
        u.ActivateFromEntra(Guid.NewGuid(), u.DisplayName);
        return u;
    }

    [Fact]
    public async Task Handle_ReturnsAssertion_WhenUserHasNetSuiteEmail()
    {
        (TestAuthDbContext ctx, Mock<INetSuiteSamlSigner> signer) = BuildDeps();

        User user = Activate(User.PreProvision(TenantId, "alice@example.com", "Alice"));
        user.SetNetSuiteEmail("alice@netsuite.example");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var stub = new SignedNetSuiteAssertion("https://acs", "BASE64", "rs");
        signer.Setup(s => s.Sign("alice@netsuite.example", user.Id, "rs"))
            .Returns(stub);

        var handler = new InitiateNetSuiteSsoCommandHandler(ctx, signer.Object, Tenant());

        var result = await handler.Handle(
            new InitiateNetSuiteSsoCommand(user.Id, "rs"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(stub);
        signer.VerifyAll();
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenUserMissing()
    {
        (TestAuthDbContext ctx, Mock<INetSuiteSamlSigner> signer) = BuildDeps();
        var handler = new InitiateNetSuiteSsoCommandHandler(ctx, signer.Object, Tenant());

        Guid missing = Guid.NewGuid();
        var result = await handler.Handle(
            new InitiateNetSuiteSsoCommand(missing, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.NotFound");
    }

    [Fact]
    public async Task Handle_ReturnsDisabled_WhenUserIsInactive()
    {
        (TestAuthDbContext ctx, Mock<INetSuiteSamlSigner> signer) = BuildDeps();

        // PreProvision creates IsActive=false
        User user = User.PreProvision(TenantId, "bob@example.com", "Bob");
        user.SetNetSuiteEmail("bob@netsuite.example");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new InitiateNetSuiteSsoCommandHandler(ctx, signer.Object, Tenant());

        var result = await handler.Handle(
            new InitiateNetSuiteSsoCommand(user.Id, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.Disabled);
    }

    [Fact]
    public async Task Handle_ReturnsNetSuiteEmailMissing_WhenEmailNotSet()
    {
        (TestAuthDbContext ctx, Mock<INetSuiteSamlSigner> signer) = BuildDeps();

        User user = Activate(User.PreProvision(TenantId, "carol@example.com", "Carol"));
        // No NetSuite email set
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new InitiateNetSuiteSsoCommandHandler(ctx, signer.Object, Tenant());

        var result = await handler.Handle(
            new InitiateNetSuiteSsoCommand(user.Id, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.NetSuiteEmailMissing);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenUserBelongsToDifferentTenant()
    {
        (TestAuthDbContext ctx, Mock<INetSuiteSamlSigner> signer) = BuildDeps();

        Guid otherTenantId = Guid.NewGuid();
        User user = Activate(User.PreProvision(otherTenantId, "eve@example.com", "Eve"));
        user.SetNetSuiteEmail("eve@netsuite.example");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        // Current tenant context is TenantId — not otherTenantId.
        var handler = new InitiateNetSuiteSsoCommandHandler(ctx, signer.Object, Tenant());

        var result = await handler.Handle(
            new InitiateNetSuiteSsoCommand(user.Id, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.NotFound");
    }
}
