namespace Auth.Application.Admin.Groups.Dtos;

public sealed record GroupSummary(Guid Id, string Name, string Description, Guid? EntraGroupId);
