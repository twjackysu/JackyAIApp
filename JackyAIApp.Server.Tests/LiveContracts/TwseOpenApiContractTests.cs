using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for TWSE OpenAPI (openapi.twse.com.tw) — an official but Chinese-field-
    /// named endpoint set consumed directly by TWSEFundamentalDataProvider (B1), TWSEMacroEconomyProvider
    /// (B2, TWSE-only methods), TWSEChipDataProvider (B3), and StockCodeResolver (B4).
    /// THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B1–B4. Bank of Taiwan / CBC CSV endpoints (B5/B6)
    /// are out of scope here — see TWSEMacroEconomyProvider's other methods, covered in Phase 3.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class TwseOpenApiContractTests : LiveContractBase
    {
        private const string BaseUrl = "https://openapi.twse.com.tw/v1";

        // Fixed company used for endpoints where a random pool pick is unreliable (see class doc
        // on MI_QFIIS_sort_20/t187ap11_L/t187ap02_L below) — 2330 (TSMC) is TWSE's largest,
        // longest-listed constituent and consistently has director-holding disclosures.
        private const string KnownStock = "2330";

        /// <summary>Null when the endpoint timed out — callers must early-return on that (see
        /// <see cref="LiveContractBase.TryFetchStringAsync"/> for why a timeout is inconclusive
        /// rather than a contract break). Several of these paths are whole-market dumps in the
        /// 1–10MB range, which is what makes timeouts a realistic outcome here at all.</summary>
        private static async Task<JsonElement?> FetchArrayAsync(string path)
        {
            var json = await TryFetchStringAsync($"{BaseUrl}/{path}", $"TWSE {path}");
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();
            Assert.True(root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0,
                $"TWSE {path} did not return a non-empty JSON array — shape changed. Body (truncated): {json[..Math.Min(json.Length, 300)]}");
            return root;
        }

        public static IEnumerable<object[]> TodaysTwStockPicks() =>
            StockPoolProvider.PickForToday(StockPoolProvider.TwStocks, count: 3, seedOffset: 3)
                .Select(s => new object[] { s });

        /// <summary>Companies only (no ETFs) — for endpoints scoped to per-company fundamentals,
        /// see <see cref="StockPoolProvider.TwCompanies"/>.</summary>
        public static IEnumerable<object[]> TodaysTwCompanyPicks() =>
            StockPoolProvider.PickForToday(StockPoolProvider.TwCompanies, count: 3, seedOffset: 4)
                .Select(s => new object[] { s });

        // === B1: TWSEFundamentalDataProvider ===

        [Theory]
        [MemberData(nameof(TodaysTwCompanyPicks))]
        public async Task BWIBBU_d_ForTodaysRandomPicks_HasValuationFields(string symbol)
        {
            if (await FetchArrayAsync("exchangeReport/BWIBBU_d") is not { } data) return;
            var stock = data.EnumerateArray().FirstOrDefault(r => r.TryGetProperty("Code", out var c) && c.GetString() == symbol);
            Assert.True(stock.ValueKind != JsonValueKind.Undefined,
                $"[BWIBBU_d/{symbol}] no row with Code=='{symbol}' — TWSEFundamentalDataProvider.FetchValuationData filters on this field.");

            Assert.True(stock.TryGetProperty("PEratio", out _), $"[BWIBBU_d/{symbol}] 'PEratio' field missing.");
            Assert.True(stock.TryGetProperty("PBratio", out _), $"[BWIBBU_d/{symbol}] 'PBratio' field missing.");
            Assert.True(stock.TryGetProperty("DividendYield", out _), $"[BWIBBU_d/{symbol}] 'DividendYield' field missing.");
            Assert.True(stock.TryGetProperty("FiscalYearQuarter", out var fq) && !string.IsNullOrEmpty(fq.GetString()),
                $"[BWIBBU_d/{symbol}] 'FiscalYearQuarter' missing/empty.");
        }

        [Theory]
        [MemberData(nameof(TodaysTwCompanyPicks))]
        public async Task Revenue_t187ap05_L_ForTodaysRandomPicks_HasUsableMonthlyRevenue(string symbol)
        {
            if (await FetchArrayAsync("opendata/t187ap05_L") is not { } data) return;
            var stock = data.EnumerateArray().FirstOrDefault(r =>
                r.TryGetProperty("公司代號", out var c) && c.GetString()?.Trim() == symbol);

            // TWSE publishes this dataset in batches around the monthly filing deadline, so a given
            // company can legitimately have no row for a few days. That is data coverage, not the
            // contract — TWSEFundamentalDataProvider.FetchRevenueData returns null for it and the
            // analysis just omits monthly revenue. Fall back to asserting the shape of the whole
            // dataset so a renamed field still fails the test.
            if (stock.ValueKind == JsonValueKind.Undefined)
            {
                var rowCount = data.GetArrayLength();
                Assert.True(rowCount >= 100,
                    $"[t187ap05_L/{symbol}] no row for '{symbol}' and the dataset has only {rowCount} rows (expected >= 100) — dataset shape changed.");
                Assert.True(data.EnumerateArray().Any(HasUsableMonthlyRevenueRow),
                    $"[t187ap05_L/{symbol}] no row for '{symbol}' and none of the {rowCount} rows has the expected fields " +
                    "(公司代號, 營業收入-當月營收 > 0, 資料年月, 營業收入-去年同月增減(%), 營業收入-上月比較增減(%)) — dataset shape changed.");
                return;
            }

            Assert.True(stock.TryGetProperty("營業收入-當月營收", out var rev) &&
                decimal.TryParse(rev.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var revVal) && revVal > 0,
                $"[t187ap05_L/{symbol}] '營業收入-當月營收' missing or not a positive number.");
            Assert.True(stock.TryGetProperty("資料年月", out var month) && !string.IsNullOrEmpty(month.GetString()),
                $"[t187ap05_L/{symbol}] '資料年月' missing/empty.");
            Assert.True(stock.TryGetProperty("營業收入-去年同月增減(%)", out _),
                $"[t187ap05_L/{symbol}] '營業收入-去年同月增減(%)' field missing.");
            Assert.True(stock.TryGetProperty("營業收入-上月比較增減(%)", out _),
                $"[t187ap05_L/{symbol}] '營業收入-上月比較增減(%)' field missing.");
        }

        private static bool HasUsableMonthlyRevenueRow(JsonElement row) =>
            row.ValueKind == JsonValueKind.Object &&
            row.TryGetProperty("公司代號", out _) &&
            row.TryGetProperty("營業收入-當月營收", out var rev) && rev.ValueKind == JsonValueKind.String &&
            decimal.TryParse(rev.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var revVal) && revVal > 0 &&
            row.TryGetProperty("資料年月", out var month) && month.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(month.GetString()) &&
            row.TryGetProperty("營業收入-去年同月增減(%)", out _) &&
            row.TryGetProperty("營業收入-上月比較增減(%)", out _);

        [Theory]
        [MemberData(nameof(TodaysTwCompanyPicks))]
        public async Task Eps_t187ap14_L_ForTodaysRandomPicks_HasUsableEpsFields(string symbol)
        {
            if (await FetchArrayAsync("opendata/t187ap14_L") is not { } data) return;
            var stock = data.EnumerateArray().FirstOrDefault(r =>
                r.TryGetProperty("公司代號", out var c) && c.GetString()?.Trim() == symbol);
            Assert.True(stock.ValueKind != JsonValueKind.Undefined,
                $"[t187ap14_L/{symbol}] no row with 公司代號=='{symbol}' — TWSEFundamentalDataProvider.FetchEPSData filters on this field.");

            Assert.True(stock.TryGetProperty("基本每股盈餘(元)", out var eps) &&
                decimal.TryParse(eps.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _),
                $"[t187ap14_L/{symbol}] '基本每股盈餘(元)' missing or not numeric.");
            Assert.True(stock.TryGetProperty("年度", out var year) && !string.IsNullOrEmpty(year.GetString()),
                $"[t187ap14_L/{symbol}] '年度' missing/empty.");
            Assert.True(stock.TryGetProperty("季別", out var quarter) && !string.IsNullOrEmpty(quarter.GetString()),
                $"[t187ap14_L/{symbol}] '季別' missing/empty.");
            Assert.True(stock.TryGetProperty("營業利益", out _), $"[t187ap14_L/{symbol}] '營業利益' field missing.");
            Assert.True(stock.TryGetProperty("稅後淨利", out _), $"[t187ap14_L/{symbol}] '稅後淨利' field missing.");
        }

        [Theory]
        [MemberData(nameof(TodaysTwStockPicks))]
        public async Task StockDayAll_ForTodaysRandomPicks_HasPositiveClosingPrice(string symbol)
        {
            if (await FetchArrayAsync("exchangeReport/STOCK_DAY_ALL") is not { } data) return;
            var stock = data.EnumerateArray().FirstOrDefault(r => r.TryGetProperty("Code", out var c) && c.GetString() == symbol);
            Assert.True(stock.ValueKind != JsonValueKind.Undefined,
                $"[STOCK_DAY_ALL/{symbol}] no row with Code=='{symbol}' — TWSEFundamentalDataProvider.FetchLatestPrice filters on this field.");

            Assert.True(stock.TryGetProperty("ClosingPrice", out var close) &&
                decimal.TryParse(close.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var closeVal) && closeVal > 0,
                $"[STOCK_DAY_ALL/{symbol}] 'ClosingPrice' missing or not a positive number.");
        }

        // === B2: TWSEMacroEconomyProvider (TWSE-only methods; FX/bank-rate CSV are B5/B6, out of scope) ===

        [Fact]
        public async Task FMTQIK_HasUsableMarketIndexRows()
        {
            if (await FetchArrayAsync("exchangeReport/FMTQIK") is not { } data) return;
            var first = data[0];

            Assert.True(first.TryGetProperty("Date", out var date) && !string.IsNullOrEmpty(date.GetString()),
                "[FMTQIK] 'Date' missing/empty on first row.");
            // MarketOverviewProvider picks the latest day by comparing these strings, so they must
            // stay fixed-width 民國 YYYMMDD.
            Assert.True(date.GetString() is { Length: 7 } d && d.All(char.IsAsciiDigit),
                $"[FMTQIK] 'Date' '{date.GetString()}' is not 民國 YYYMMDD.");
            Assert.True(first.TryGetProperty("TAIEX", out var taiex) &&
                decimal.TryParse(taiex.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var taiexVal) && taiexVal > 0,
                "[FMTQIK] 'TAIEX' missing or not a positive number — TWSEMacroEconomyProvider.FetchMarketIndex reads this exact field.");
            Assert.True(first.TryGetProperty("Change", out _), "[FMTQIK] 'Change' field missing.");
            Assert.True(first.TryGetProperty("TradeVolume", out _), "[FMTQIK] 'TradeVolume' field missing.");
            Assert.True(first.TryGetProperty("TradeValue", out _), "[FMTQIK] 'TradeValue' field missing.");
            Assert.True(first.TryGetProperty("Transaction", out _), "[FMTQIK] 'Transaction' field missing.");
        }

        [Fact]
        public async Task MI_INDEX_HasWeightedIndexAmongKeySectors()
        {
            if (await FetchArrayAsync("exchangeReport/MI_INDEX") is not { } data) return;
            var weighted = data.EnumerateArray().FirstOrDefault(r =>
                r.TryGetProperty("指數", out var name) && name.GetString() == "發行量加權股價指數");
            Assert.True(weighted.ValueKind != JsonValueKind.Undefined,
                "[MI_INDEX] no row with 指數=='發行量加權股價指數' — TWSEMacroEconomyProvider.KeySectors depends on this exact name surviving.");

            Assert.True(weighted.TryGetProperty("收盤指數", out var close) &&
                decimal.TryParse(close.GetString()?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var closeVal) && closeVal > 0,
                "[MI_INDEX] '收盤指數' missing or not a positive number for 發行量加權股價指數.");
            Assert.True(weighted.TryGetProperty("漲跌", out _), "[MI_INDEX] '漲跌' field missing.");
            Assert.True(weighted.TryGetProperty("漲跌點數", out _), "[MI_INDEX] '漲跌點數' field missing.");
            Assert.True(weighted.TryGetProperty("漲跌百分比", out _), "[MI_INDEX] '漲跌百分比' field missing.");
        }

        [Fact]
        public async Task MI_MARGN_HasUsableMarginFieldsAcrossRows()
        {
            if (await FetchArrayAsync("exchangeReport/MI_MARGN") is not { } data) return;
            var first = data[0];

            Assert.True(first.TryGetProperty("融資買進", out _), "[MI_MARGN] '融資買進' field missing.");
            Assert.True(first.TryGetProperty("融資賣出", out _), "[MI_MARGN] '融資賣出' field missing.");
            Assert.True(first.TryGetProperty("融資今日餘額", out _), "[MI_MARGN] '融資今日餘額' field missing.");
            Assert.True(first.TryGetProperty("融券賣出", out _), "[MI_MARGN] '融券賣出' field missing.");
            Assert.True(first.TryGetProperty("融券買進", out _), "[MI_MARGN] '融券買進' field missing.");
            Assert.True(first.TryGetProperty("融券今日餘額", out _), "[MI_MARGN] '融券今日餘額' field missing.");
            // MarginSummary.MarginChange/ShortChange are today's balance minus these.
            Assert.True(first.TryGetProperty("融資前日餘額", out _), "[MI_MARGN] '融資前日餘額' field missing.");
            Assert.True(first.TryGetProperty("融券前日餘額", out _), "[MI_MARGN] '融券前日餘額' field missing.");
        }

        // === B3: TWSEChipDataProvider ===

        [Theory]
        [MemberData(nameof(TodaysTwCompanyPicks))]
        public async Task ChipMI_MARGN_ForTodaysRandomPicks_HasPerStockMarginFields(string symbol)
        {
            if (await FetchArrayAsync("exchangeReport/MI_MARGN") is not { } data) return;
            var stock = data.EnumerateArray().FirstOrDefault(r =>
                r.TryGetProperty("股票代號", out var c) && c.GetString()?.Trim() == symbol);
            Assert.True(stock.ValueKind != JsonValueKind.Undefined,
                $"[MI_MARGN(chip)/{symbol}] no row with 股票代號=='{symbol}' — TWSEChipDataProvider.FetchMarginDataAsync filters on this field.");

            Assert.True(stock.TryGetProperty("融資今日餘額", out _), $"[MI_MARGN(chip)/{symbol}] '融資今日餘額' field missing.");
            Assert.True(stock.TryGetProperty("融券今日餘額", out _), $"[MI_MARGN(chip)/{symbol}] '融券今日餘額' field missing.");
            Assert.True(stock.TryGetProperty("資券互抵", out _), $"[MI_MARGN(chip)/{symbol}] '資券互抵' field missing.");
        }

        [Fact]
        public async Task MI_QFIIS_sort_20_HasUsableOverallShape()
        {
            // Only the top-20 stocks by foreign holding appear here, so a randomly-picked stock is
            // very unlikely to be present — assert on the endpoint's overall shape instead
            // (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section 4.2/task instructions).
            if (await FetchArrayAsync("fund/MI_QFIIS_sort_20") is not { } data) return;
            var first = data[0];

            Assert.True(first.TryGetProperty("Code", out var code) && !string.IsNullOrEmpty(code.GetString()),
                "[MI_QFIIS_sort_20] 'Code' missing/empty on first row.");
            Assert.True(first.TryGetProperty("SharesHeld", out _), "[MI_QFIIS_sort_20] 'SharesHeld' field missing.");
            Assert.True(first.TryGetProperty("AvailableShare", out _), "[MI_QFIIS_sort_20] 'AvailableShare' field missing.");
            Assert.True(first.TryGetProperty("SharesHeldPer", out var pct) &&
                decimal.TryParse(pct.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var pctVal) && pctVal is >= 0 and <= 100,
                "[MI_QFIIS_sort_20] 'SharesHeldPer' missing or outside the 0–100 sane range.");
            Assert.True(first.TryGetProperty("Upperlimit", out _), "[MI_QFIIS_sort_20] 'Upperlimit' field missing.");
        }

        [Fact]
        public async Task SBL_TWT96U_ForKnownStock_HasAvailableVolume()
        {
            if (await FetchArrayAsync("SBL/TWT96U") is not { } data) return;
            var stock = data.EnumerateArray().FirstOrDefault(r =>
                r.TryGetProperty("TWSECode", out var c) && c.GetString()?.Trim() == KnownStock);
            Assert.True(stock.ValueKind != JsonValueKind.Undefined,
                $"[SBL/TWT96U] no row with TWSECode=='{KnownStock}' — TWSEChipDataProvider.FetchSBLDataAsync filters on this field.");
            Assert.True(stock.TryGetProperty("TWSEAvailableVolume", out _),
                "[SBL/TWT96U] 'TWSEAvailableVolume' field missing.");
        }

        [Fact]
        public async Task DirectorHoldings_t187ap11_L_ForKnownStock_HasUsableFields()
        {
            // Not every stock has recent director-holding disclosures; 2330 (TSMC) reliably does,
            // so this uses a fixed known-good company rather than a random pool pick.
            if (await FetchArrayAsync("opendata/t187ap11_L") is not { } data) return;
            var rows = data.EnumerateArray().Where(r =>
                r.TryGetProperty("公司代號", out var c) && c.GetString()?.Trim() == KnownStock).ToList();
            Assert.True(rows.Count > 0,
                $"[t187ap11_L] no rows with 公司代號=='{KnownStock}' — TWSEChipDataProvider.FetchDirectorHoldingsAsync filters on this field.");

            var first = rows[0];
            Assert.True(first.TryGetProperty("職稱", out _), "[t187ap11_L] '職稱' field missing.");
            Assert.True(first.TryGetProperty("姓名", out _), "[t187ap11_L] '姓名' field missing.");
            Assert.True(first.TryGetProperty("目前持股", out _), "[t187ap11_L] '目前持股' field missing.");
            Assert.True(first.TryGetProperty("設質股數", out _), "[t187ap11_L] '設質股數' field missing.");
            Assert.True(first.TryGetProperty("設質股數佔持股比例", out _), "[t187ap11_L] '設質股數佔持股比例' field missing.");
        }

        [Fact]
        public async Task MajorShareholders_t187ap02_L_HasUsableOverallShape()
        {
            // Not every stock has a >10% single shareholder to report, so this asserts on the
            // endpoint's overall shape rather than requiring a specific company to appear.
            if (await FetchArrayAsync("opendata/t187ap02_L") is not { } data) return;
            var first = data[0];

            Assert.True(first.TryGetProperty("公司代號", out var code) && !string.IsNullOrEmpty(code.GetString()),
                "[t187ap02_L] '公司代號' missing/empty on first row.");
            Assert.True(first.TryGetProperty("大股東名稱", out var name) && !string.IsNullOrEmpty(name.GetString()),
                "[t187ap02_L] '大股東名稱' missing/empty on first row — TWSEChipDataProvider.FetchMajorShareholdersAsync reads this exact field.");
        }

        [Theory]
        [MemberData(nameof(TodaysTwStockPicks))]
        public async Task DayTradingSuspension_TWTB4U_ForTodaysRandomPicks_HasSuspensionField(string symbol)
        {
            if (await FetchArrayAsync("exchangeReport/TWTB4U") is not { } data) return;
            var stock = data.EnumerateArray().FirstOrDefault(r => r.TryGetProperty("Code", out var c) && c.GetString() == symbol);
            Assert.True(stock.ValueKind != JsonValueKind.Undefined,
                $"[TWTB4U/{symbol}] no row with Code=='{symbol}' — TWSEChipDataProvider.FetchDayTradingAsync filters on this field.");
            Assert.True(stock.TryGetProperty("Suspension", out _), $"[TWTB4U/{symbol}] 'Suspension' field missing.");
        }

        // === B4: company list, consumed by StockCodeResolver ===

        [Fact]
        public async Task CompanyList_t187ap03_L_HasUsableCodeAndNameFields()
        {
            if (await FetchArrayAsync("opendata/t187ap03_L") is not { } data) return;
            var stock = data.EnumerateArray().FirstOrDefault(r =>
                r.TryGetProperty("公司代號", out var c) && c.GetString()?.Trim() == KnownStock);
            Assert.True(stock.ValueKind != JsonValueKind.Undefined,
                $"[t187ap03_L] no row with 公司代號=='{KnownStock}' — StockCodeResolver.ExtractCompanyInfo filters on this field.");
            Assert.True(stock.TryGetProperty("公司名稱", out var name) && !string.IsNullOrEmpty(name.GetString()),
                "[t187ap03_L] '公司名稱' missing/empty — StockCodeResolver.ExtractCompanyInfo reads this exact field.");
        }
    }
}
