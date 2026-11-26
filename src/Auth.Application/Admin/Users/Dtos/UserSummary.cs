namespace Auth.Application.Admin.Users.Dtos;

public sealed record UserSummary(Guid Id, string Email, string DisplayName, bool IsActive);
