using System.Diagnostics.CodeAnalysis;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Outcome of a service operation: the payload on success, or an error code + message on
    /// failure. Controllers map it to an IActionResult with <see cref="ServiceResultExtensions.ToActionResult{T}"/>.
    ///
    /// A method succeeds with <c>ServiceResult.Ok(dto)</c> and fails with
    /// <c>ServiceResult.Fail(code, message)</c>, which converts to a result of any payload type.
    ///
    /// <see cref="Data"/> is set on success. The one exception is a nullable payload type
    /// (<c>ServiceResult&lt;Word?&gt;</c>), where null is itself the answer — the compiler still
    /// treats it as set after <see cref="IsSuccess"/>, so check it before use.
    /// </summary>
    public sealed record ServiceResult<T>(T? Data, ErrorCodes? Error, string? ErrorMessage)
    {
        [MemberNotNullWhen(true, nameof(Data))]
        [MemberNotNullWhen(false, nameof(Error), nameof(ErrorMessage))]
        public bool IsSuccess => Error is null;

        public static ServiceResult<T> Ok(T data) => new(data, null, null);

        /// <summary>This failure, to hand on from a method whose payload is of another type.</summary>
        public ServiceFailure AsFailure() =>
            IsSuccess
                ? throw new InvalidOperationException("A successful result is not a failure.")
                : new ServiceFailure(Error.Value, ErrorMessage);

        // Deliberately no implicit conversion from T: with T = object it would accept any value —
        // another ServiceResult included — and wrap it as the payload.

        public static implicit operator ServiceResult<T>(ServiceFailure failure) =>
            new(default, failure.Error, failure.Message);
    }

    /// <summary>A failure not yet tied to a payload type; converts to any <see cref="ServiceResult{T}"/>.</summary>
    public readonly record struct ServiceFailure(ErrorCodes Error, string Message);

    /// <summary>The payload of an operation that succeeds with nothing to return.</summary>
    public sealed class NoData
    {
        public static readonly NoData Value = new();

        private NoData() { }
    }

    public static class ServiceResult
    {
        public static ServiceResult<T> Ok<T>(T data) => ServiceResult<T>.Ok(data);

        /// <summary>Success with nothing to return; answered as <c>{ "data": null }</c>.</summary>
        public static ServiceResult<NoData> Ok() => ServiceResult<NoData>.Ok(NoData.Value);

        public static ServiceFailure Fail(ErrorCodes error, string message) => new(error, message);
    }
}
