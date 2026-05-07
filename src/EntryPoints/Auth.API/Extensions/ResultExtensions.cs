using SharedKernel;

namespace Auth.API.Extensions;

internal static class ResultExtensions
{
    public static IResult Match<TValue>(
        this Result<TValue> result,
        Func<TValue, IResult> onSuccess,
        Func<Result, IResult> onFailure) =>
        result.IsSuccess ? onSuccess(result.Value) : onFailure(result);

    public static IResult Match(
        this Result result,
        Func<IResult> onSuccess,
        Func<Result, IResult> onFailure) =>
        result.IsSuccess ? onSuccess() : onFailure(result);
}
