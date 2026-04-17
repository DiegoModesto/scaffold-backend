using SharedKernel.Enumerations;

namespace SharedKernel;

public sealed class ErrorType : Enumeration<ErrorType>
{
    public static readonly ErrorType Failure = new(0, nameof(Failure));
    public static readonly ErrorType Validation = new(1, nameof(Validation));
    public static readonly ErrorType Problem = new(2, nameof(Problem));
    public static readonly ErrorType NotFound = new(3, nameof(NotFound));
    public static readonly ErrorType Conflict = new(4, nameof(Conflict));
    public static readonly ErrorType Forbidden = new(5, nameof(Forbidden));

    private ErrorType(int value, string name)
        : base(value, name)
    {
    }
}
