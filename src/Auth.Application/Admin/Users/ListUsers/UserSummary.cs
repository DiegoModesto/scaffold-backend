namespace Auth.Application.Admin.Users.ListUsers;

public sealed record UserSummary(Guid Id, string Email, string DisplayName, bool IsActive);
