using SharedKernel;

namespace Auth.Domain.Audit;

public sealed class AuthAuditEvent : Entity
{
    private AuthAuditEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public AuthAuditEventType EventType { get; private set; }
    public string Ip { get; private set; } = string.Empty;
    public string UserAgent { get; private set; } = string.Empty;
    public string Detail { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }

    public static AuthAuditEvent Record(
        Guid tenantId,
        Guid? userId,
        AuthAuditEventType eventType,
        string ip,
        string userAgent,
        string detail)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new AuthAuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            EventType = eventType,
            Ip = ip,
            UserAgent = userAgent,
            Detail = detail,
            OccurredAt = now,
            CreatedAt = now,
        };
    }
}
