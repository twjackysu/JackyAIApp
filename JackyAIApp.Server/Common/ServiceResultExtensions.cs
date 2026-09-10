using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Common
{
    public static class ServiceResultExtensions
    {
        /// <summary>
        /// Turns a <see cref="ServiceResult"/> into the HTTP response it describes.
        ///
        /// Lives as an extension on the factory rather than on a controller base class because the
        /// controllers that need it do not share one base: two already derive from
        /// <see cref="ApiControllerBase"/> and four from <c>ControllerBase</c>. Putting it on a base
        /// would also mean giving that base a constructor parameter, which would shut out
        /// controllers that want <see cref="ApiControllerBase.CurrentUserId"/> but have no response
        /// factory of their own.
        /// </summary>
        public static IActionResult ToActionResult(this IMyResponseFactory responseFactory, ServiceResult result) =>
            result.IsSuccess
                ? responseFactory.CreateOKResponse(result.Data)
                : responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
