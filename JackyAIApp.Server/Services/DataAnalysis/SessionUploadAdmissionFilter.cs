using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Turns a session upload away before its body is read — a resource filter runs ahead of model
    /// binding — so a request that can't be kept never gets parsed into memory: 413 when Content-Length
    /// is over <see cref="SessionUploadOptions.MaxRequestBodyBytes"/>, 429 when the body is bigger than
    /// what is left of the session budget (an upload's in-memory size is at least about its JSON size),
    /// or when the machine is already short of memory. A body without Content-Length is still capped by
    /// the action's [RequestSizeLimit]. The body's length is not its size in memory (tiny cells cost far more
    /// once read), so the rows are measured while they are read too (<see cref="SessionRowsJsonConverter"/>, D-158)
    /// and then precisely by the store.
    /// Apply with <c>[ServiceFilter(typeof(SessionUploadAdmissionFilter))]</c>.
    /// </summary>
    public sealed class SessionUploadAdmissionFilter(
        SessionUploadBudget budget,
        IOptions<SessionUploadOptions> options,
        ILogger<SessionUploadAdmissionFilter> logger) : IAsyncResourceFilter
    {
        private readonly SessionUploadBudget _budget = budget;
        private readonly SessionUploadOptions _limits = options.Value;
        private readonly ILogger<SessionUploadAdmissionFilter> _logger = logger;

        /// <summary>
        /// The machine's memory load as the GC last measured it (0..1), or null before the first GC.
        /// Replaceable for tests.
        /// </summary>
        internal Func<double?> ReadMemoryLoad { get; init; } = () =>
        {
            var info = GC.GetGCMemoryInfo();
            return info.TotalAvailableMemoryBytes > 0
                ? (double)info.MemoryLoadBytes / info.TotalAvailableMemoryBytes
                : null;
        };

        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            var length = context.HttpContext.Request.ContentLength;
            if (length > SessionUploadOptions.MaxRequestBodyBytes)
            {
                Refuse(context, ErrorCodes.PayloadTooLarge,
                    $"資料太大（上限約 {SessionUploadOptions.MaxRequestBodyBytes >> 20} MB），請減少列數或欄數後再試。");
                return;
            }

            if (length > _budget.RemainingBytes)
            {
                _logger.LogWarning("Session upload refused: {length} bytes, {remaining} left of the session budget",
                    length, _budget.RemainingBytes);
                Refuse(context, ErrorCodes.TooManyRequests, SessionUploadOptions.BusyMessage);
                return;
            }

            if (ReadMemoryLoad() is { } load && load >= _limits.MaxMemoryLoad)
            {
                _logger.LogWarning("Session upload refused: memory load {load:P0} (limit {limit:P0})",
                    load, _limits.MaxMemoryLoad);
                Refuse(context, ErrorCodes.TooManyRequests, SessionUploadOptions.BusyMessage);
                return;
            }

            await next();
        }

        private static void Refuse(ResourceExecutingContext context, ErrorCodes code, string message) =>
            context.Result = context.HttpContext.RequestServices.GetRequiredService<IMyResponseFactory>()
                .CreateErrorResponse(code, message);
    }
}
