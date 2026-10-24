using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.DailyChallenge;
using JackyAIApp.Server.Services.SpacedRepetition;

namespace JackyAIApp.Server.Services.Dictionary.Assistant
{
    /// <summary>
    /// Where the reader stands in their English learning today, for Studio AI: how many saved words are due
    /// for review (and a few of them), the daily challenge, the streak and XP — with a link to start reviewing.
    /// </summary>
    public sealed class LearningProgressTool(IReviewService reviews, IDailyChallengeService challenge) : ISiteTool
    {
        public const string Name = "learning_progress";

        /// <summary>The most due words named to the model.</summary>
        internal const int MaxWordsNamed = 10;

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "學習進度",
            "讀取使用者今天的英文學習狀況：有幾個單字到期該複習（並列出幾個）、今天的每日挑戰做了沒、連續天數、XP 與等級。" +
            "使用者問到複習、今天要做什麼、學習進度時使用。",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { } }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            var due = await reviews.GetDueAsync(ct);
            var stats = await challenge.GetStatsAsync();
            if (!due.IsSuccess && !stats.IsSuccess) return AiToolResult.Error("學習進度暫時讀不到，請告訴使用者稍後再試。");

            var text = new StringBuilder();
            var dueCount = due.IsSuccess ? due.Data!.TotalDueCount : (int?)null;
            if (due.IsSuccess)
            {
                text.AppendLine($"到期該複習的單字：{dueCount} 個");
                var words = due.Data!.DueWords.Take(MaxWordsNamed).Select(w => w.WordText).ToList();
                if (words.Count > 0) text.AppendLine($"其中幾個：{string.Join(", ", words)}");
            }
            if (stats.IsSuccess)
            {
                var s = stats.Data!;
                text.AppendLine($"今天的每日挑戰：{(s.TodayCompleted ? $"已完成（{s.TodayScore ?? 0} 分）" : "還沒做")}");
                text.AppendLine($"連續 {s.CurrentStreak} 天（最長 {s.LongestStreak} 天），本週還有 {s.StreakFreezesAvailable} 次連續天數保護");
                text.AppendLine($"XP {s.TotalXP}，等級 {s.Level}");
            }

            var (href, label, description) = dueCount > 0
                ? ("/english/review", "開始複習", $"{dueCount} 個單字等你複習")
                : stats.IsSuccess && !stats.Data!.TodayCompleted
                    ? ("/english/daily", "做每日挑戰", "今天的 5 題每日挑戰")
                    : ("/english/stats", "看學習統計", "連續天數、XP 與每週報告");
            return new AiToolResult(text.ToString().TrimEnd(), Card: new { kind = "link", title = "英文學習", href, description, actionLabel = label });
        }
    }
}
