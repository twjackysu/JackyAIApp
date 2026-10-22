using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Action filter for an endpoint only admins may call (<see cref="IAdminAccess"/>): anyone else
    /// is answered 403 + <see cref="ErrorCodes.Forbidden"/> before the action runs. Put
    /// <c>[Authorize]</c> on the endpoint (or its controller) too, so an anonymous request gets the
    /// usual 401 first.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class AdminOnlyAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var services = context.HttpContext.RequestServices;
            var adminAccess = services.GetRequiredService<IAdminAccess>();

            if (!await adminAccess.IsAdminAsync(context.HttpContext.RequestAborted))
            {
                var responseFactory = services.GetRequiredService<IMyResponseFactory>();
                context.Result = responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "Admin access required.");
                return;
            }

            await next();
        }
    }
}
