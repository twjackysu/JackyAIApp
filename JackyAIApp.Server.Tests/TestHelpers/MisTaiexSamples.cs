using System.Text;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// Canned answers of MIS's <c>getStockInfo.jsp?ex_ch=tse_t00.tw</c>. The layout is the one the stock
    /// quotes of the same endpoint have (<c>c</c> code, <c>d</c> date, <c>t</c> time, <c>tlong</c> epoch
    /// ms, <c>z</c> last, <c>y</c> previous close, all strings); it could not be fetched from the build
    /// environment, so these are representative, not recorded. That the real index entry has the same
    /// fields is what <c>TaiexContractTests</c> checks on the scheduled workflow.
    /// </summary>
    public static class MisTaiexSamples
    {
        public static readonly DateTimeOffset DefaultQuotedAt = new(2026, 10, 8, 13, 30, 0, TimeSpan.FromHours(8));

        /// <summary>A reading; pass null to leave a field out of the entry altogether.</summary>
        public static string Json(
            string? z = "23100.50", string? y = "23000.00", string? d = "20261008", string? t = "13:30:00",
            string? tlong = "default", string? c = "t00", string? ch = "t00.tw")
        {
            if (tlong == "default") tlong = DefaultQuotedAt.ToUnixTimeMilliseconds().ToString();

            var fields = new List<string> { "\"ex\":\"tse\"", "\"n\":\"發行量加權股價指數\"", "\"a\":\"_\"", "\"b\":\"_\"" };
            void Add(string name, string? value) { if (value is not null) fields.Add($"\"{name}\":\"{value}\""); }
            Add("c", c); Add("ch", ch); Add("z", z); Add("y", y); Add("d", d); Add("t", t); Add("tlong", tlong);

            var sb = new StringBuilder();
            sb.Append("{\"msgArray\":[{").Append(string.Join(",", fields)).Append("}],");
            sb.Append("\"referer\":\"\",\"userDelay\":5000,\"rtcode\":\"0000\",\"rtmessage\":\"OK\"}");
            return sb.ToString();
        }
    }
}
