namespace Auth.Infra.Database.Joins;

internal sealed class UserRole
{
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
}
