using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Common
{
    public static class ServiceResultExtensions
    {
        /// <summary>
        /// Turns a <see cref="ServiceResult{T}"/> into the HTTP response it describes.
        ///
        /// Lives as an extension on the factory rather than on a controller base class, so any
        /// controller with a response factory can use it without deriving from anything.
        /// </summary>
        public static IActionResult ToActionResult<T>(this IMyResponseFactory responseFactory, ServiceResult<T> result) =>
            result.IsSuccess
                ? responseFactory.CreateOKResponse(result.Data)
                : responseFactory.CreateErrorResponse(result.Error.Value, result.ErrorMessage);

        /// <summary>A success with no payload is answered as <c>{ "data": null }</c>.</summary>
        public static IActionResult ToActionResult(this IMyResponseFactory responseFactory, ServiceResult<NoData> result) =>
            result.IsSuccess
                ? responseFactory.CreateOKResponse<object?>(null)
                : responseFactory.CreateErrorResponse(result.Error.Value, result.ErrorMessage);
    }
}
