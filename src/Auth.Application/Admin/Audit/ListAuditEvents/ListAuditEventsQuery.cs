using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Audit.Dtos;
using Auth.Application.Common;
using Auth.Application.Common.Dtos;
using Auth.Domain.Audit;

namespace Auth.Application.Admin.Audit.ListAuditEvents;

public sealed record ListAuditEventsQuery(
    int Page,
    int PageSize,
    Guid? UserId,
    AuthAuditEventType? EventType,
    DateTimeOffset? From,
    DateTimeOffset? To) : IQuery<PagedResponse<AuthAuditEventResponse>>;
