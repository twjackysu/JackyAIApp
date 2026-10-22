using JackyAIApp.Server.Services.ExternalApi.Twse;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// The Taiwan announcements the market digest is grounded in (<see cref="MarketInsightService.MajorNewsText"/>):
    /// one "code name：subject" line per announcement of t187ap04_L.
    /// </summary>
    public class MarketInsightMajorNewsTests
    {
        private static async Task<IReadOnlyList<TwseDataRow>?> ReadAsync(string json)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => StubHttpMessageHandler.CreateClient(_ => StubHttpMessageHandler.JsonResponse(json)));
            var twse = new TwseOpenApi(factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseOpenApi>.Instance, TimeProvider.System);
            return await twse.GetMaterialInfoAsync();
        }

        [Fact]
        public async Task EachAnnouncement_IsOneLine_WithTwsesOwnPaddedSubjectField()
        {
            // TWSE names the subject "主旨 ", with a trailing space. Read as "主旨" it never matched,
            // and the digest never had a Taiwan announcement in it.
            var rows = await ReadAsync("""
                [{"出表日期":"1151022","公司代號":"2330","公司名稱":"台積電","主旨 ":"公告本公司董事會決議股利分派","說明":"..."},
                 {"出表日期":"1151022","公司代號":"1101","公司名稱":"台泥","主旨 ":"澄清媒體報導"}]
                """);

            Assert.Equal("2330 台積電：公告本公司董事會決議股利分派\n1101 台泥：澄清媒體報導", MarketInsightService.MajorNewsText(rows));
        }

        [Fact]
        public async Task ASubjectWrappedOntoSeveralLines_StaysOnOne()
        {
            var rows = await ReadAsync("""[{"公司代號":"2072","公司名稱":"世紀風電","主旨 ":"公告本公司名稱\r\n更名，公告期間：\n115年11月25日。"}]""");

            Assert.Equal("2072 世紀風電：公告本公司名稱更名，公告期間：115年11月25日。", MarketInsightService.MajorNewsText(rows));
        }

        [Fact]
        public async Task AnnouncementsWithoutASubject_AreLeftOut()
        {
            var rows = await ReadAsync("""[{"公司代號":"2330","公司名稱":"台積電","主旨":""},{"公司代號":"1101","公司名稱":"台泥","主旨":"澄清"}]""");

            Assert.Equal("1101 台泥：澄清", MarketInsightService.MajorNewsText(rows));
        }

        [Fact]
        public async Task AtMost60_AreListed()
        {
            var json = "[" + string.Join(",", Enumerable.Range(1, 80).Select(i => $$"""{"公司代號":"{{1000 + i}}","公司名稱":"公司{{i}}","主旨":"主旨{{i}}"}""")) + "]";

            var text = MarketInsightService.MajorNewsText(await ReadAsync(json));

            var lines = text.Split('\n');
            Assert.Equal(60, lines.Length);
            Assert.Equal("1060 公司60：主旨60", lines[^1]);
        }

        [Fact]
        public void NoAnnouncements_IsEmpty() => Assert.Equal("", MarketInsightService.MajorNewsText(null));
    }
}
