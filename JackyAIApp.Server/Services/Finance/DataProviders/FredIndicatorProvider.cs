using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Fetches key macro indicators from FRED (Federal Reserve Economic Data) — free, official,
    /// with deep history. Requires a free API key (Settings:FredApiKey).
    /// </summary>
    public class FredIndicatorProvider : IIndicatorProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<Settings> _settings;
        private readonly ILogger<FredIndicatorProvider> _logger;

        private const string OBSERVATIONS_URL = "https://api.stlouisfed.org/fred/series/observations";
        private const int LOOKBACK_YEARS = 2;
        private const int MAX_POINTS = 180; // downsample target for the chart

        // (Id, FRED series id, 中文名, 是什麼, 單位)
        private static readonly (string Id, string Series, string Name, string Description, string Unit)[] Indicators =
        {
            ("us10y",  "DGS10",     "美債10年期殖利率", "美國10年期公債殖利率，公認的『無風險利率』基準，牽動全球資產定價。", "%"),
            ("vix",    "VIXCLS",    "VIX 恐慌指數",     "S&P 500 選擇權的隱含波動率，市場恐慌與避險情緒的溫度計。", ""),
            ("m2",     "M2SL",      "M2 貨幣供給",      "市場上流通的廣義貨幣總量，反映整體流動性的鬆緊。", "十億美元"),
            ("dollar", "DTWEXBGS",  "美元指數（廣義）", "美元對主要貿易夥伴貨幣的加權匯率（Fed 廣義美元指數，DXY 的替代指標）。", ""),
            ("fedbs",  "WALCL",     "Fed 資產負債表",   "聯準會總資產規模，QE（擴表）與 QT（縮表）的直接指標。", "百萬美元"),
            ("rrp",    "RRPONTSYD", "隔夜逆回購 (RRP)", "金融機構停泊在 Fed 的隔夜資金，反映系統中的過剩流動性。", "十億美元"),
        };

        public FredIndicatorProvider(
            IHttpClientFactory httpClientFactory,
            IOptionsMonitor<Settings> settings,
            ILogger<FredIndicatorProvider> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.CurrentValue.FredApiKey?.Trim());

        public async Task<IndicatorExplorerResponse> FetchAsync(CancellationToken cancellationToken = default)
        {
            var apiKey = _settings.CurrentValue.FredApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("FRED API key is not configured (Settings:FredApiKey).");

            var tasks = Indicators.Select(i => FetchSeriesSafeAsync(i, apiKey, cancellationToken));
            var series = await Task.WhenAll(tasks);

            return new IndicatorExplorerResponse
            {
                Indicators = series.Where(s => s != null).Select(s => s!).ToList(),
                GeneratedAt = DateTime.UtcNow,
            };
        }

        private async Task<IndicatorSeries?> FetchSeriesSafeAsync(
            (string Id, string Series, string Name, string Description, string Unit) def,
            string apiKey,
            CancellationToken ct)
        {
            try
            {
                var start = DateTime.UtcNow.AddYears(-LOOKBACK_YEARS).ToString("yyyy-MM-dd");
                var url = $"{OBSERVATIONS_URL}?series_id={def.Series}&api_key={apiKey}&file_type=json" +
                          $"&observation_start={start}&sort_order=asc";

                var client = _httpClientFactory.CreateClient();
                using var response = await client.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // FRED returns a helpful body on failure (e.g. "api_key is not registered" /
                    // "Bad Request"). Log it so live config issues are diagnosable.
                    var body = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("FRED series {series} returned {status}: {body}",
                        def.Series, response.StatusCode, body);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("observations", out var obs) || obs.ValueKind != JsonValueKind.Array)
                    return null;

                var points = new List<IndicatorPoint>();
                foreach (var o in obs.EnumerateArray())
                {
                    var valStr = o.GetProperty("value").GetString();
                    if (string.IsNullOrEmpty(valStr) || valStr == ".") continue; // FRED uses "." for missing
                    if (!decimal.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)) continue;
                    points.Add(new IndicatorPoint { Date = o.GetProperty("date").GetString() ?? "", Value = val });
                }

                if (points.Count == 0) return null;

                var latest = points[^1];
                var previous = points.Count > 1 ? points[^2] : null;

                return new IndicatorSeries
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    Unit = def.Unit,
                    LatestValue = latest.Value,
                    LatestDate = latest.Date,
                    PreviousValue = previous?.Value,
                    Points = Downsample(points, MAX_POINTS),
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch FRED series {series}", def.Series);
                return null;
            }
        }

        /// <summary>Evenly thins a series to at most <paramref name="max"/> points, always keeping the last.</summary>
        private static List<IndicatorPoint> Downsample(List<IndicatorPoint> points, int max)
        {
            if (points.Count <= max) return points;

            var step = (int)Math.Ceiling(points.Count / (double)max);
            var result = new List<IndicatorPoint>();
            for (int i = 0; i < points.Count; i += step)
                result.Add(points[i]);

            if (result[^1].Date != points[^1].Date)
                result.Add(points[^1]);

            return result;
        }
    }
}
