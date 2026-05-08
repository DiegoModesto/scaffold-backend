using Auth.Application.Abstractions.Messaging;
using Auth.Application.Common;
using Auth.Domain.Audit;

namespace Auth.Application.Admin.Audit.ListAuditEvents;

public sealed record ListAuditEventsQuery(
    int Page,
    int PageSize,
    Guid? UserId,
    AuthAuditEventType? EventType,
    DateTimeOffset? From,
    DateTimeOffset? To) : IQuery<PagedResponse<AuthAuditEventResponse>>;

public sealed record AuthAuditEventResponse(
    Guid Id,
    Guid? UserId,
    AuthAuditEventType EventType,
    string Ip,
    string UserAgent,
    string Detail,
    DateTimeOffset OccurredAt);
