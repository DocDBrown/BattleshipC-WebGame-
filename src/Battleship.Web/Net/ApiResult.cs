namespace Battleship.Web.Net;

public sealed record ApiResult<T>(bool Success, T? Value, string? ErrorCode, string? Detail)
{
    public static ApiResult<T> Ok(T value) => new(true, value, null, null);

    public static ApiResult<T> Fail(string errorCode, string detail) =>
        new(false, default, errorCode, detail);
}
