using Auth.Domain.Audit;
using Shouldly;

namespace Auth.Domain.UnitTests.Audit;

public sealed class AuthAuditEventTests
{
    [Fact]
    public void Record_ShouldCaptureTenantUserAndIp()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        AuthAuditEvent evt = AuthAuditEvent.Record(
            tenantId,
            userId,
            AuthAuditEventType.LoginSucceeded,
            "127.0.0.1",
            "Mozilla/5.0",
            "User logged in");

        evt.Id.ShouldNotBe(Guid.Empty);
        evt.TenantId.ShouldBe(tenantId);
        evt.UserId.ShouldBe(userId);
        evt.EventType.ShouldBe(AuthAuditEventType.LoginSucceeded);
        evt.Ip.ShouldBe("127.0.0.1");
        evt.UserAgent.ShouldBe("Mozilla/5.0");
        evt.Detail.ShouldBe("User logged in");
        evt.OccurredAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
    }
}
