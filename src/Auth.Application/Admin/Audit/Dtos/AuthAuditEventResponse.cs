using Auth.Domain.Audit;

namespace Auth.Application.Admin.Audit.Dtos;

public sealed record AuthAuditEventResponse(
    Guid Id,
    Guid? UserId,
    AuthAuditEventType EventType,
    string Ip,
    string UserAgent,
    string Detail,
    DateTimeOffset OccurredAt);
