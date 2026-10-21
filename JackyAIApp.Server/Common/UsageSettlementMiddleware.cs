using JackyAIApp.Server.Services;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Settles the request's usage charges once the response is done: refunds them when the
    /// <see cref="UsageLedger"/> says so (the request failed and no AI provider billed). Runs outside
    /// <see cref="ExceptionMiddleware"/>, so an unhandled exception arrives here as the 500 it was
    /// turned into. A request that took no charge costs one empty ledger and nothing else.
    /// </summary>
    public class UsageSettlementMiddleware(RequestDelegate next, ILogger<UsageSettlementMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context, UsageLedger ledger)
        {
            var threw = true;
            try
            {
                await next(context);
                threw = false;
            }
            finally
            {
                await SettleAsync(context, ledger, failed: threw || context.Response.StatusCode >= 400);
            }
        }

        private async Task SettleAsync(HttpContext context, UsageLedger ledger, bool failed)
        {
            var refunds = ledger.TakeRefunds(failed);
            if (refunds.Count == 0) return;

            var usageLimits = context.RequestServices.GetRequiredService<IUsageLimitService>();
            foreach (var charge in refunds)
            {
                try
                {
                    await usageLimits.RefundAsync(charge.UserId, charge.Feature, charge.Cost);
                }
                catch (Exception ex)
                {
                    // The response is already decided; a lost refund only leaves the charge in place.
                    logger.LogWarning(ex, "Usage refund of {Cost} for {Feature} failed", charge.Cost, charge.Feature);
                }
            }
        }
    }
}
