namespace Infra.Authentication;

public sealed class IntrospectionCacheOptions
{
    public const string SectionName = "IntrospectionCache";

    public int TtlSeconds { get; set; } = 30;

    public TimeSpan Ttl => TimeSpan.FromSeconds(TtlSeconds);
}
