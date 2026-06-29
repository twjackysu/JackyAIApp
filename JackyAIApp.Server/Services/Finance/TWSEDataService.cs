using DotnetSdkUtilities.Services;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Service for TWSE data handling: fetches the raw daily material info with caching.
    /// </summary>
    public class TWSEDataService : ITWSEDataService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TWSEDataService> _logger;
        private readonly IExtendedMemoryCache _memoryCache;

        public TWSEDataService(
            IHttpClientFactory httpClientFactory,
            ILogger<TWSEDataService> logger,
            IExtendedMemoryCache memoryCache)
        {
            _httpClient = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        /// <summary>
        /// Fetches raw material information from Taiwan Stock Exchange API with caching.
        /// </summary>
        public async Task<string> GetRawMaterialInfoWithCacheAsync(CancellationToken cancellationToken = default)
        {
            var today = DateTime.Today.ToString("yyyyMMdd");
            var apiCacheKey = $"twse_raw_data_{today}";

            // Check cache first
            if (_memoryCache.TryGetValue(apiCacheKey, out string? cachedData) && !string.IsNullOrEmpty(cachedData))
            {
                return cachedData;
            }

            try
            {
                string apiUrl = "https://openapi.twse.com.tw/v1/opendata/t187ap04_L";

                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

                    // Daily-published filings; cache 12h to avoid refetching the same data.
                    _memoryCache.Set(apiCacheKey, jsonContent, TimeSpan.FromHours(12));

                    return jsonContent;
                }
                else
                {
                    _logger.LogError("Failed to fetch data from Taiwan Stock Exchange API. Status code: {StatusCode}", response.StatusCode);
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching data from Taiwan Stock Exchange API.");
                return string.Empty;
            }
        }
    }
}
