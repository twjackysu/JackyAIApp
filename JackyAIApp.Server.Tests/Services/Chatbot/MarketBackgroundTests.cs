using System.Text.Json;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>The /finance page's market data as the assistant's background, in few tokens.</summary>
    public class MarketBackgroundTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 30, 0, TimeSpan.FromHours(8));

        private static MarketBackgroundData Sample() => new(
            new MarketOverviewResponse
            {
                Quotes =
                [
                    new() { Group = "US", Name = "S&P 500", Price = 7712.57m, Change = 21.84m, ChangePercent = 0.28m },
                    new() { Group = "TW", Name = "加權指數", Price = 46292.93m, Change = -529.15m, ChangePercent = -1.13m },
                    new() { Group = "Other", Name = "BTC", Price = null },
                ],
            },
            new MacroEconomyResponse
            {
                MarketIndex = [new() { Date = "1151020", TAIEX = 46292.93m, Change = -529.15m, TradeValue = 802_947_441_658 }],
                SectorIndices = [new() { Date = "1151020", Name = "半導體類", CloseIndex = 1234.5m, Direction = "-", ChangePercent = 0.5m }],
                Margin = new MarginSummary
                {
                    MarginBalanceTotal = 9_600_627, MarginPrevBalanceTotal = 9_592_706,
                    ShortBalanceTotal = 215_912, ShortPrevBalanceTotal = 201_911,
                },
                ExchangeRates = [new() { Currency = "USD", DisplayName = "美元", Rate = 32.4112m }],
                BankRate = new BankRate { BankName = "臺銀", Period = "11509", OneYearFixed = 1.725m, BaseLendingRate = 2.9m },
            },
            History(),
            new IndicatorExplorerResponse
            {
                Indicators = [new() { Name = "美國 10 年期公債殖利率, 日", Unit = "%", LatestValue = 4.12m, PreviousValue = 4.1m, LatestDate = "2026-10-20" }],
            });

        /// <summary>Three years of closes rising 1 point a day from 1000, to 2026-10-20.</summary>
        private static MarketIndexHistoryResponse History()
        {
            var end = new DateOnly(2026, 10, 20);
            var start = end.AddYears(-3);
            var points = new List<MarketIndexPoint>();
            var close = 1000m;
            for (var d = start; d <= end; d = d.AddDays(1), close += 1) points.Add(new MarketIndexPoint(d, close));
            return new MarketIndexHistoryResponse("^TWII", "加權指數", "Yahoo", end, points);
        }

        private static string Render() => MarketBackground.Render(Sample(), Now)!;

        [Fact]
        public void EachTable_IsOneHeaderNamingItsFields_ThenARowPerLine()
        {
            var text = Render();

            Assert.Contains("報價[2]{區,項目,價,漲跌,漲跌%}:\n  美,S&P 500,7712.57,+21.84,+0.28\n  台,加權指數,46292.93,-529.15,-1.13", text);
        }

        [Fact]
        public void NumbersCarryNoThousandsSeparators_AndAMissingQuoteIsLeftOut()
        {
            var text = Render();

            Assert.Contains("融資融券_上市_張[1]{融資餘額,融資增減,融券餘額,融券增減,券資比%}:\n  9600627,+7921,215912,+14001,2.25", text);
            Assert.DoesNotContain("BTC", text);
        }

        [Fact]
        public void ATextWithACommaIsQuoted_SoTheRowStillSplitsRight()
        {
            Assert.Contains("\"美國 10 年期公債殖利率, 日\",4.12,4.1,%,2026-10-20", Render());
        }

        [Fact]
        public void TheTaiex_GivesTheDayFromTwse_AndItsTrendFromHistory()
        {
            var text = Render();

            Assert.Contains("加權指數[1]{日期,收盤,漲跌,漲跌%,成交值億}:\n  10/20,46292.93,-529.15,-1.13,8029", text);
            Assert.Contains("加權指數報酬%_至10/20[1]{1週,1月,3月,今年,1年}:", text);
            Assert.Contains("加權指數位置[1]{收盤,52週高,高點日,52週低,低點日,距高%,MA20,MA60,MA240}:", text);
            Assert.Contains("10月報酬%[1]{本月至今,過去平均,過去上漲年數,年數}:", text);
        }

        [Fact]
        public void ASectorFalling_IsShownWithAMinus()
        {
            Assert.Contains("類股指數_10/20[1]{類股,指數,漲跌%}:\n  半導體類,1234.5,-0.5", Render());
        }

        [Fact]
        public void TheHeader_DatesTheSnapshot_AndSaysToUseTheToolsForMore()
        {
            var text = Render();

            Assert.StartsWith("# 市場背景（2026-10-21 09:30 台北時間的 /finance 頁面快照", text);
            Assert.Contains("tw-stock", text);
        }

        [Fact]
        public void NoDataAtAll_IsNoBackground() =>
            Assert.Null(MarketBackground.Render(new MarketBackgroundData(null, null, null, null), Now));

        [Fact]
        public void TheBackground_IsASecondSystemBlock_CachedForEveryConversation()
        {
            var request = AnthropicChatService.BuildRequest(new DateOnly(2026, 10, 21), [], "# 市場背景");
            var system = JsonSerializer.SerializeToElement(request).GetProperty("RawBodyData").GetProperty("system");

            Assert.Equal(2, system.GetArrayLength());
            Assert.StartsWith("今天是 2026-10-21", system[0].GetProperty("text").GetString());
            Assert.False(system[0].TryGetProperty("cache_control", out _));
            Assert.Equal("# 市場背景", system[1].GetProperty("text").GetString());
            Assert.Equal("ephemeral", system[1].GetProperty("cache_control").GetProperty("type").GetString());
        }

        [Fact]
        public void TheReturnsAreRightForASteadyRise()
        {
            // From 999+... a rise of 1 a day: the last week is 7 points on ~2088.
            var text = Render();
            var line = text.Split('\n')[text.Split('\n').ToList().FindIndex(l => l.StartsWith("加權指數報酬%")) + 1];
            Assert.StartsWith("  +0.34,", line);
        }
    }
}
