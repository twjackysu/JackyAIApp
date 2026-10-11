using System.Text;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// A canned answer of MIS's <c>getStockInfo.jsp?ex_ch=tse_t00.tw</c>, with the field set that was
    /// recorded from the real endpoint (GitHub Actions <c>contract-tests.yml</c>, run 38140029229, a
    /// Sunday, so it shows the last session): <c>@ pid ^ key c # d % ch tlong h i it l m n o r ex t y z</c>,
    /// all strings, and no <c>u w a b f g v tv</c> — an index has no limit prices, order book or volume.
    /// Of the values only <c>c d ch tlong n t y z</c> were recorded; the others are placeholders, because
    /// nothing reads them.
    /// </summary>
    public static class MisTaiexSamples
    {
        /// <summary>2026-10-08 13:31:00 Taipei.</summary>
        public static readonly DateTimeOffset RecordedQuotedAt = new(2026, 10, 8, 13, 31, 0, TimeSpan.FromHours(8));

        public static string RecordedJson(string z = "49313.44", string y = "49806.37", string? tlong = null, string c = "t00")
        {
            tlong ??= RecordedQuotedAt.ToUnixTimeMilliseconds().ToString();
            var sb = new StringBuilder();
            sb.Append("{\"msgArray\":[{");
            sb.Append("\"@\":\"t00.tw\",\"pid\":\"6\",\"^\":\"20\",\"key\":\"tse_t00.tw_20261008\",");
            sb.Append($"\"c\":\"{c}\",\"#\":\"t00.tw\",\"d\":\"20261008\",\"%\":\"13:31:00\",\"ch\":\"t00.tw\",");
            sb.Append($"\"tlong\":\"{tlong}\",\"h\":\"49500.00\",\"i\":\"t00\",\"it\":\"12\",\"l\":\"49000.00\",\"m\":\"20\",");
            sb.Append("\"n\":\"發行量加權股價指數\",\"o\":\"49100.00\",\"r\":\"1\",\"ex\":\"tse\",\"t\":\"13:31:00\",");
            sb.Append($"\"y\":\"{y}\",\"z\":\"{z}\"");
            sb.Append("}],\"queryTime\":{\"sysTime\":\"13:31:05\"},\"referer\":\"\",\"userDelay\":5000,\"rtcode\":\"0000\",\"rtmessage\":\"OK\"}");
            return sb.ToString();
        }
    }
}
