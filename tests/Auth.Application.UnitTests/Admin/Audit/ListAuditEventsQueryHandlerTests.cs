using Auth.Application.Admin.Audit.ListAuditEvents;
using Auth.Application.UnitTests.Infrastructure;
using Auth.Domain.Audit;
using Shouldly;

namespace Auth.Application.UnitTests.Admin.Audit;

public class ListAuditEventsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static StubTenantContext Tenant() => new(TenantId);

    [Fact]
    public async Task Should_ReturnEventsForTenant_OrderedByOccurredAtDesc()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.AuditEvents.Add(
            AuthAuditEvent.Record(TenantId, null, AuthAuditEventType.LoginSucceeded, "1.1.1.1", "ua", "first"));
        await Task.Delay(2);
        ctx.AuditEvents.Add(
            AuthAuditEvent.Record(TenantId, null, AuthAuditEventType.LoginSucceeded, "1.1.1.1", "ua", "second"));
        ctx.AuditEvents.Add(
            AuthAuditEvent.Record(Guid.NewGuid(), null, AuthAuditEventType.LoginSucceeded, "1.1.1.1", "ua", "other-tenant"));
        await ctx.SaveChangesAsync();

        var handler = new ListAuditEventsQueryHandler(ctx, Tenant());

        var result = await handler.Handle(
            new ListAuditEventsQuery(1, 10, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Total.ShouldBe(2);
        result.Value.Items.Select(i => i.Detail).ShouldNotContain("other-tenant");
        result.Value.Items.First().Detail.ShouldBe("second");
    }

    [Fact]
    public async Task Should_FilterByEventType()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.AuditEvents.Add(
            AuthAuditEvent.Record(TenantId, null, AuthAuditEventType.LoginSucceeded, "1", "ua", "ok"));
        ctx.AuditEvents.Add(
            AuthAuditEvent.Record(TenantId, null, AuthAuditEventType.LoginFailed, "1", "ua", "fail"));
        await ctx.SaveChangesAsync();

        var handler = new ListAuditEventsQueryHandler(ctx, Tenant());

        var result = await handler.Handle(
            new ListAuditEventsQuery(1, 10, null, AuthAuditEventType.LoginFailed, null, null),
            CancellationToken.None);

        result.Value.Total.ShouldBe(1);
        result.Value.Items.Single().Detail.ShouldBe("fail");
    }

    [Fact]
    public async Task Should_FilterByUserId()
    {
        await using var ctx = TestAuthDbContext.Create();
        var userId = Guid.NewGuid();
        ctx.AuditEvents.Add(
            AuthAuditEvent.Record(TenantId, userId, AuthAuditEventType.LoginSucceeded, "1", "ua", "u1"));
        ctx.AuditEvents.Add(
            AuthAuditEvent.Record(TenantId, Guid.NewGuid(), AuthAuditEventType.LoginSucceeded, "1", "ua", "u2"));
        await ctx.SaveChangesAsync();

        var handler = new ListAuditEventsQueryHandler(ctx, Tenant());

        var result = await handler.Handle(
            new ListAuditEventsQuery(1, 10, userId, null, null, null),
            CancellationToken.None);

        result.Value.Total.ShouldBe(1);
        result.Value.Items.Single().UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task Should_FilterByDateRange()
    {
        await using var ctx = TestAuthDbContext.Create();
        ctx.AuditEvents.Add(
            AuthAuditEvent.Record(TenantId, null, AuthAuditEventType.LoginSucceeded, "1", "ua", "in-range"));
        await ctx.SaveChangesAsync();

        var handler = new ListAuditEventsQueryHandler(ctx, Tenant());
        DateTimeOffset future = DateTimeOffset.UtcNow.AddDays(1);

        var result = await handler.Handle(
            new ListAuditEventsQuery(1, 10, null, null, future, null),
            CancellationToken.None);

        result.Value.Total.ShouldBe(0);
    }
}
