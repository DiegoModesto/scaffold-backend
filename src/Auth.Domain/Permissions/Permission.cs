using SharedKernel;

namespace Auth.Domain.Permissions;

public sealed class Permission : Entity
{
    private Permission()
    {
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public static Permission Create(string code, string description)
    {
        return new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
