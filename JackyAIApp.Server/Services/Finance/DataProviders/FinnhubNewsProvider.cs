using JackyAIApp.Server.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    public interface IFinnhubNewsProvider
    {
        bool IsConfigured { get; }
        Task<IReadOnlyList<FinnhubNewsItem>> FetchAsync(CancellationToken cancellationToken = default);
    }

    public record FinnhubNewsItem(string Headline, string Summary, string Source, long Datetime);

    public class FinnhubNewsProvider : IFinnhubNewsProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<Settings> _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<FinnhubNewsProvider> _logger;

        private const string BASE_URL = "https://finnhub.io/api/v1/news";
        private const int MAX_ITEMS = 15;
        private const int LOOKBACK_HOURS = 24;

        public FinnhubNewsProvider(
            IHttpClientFactory httpClientFactory,
            IOptionsMonitor<Settings> settings,
            IMemoryCache cache,
            ILogger<FinnhubNewsProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings;
            _cache = cache;
            _logger = logger;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.CurrentValue.FinnhubApiKey);

        public async Task<IReadOnlyList<FinnhubNewsItem>> FetchAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "FinnhubNews";
            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<FinnhubNewsItem>? cached) && cached != null)
                return cached;

            var apiKey = _settings.CurrentValue.FinnhubApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                return [];

            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{BASE_URL}?category=general&token={apiKey}";
                using var response = await client.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Finnhub news returned {status}: {body}", response.StatusCode, body);
                    return [];
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return [];

                var cutoff = DateTimeOffset.UtcNow.AddHours(-LOOKBACK_HOURS).ToUnixTimeSeconds();
                var items = new List<FinnhubNewsItem>();

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (items.Count >= MAX_ITEMS) break;

                    var dt = el.TryGetProperty("datetime", out var dtProp) ? dtProp.GetInt64() : 0;
                    if (dt < cutoff) continue;

                    var headline = el.TryGetProperty("headline", out var h) ? h.GetString() ?? "" : "";
                    var summary = el.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                    var source = el.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(headline)) continue;

                    // Trim summary — keep first 120 chars to control prompt size.
                    if (summary.Length > 120) summary = summary[..120] + "…";

                    items.Add(new FinnhubNewsItem(headline, summary, source, dt));
                }

                _cache.Set(cacheKey, (IReadOnlyList<FinnhubNewsItem>)items, TimeSpan.FromMinutes(30));
                return items;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch Finnhub news");
                return [];
            }
        }
    }
}
