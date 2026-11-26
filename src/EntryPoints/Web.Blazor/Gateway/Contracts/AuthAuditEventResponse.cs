namespace Web.Blazor.Gateway.Contracts;

public sealed record AuthAuditEventResponse(
    Guid Id,
    Guid? UserId,
    string EventType,
    string Ip,
    string UserAgent,
    string Detail,
    DateTimeOffset OccurredAt);
