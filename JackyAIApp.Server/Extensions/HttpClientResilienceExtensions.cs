using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace JackyAIApp.Server.Extensions
{
    public static class HttpClientResilienceExtensions
    {
        /// <summary>
        /// Retry (2 attempts, exponential backoff starting at 1s) + circuit breaker for
        /// idempotent, read-only external calls (market data / job boards). Not meant for
        /// clients that make non-idempotent calls (payments, OpenAI generations), where
        /// retrying a request that already landed server-side risks a duplicate side effect.
        /// </summary>
        public static IHttpClientBuilder AddReadOnlyResilience(this IHttpClientBuilder builder)
        {
            builder.AddResilienceHandler("read-only-retry-circuit-breaker", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(1),
                    UseJitter = true
                });
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromSeconds(30)
                });
            });
            return builder;
        }
    }
}
