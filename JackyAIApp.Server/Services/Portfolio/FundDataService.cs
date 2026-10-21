using System.Globalization;
using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Portfolio;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Taiwan mutual-fund data via FundRich (基富通) public site backend.
    /// Reverse-engineered private JSON API — wrapped defensively so a remote
    /// change degrades to "no data" rather than throwing.
    /// </summary>
    public interface IFundDataService
    {
        /// <summary>Search funds by (Chinese) keyword. Returns up to ~10 matches.</summary>
        Task<List<FundSearchItem>> SearchFundsAsync(string keyword, CancellationToken ct = default);

        /// <summary>Daily NAV history for a fund id (e.g. "ALI006"), most-recent first day excluded weekends not filtered.</summary>
        Task<FundNavHistory?> GetFundNavHistoryAsync(string fundId, CancellationToken ct = default);
    }

    public record FundNavHistory(string Currency, List<PricePoint> Prices);

    public class FundDataService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<FundDataService> logger) : IFundDataService
    {
        private const string SEARCH_URL = "https://apis.fundrich.com.tw/DataCenter/FRSIndex/";
        private const string CHART_URL = "https://apis.fundrich.com.tw/FRSDataCenter/GetFundChart";

        // FundRich rejects requests without a same-origin Referer/Origin (HTTP 406).
        private static void AddBrowserHeaders(HttpRequestMessage req)
        {
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Origin", "https://www.fundrich.com.tw");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.fundrich.com.tw/");
        }

        public async Task<List<FundSearchItem>> SearchFundsAsync(string keyword, CancellationToken ct = default)
        {
            keyword = (keyword ?? "").Trim();
            if (keyword.Length < 1) return [];

            // A failed search answers null, which is not cached, so the next call tries again.
            return await cache.GetOrCreateSingleFlightAsync($"fund_search_{keyword}", CacheTtl.Portfolio.FundRich,
                () => SearchFundsUncachedAsync(keyword), ct) ?? [];
        }

        private async Task<List<FundSearchItem>?> SearchFundsUncachedAsync(string keyword)
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var ct = CancellationToken.None;
            try
            {
                var client = httpClientFactory.CreateClient("ExternalReadOnly");
                using var req = new HttpRequestMessage(HttpMethod.Post, SEARCH_URL);
                AddBrowserHeaders(req);
                var payload = JsonSerializer.Serialize(new { Method = "getSearchKw", Data = new { kw = keyword } });
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var resp = await client.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var items = new List<FundSearchItem>();
                if (root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("list", out var list) &&
                    list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in list.EnumerateArray())
                    {
                        var fundId = el.TryGetProperty("fundId", out var f) ? f.GetString() ?? "" : "";
                        var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var isSell = !el.TryGetProperty("isSell", out var s) || s.ValueKind != JsonValueKind.False;
                        if (string.IsNullOrWhiteSpace(fundId) || string.IsNullOrWhiteSpace(name)) continue;
                        items.Add(new FundSearchItem { FundId = fundId, Name = name, IsSell = isSell });
                        if (items.Count >= 12) break;
                    }
                }

                return items;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fund search failed for {Keyword}", keyword);
                return null;
            }
        }

        public async Task<FundNavHistory?> GetFundNavHistoryAsync(string fundId, CancellationToken ct = default)
        {
            fundId = (fundId ?? "").Trim();
            if (fundId.Length < 1) return null;

            // Single-flight: a cold cache can see this fundId requested by several Portfolio
            // endpoints at once (same page load). The shared fetch runs with its own
            // CancellationToken.None (see SingleFlight's doc) so one caller cancelling never cancels
            // it for the others still waiting. No history (failed, or none returned) is not cached.
            return await cache.GetOrCreateSingleFlightAsync($"fund_nav_{fundId}", CacheTtl.Portfolio.FundRich, async () =>
            {
                try
                {
                    var client = httpClientFactory.CreateClient("ExternalReadOnly");
                    using var req = new HttpRequestMessage(HttpMethod.Post, CHART_URL);
                    AddBrowserHeaders(req);
                    // "1Y" gives ~243 daily points; enough for the 180-day net-worth window.
                    var payload = JsonSerializer.Serialize(new { data = new { fundId, period = "1Y" } });
                    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    using var resp = await client.SendAsync(req, CancellationToken.None);
                    resp.EnsureSuccessStatusCode();
                    var json = await resp.Content.ReadAsStringAsync(CancellationToken.None);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("data", out var data) ||
                        !data.TryGetProperty("tableRow", out var tableRow))
                        return null;

                    var currency = tableRow.TryGetProperty("currency", out var c) ? c.GetString() ?? "TWD" : "TWD";
                    var prices = new List<PricePoint>();
                    if (tableRow.TryGetProperty("priceHistory", out var ph) && ph.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in ph.EnumerateArray())
                        {
                            var dateStr = el.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                            var priceStr = el.TryGetProperty("price", out var p) ? p.GetString() ?? "" : "";
                            if (!DateTime.TryParseExact(dateStr, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var dt))
                                continue;
                            if (!decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price <= 0)
                                continue;
                            prices.Add(new PricePoint
                            {
                                Date = dt.ToString("yyyy-MM-dd"),
                                Close = price,
                                High = price,
                                Low = price,
                            });
                        }
                    }

                    if (prices.Count == 0) return null;
                    // API returns newest-first; normalize to ascending by date.
                    prices.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));

                    return new FundNavHistory(currency, prices);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Fund NAV history failed for {FundId}", fundId);
                    return null;
                }
            }, ct);
        }
    }
}
