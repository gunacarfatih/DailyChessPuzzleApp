namespace DailyChessPuzzleApi.Exceptions;

public sealed class AppException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    public AppException(int statusCode, string errorCode)
        : base(errorCode)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
