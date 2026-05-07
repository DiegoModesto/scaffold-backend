namespace Auth.Infra.Database.Joins;

internal sealed class GroupRole
{
    public Guid GroupId { get; init; }
    public Guid RoleId { get; init; }
}
