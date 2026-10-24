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
    /// <summary>
    /// A short quiz on the reader's own words, in the chat (Studio AI's 「考我今天的單字」): a few multiple-choice
    /// questions on the words due for review (<see cref="IReviewQuizService"/>, the daily challenge's kinds, no AI), shown
    /// as a <c>quiz</c> card the reader answers on; the card sends each answer to the review schedule itself, as the
    /// review page does. A read: making the quiz changes nothing and charges nothing. The model is told the words, and
    /// the questions and answers only for a client that cannot draw the card (another AI through the site's MCP server).
    /// </summary>
    public sealed class ReviewQuizTool(IReviewQuizService quizzes) : ISiteTool
    {
        public const string Name = "review_quiz";

        internal const int DefaultCount = 3;

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "複習小測驗",
            "從使用者到期該複習的單字（沒有到期的，就用單字庫裡最快要複習的）出幾題選擇題，" +
            "在對話裡顯示成可以直接作答的卡片；使用者在卡片上作答，答案會記進他的複習排程。" +
            "使用者說「考我」「出題」「測驗我今天的單字」或想在這裡複習時使用。",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    count = new
                    {
                        type = "integer",
                        minimum = 1,
                        maximum = ReviewQuizService.MaxQuestions,
                        description = $"題數，預設 {DefaultCount}",
                    },
                },
            }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            var count = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("count", out var given) &&
                        given.ValueKind == JsonValueKind.Number && given.TryGetInt32(out var n)
                ? n
                : DefaultCount;
            var made = await quizzes.CreateAsync(count, ct);
            if (!made.IsSuccess) return AiToolResult.Error("現在出不了題，請告訴使用者稍後再試。");

            var quiz = made.Data!;
            if (quiz.Questions.Count == 0)
                return new AiToolResult(
                    "使用者的單字庫還沒有可以出題的單字。建議他先查幾個字並加入單字庫（可以用 look_up_word、add_word_to_repository）。",
                    Card: new { kind = "link", title = "字典", href = "/english/dictionary", description = "查單字並加入單字庫，就能出題考你", actionLabel = "去查單字" });

            var letters = "ABCDEFGH";
            var text = new StringBuilder();
            text.AppendLine($"已出 {quiz.Questions.Count} 題小測驗，考的字：{string.Join("、", quiz.Questions.Select(q => q.Word))}" +
                            (quiz.FromDue ? $"（到期該複習的共 {quiz.TotalDueCount} 個）。" : "（今天沒有到期的字，挑的是最快要複習的）。"));
            text.AppendLine("題目已經顯示在使用者畫面上的卡片裡，他會直接在卡片上作答，答案會記進複習排程。" +
                            "請用一兩句話請他作答；不要在回答中重複題目，也不要說出答案。");
            text.AppendLine();
            text.AppendLine("以下只供看不到卡片的情況使用（例如在其他 AI 裡）：一次問一題，等使用者作答後再公布那一題的答案。");
            foreach (var (q, i) in quiz.Questions.Select((q, i) => (q, i)))
            {
                text.AppendLine($"{i + 1}. {q.Prompt}");
                text.AppendLine("   " + string.Join("  ", q.Options.Select((o, j) => $"({letters[j % letters.Length]}) {o}")));
            }
            text.AppendLine("答案：" + string.Join("、", quiz.Questions.Select((q, i) => $"{i + 1}-{letters[q.CorrectIndex % letters.Length]}")));

            return new AiToolResult(text.ToString().TrimEnd(), Card: new
            {
                kind = "quiz",
                title = "複習小測驗",
                data = new
                {
                    fromDue = quiz.FromDue,
                    totalDue = quiz.TotalDueCount,
                    questions = quiz.Questions.Select(q => new
                    {
                        userWordId = q.UserWordId,
                        word = q.Word,
                        prompt = q.Prompt,
                        options = q.Options,
                        correctIndex = q.CorrectIndex,
                        explanation = q.Explanation,
                    }),
                },
            });
        }
    }
}
