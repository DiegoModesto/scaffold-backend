namespace Auth.Infra.Database.Joins;

internal sealed class UserGroup
{
    public Guid UserId { get; init; }
    public Guid GroupId { get; init; }
}
