using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharedKernel.Enumerations;

[SuppressMessage("Major Code Smell", "S4035:Classes implementing 'IEquatable<T>' should be sealed",
    Justification = "Enumeration<TEnum> is the abstract base of the Enumeration pattern; equality is by (Value, Name) which is invariant across derived types.")]
public abstract class Enumeration<TEnum> : IEquatable<Enumeration<TEnum>>
    where TEnum : Enumeration<TEnum>
{
    private static readonly Dictionary<int, TEnum> Enumerations = CreateEnumerations();

    protected Enumeration(int value, string name)
    {
        Value = value;
        Name = name;
    }

    public int Value { get; protected init; }
    public string Name { get; protected init; }

    public static TEnum? FromValue(int value) =>
        Enumerations.TryGetValue(value, out TEnum? enumeration) ? enumeration : null;

    public static TEnum? FromValue(string name) =>
        Enumerations.Values.SingleOrDefault(x => x!.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

    private static Dictionary<int, TEnum> CreateEnumerations()
    {
        Type enumerationType = typeof(TEnum);

        IEnumerable<TEnum?> fieldsForType = enumerationType
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => enumerationType.IsAssignableFrom(f.FieldType))
            .Select(f => (TEnum)f.GetValue(default)!);

        return fieldsForType.ToDictionary(f => f!.Value)!;
    }

    public override int GetHashCode() => HashCode.Combine(Value, Name);

    public override bool Equals(object? obj) =>
        obj is Enumeration<TEnum> other && Equals(other);

    public bool Equals(Enumeration<TEnum>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Value == other.Value && Name == other.Name;
    }
}
