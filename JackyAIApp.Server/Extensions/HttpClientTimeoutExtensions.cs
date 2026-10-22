using Betalgo.Ranul.OpenAI.Extensions;

namespace JackyAIApp.Server.Extensions
{
    public static class HttpClientTimeoutExtensions
    {
        /// <summary>The app's default, for the external APIs it reads (see <see cref="AddHttpClientDefaultTimeout"/>).</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

        /// <summary>OpenAI's own (see <see cref="AddOpenAIClient"/>).</summary>
        public static readonly TimeSpan OpenAITimeout = TimeSpan.FromSeconds(90);

        /// <summary>
        /// Default timeout for every HttpClient the app creates (named, typed, or the unnamed default from
        /// CreateClient()) — previously unbounded (~100s default), so one slow external API
        /// (TWSE/Yahoo/FundRich/SEC/FRED/...) could tie up a request thread far longer than a user will
        /// ever wait for a response. Individual AddHttpClient(...) registrations can still override it.
        /// </summary>
        public static IServiceCollection AddHttpClientDefaultTimeout(this IServiceCollection services) =>
            services.ConfigureHttpClientDefaults(http =>
                http.ConfigureHttpClient(client => client.Timeout = DefaultTimeout));

        /// <summary>
        /// OpenAI (Betalgo's typed client, named "IOpenAIService"), with a timeout of its own rather than
        /// the default for external APIs: gpt-6-luna reasons before it answers, and a word definition or
        /// an exam takes 5-20s, sometimes more. At 20s, 1.6% of calls were cut off (2026-10) — answers
        /// OpenAI went on to bill for, thrown away. 90s stays under Cloudflare's 100s, past which a
        /// request someone waits on fails anyway. No retries: a completion that reached OpenAI would be
        /// generated, and paid for, twice.
        /// </summary>
        public static IHttpClientBuilder AddOpenAIClient(this IServiceCollection services, string apiKey) =>
            services.AddOpenAIService(options =>
            {
                options.ApiKey = apiKey;
                options.UseBeta = true;
            }).ConfigureHttpClient(client => client.Timeout = OpenAITimeout);
    }
}
