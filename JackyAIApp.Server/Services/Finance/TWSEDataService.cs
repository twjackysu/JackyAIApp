using JackyAIApp.Server.Common;
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
        private readonly IMemoryCache _memoryCache;

        public TWSEDataService(
            IHttpClientFactory httpClientFactory,
            ILogger<TWSEDataService> logger,
            IMemoryCache memoryCache)
        {
            _httpClient = httpClientFactory?.CreateClient("ExternalReadOnly") ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        /// <summary>
        /// Fetches raw material information from Taiwan Stock Exchange API with caching.
        /// </summary>
        public async Task<string> GetRawMaterialInfoWithCacheAsync(CancellationToken cancellationToken = default)
        {
            var today = DateTime.Today.ToString("yyyyMMdd");
            // Daily-published filings; cache 12h to avoid refetching the same data. Empty (the fetch
            // failed) is returned but not cached, so the next call tries again.
            return await _memoryCache.GetOrCreateSingleFlightAsync($"twse_raw_data_{today}", TimeSpan.FromHours(12),
                FetchRawMaterialInfoAsync, cancellationToken, shouldCache: content => content.Length > 0);
        }

        private async Task<string> FetchRawMaterialInfoAsync()
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var cancellationToken = CancellationToken.None;
            try
            {
                string apiUrl = "https://openapi.twse.com.tw/v1/opendata/t187ap04_L";

                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(cancellationToken);
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
