using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Action filter that consumes the current user's daily usage budget before the action runs.
    /// Short-circuits with a 429 + <see cref="ErrorCodes.UsageLimitExceeded"/> response when the
    /// user is unauthenticated or has exceeded today's limit. Replaced the old per-feature credit
    /// charge for the usage-limit mechanism (DECISION-006/007).
    /// Apply to AI-powered endpoints, e.g. <c>[UsageLimit(UsageCosts.ClozeTest, "cloze_test")]</c>.
    ///
    /// This is how an endpoint is charged. Charging up front is safe even when the action may still
    /// reject the request: a request that fails without any AI provider billing is refunded at the
    /// end of the request (<see cref="Services.UsageLedger"/>, settled by
    /// <see cref="UsageSettlementMiddleware"/>). An endpoint charges inline only when the charge
    /// depends on something the action finds out first — a cache hit that is free, a cost that
    /// depends on the request (chat attachments), one charge per user per day.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class UsageLimitAttribute(ulong cost, string feature)
        : Attribute, IAsyncActionFilter
    {
        public ulong Cost => cost;

        public string Feature => feature;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var services = context.HttpContext.RequestServices;
            var userService = services.GetRequiredService<IUserService>();
            var usageLimitService = services.GetRequiredService<IUsageLimitService>();
            var responseFactory = services.GetRequiredService<IMyResponseFactory>();

            var userId = userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                context.Result = responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
                return;
            }

            if (!await usageLimitService.TryConsumeAsync(userId, cost, feature))
            {
                context.Result = responseFactory.CreateErrorResponse(
                    ErrorCodes.UsageLimitExceeded,
                    "Daily usage limit exceeded.");
                return;
            }

            await next();
        }
    }
}
