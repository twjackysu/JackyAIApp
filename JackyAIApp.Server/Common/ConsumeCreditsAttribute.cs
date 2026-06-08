using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Action filter that consumes the current user's credits before the action runs.
    /// Short-circuits with a Forbidden response when the user is unauthenticated or
    /// has insufficient credits. Apply to AI-powered endpoints, e.g.
    /// <c>[ConsumeCredits(CreditCosts.ClozeTest, "cloze_test", "Cloze test generation")]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ConsumeCreditsAttribute(ulong cost, string reason, string description)
        : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var services = context.HttpContext.RequestServices;
            var userService = services.GetRequiredService<IUserService>();
            var creditService = services.GetRequiredService<ICreditService>();
            var responseFactory = services.GetRequiredService<IMyResponseFactory>();

            var userId = userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                context.Result = responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
                return;
            }

            if (!await creditService.ConsumeCreditsAsync(userId, cost, reason, description))
            {
                context.Result = responseFactory.CreateErrorResponse(
                    ErrorCodes.Forbidden,
                    $"Insufficient credits. Required: {cost}. Please purchase more credits to continue.");
                return;
            }

            await next();
        }
    }
}
