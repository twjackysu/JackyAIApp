namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Generic outcome of a service operation: an optional payload on success, or an
    /// error code + message on failure. Controllers map this to an IActionResult.
    /// </summary>
    public sealed record ServiceResult(object? Data, ErrorCodes? Error, string? ErrorMessage)
    {
        public bool IsSuccess => Error is null;

        public static ServiceResult Ok(object? data = null) => new(data, null, null);

        public static ServiceResult Fail(ErrorCodes error, string message) => new(null, error, message);
    }
}
