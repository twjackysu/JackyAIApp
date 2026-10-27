namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Yahoo Finance's keyless chart API (<c>query1.finance.yahoo.com/v8/finance/chart</c>). One
    /// place for the base URL and the browser User-Agent it requires — without a User-Agent Yahoo
    /// answers non-browser clients with an error — so the finance providers and the dividend service
    /// don't each repeat them. Each caller still parses the response for the fields it needs.
    /// Send the request on an <c>"ExternalReadOnly"</c> client (Polly retry + circuit breaker).
    /// </summary>
    public static class YahooChart
    {
        public const string ChartBase = "https://query1.finance.yahoo.com/v8/finance/chart";

        /// <summary>The chart URL for <paramref name="symbol"/> with <paramref name="query"/> (no leading <c>?</c>).
        /// The symbol is escaped: it can come from what a reader typed (a holding, a stock code), and a <c>/</c>,
        /// <c>?</c> or <c>#</c> in it would otherwise reach another path or query on Yahoo's host.</summary>
        public static string SymbolUrl(string symbol, string query) =>
            $"{ChartBase}/{Uri.EscapeDataString(symbol)}?{query}";

        /// <summary>A GET for <paramref name="url"/> carrying the User-Agent Yahoo requires.</summary>
        public static HttpRequestMessage Request(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            return request;
        }
    }
}
