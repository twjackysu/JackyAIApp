using System.Text;
using System.Text.Json;
using JackyAIApp.Server.DTO;
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
    /// Where the reader went wrong lately, for Studio AI (「找出我的弱點」): how each exam type went over the last days and
    /// the words they answered wrong most (<see cref="IStatsService.GetWeakSpotsAsync"/>, their own exam attempts), as a
    /// table card, so the model can coach them and offer a <see cref="ReviewQuizTool"/> on what is due. A read: nothing
    /// is changed or charged. The words are the dictionary's, the counts the site's own, so nothing here is outside text.
    /// </summary>
    public sealed class WeakSpotsTool(IStatsService stats) : ISiteTool
    {
        public const string Name = "weak_spots";

        internal const int DefaultDays = 30;

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "最近的弱點",
            "讀取使用者最近幾天（預設 30 天）的英文測驗紀錄：各種測驗（克漏字、翻譯、造句、聽寫、聽力、閱讀…）的答對率，" +
            "以及最常答錯的單字（答錯幾次、最近一次答對了沒）。使用者問自己哪裡最弱、最常錯什麼、測驗表現、該加強什麼時使用。",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    days = new
                    {
                        type = "integer",
                        minimum = StatsService.MinWeakSpotDays,
                        maximum = StatsService.MaxWeakSpotDays,
                        description = $"往回看幾天，預設 {DefaultDays}",
                    },
                },
            }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        /// <summary>The exam types as the reader knows them on the exam page; another type is shown as recorded.</summary>
        internal static string LabelOf(string examType) => examType switch
        {
            "Cloze" => "克漏字",
            "Translation" => "翻譯",
            "Sentence" => "造句",
            "Dictation" => "聽寫",
            "Listening" => "聽力（看圖）",
            "ListeningResponse" => "聽力（選回應）",
            "Reading" => "閱讀測驗",
            "Writing" => "寫作",
            _ => examType,
        };

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            var days = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("days", out var given) &&
                       given.ValueKind == JsonValueKind.Number && given.TryGetInt32(out var n)
                ? n
                : DefaultDays;
            var read = await stats.GetWeakSpotsAsync(days, ct);
            if (!read.IsSuccess) return AiToolResult.Error("測驗紀錄暫時讀不到，請告訴使用者稍後再試。");

            var spots = read.Data!;
            if (spots.TotalAttempts == 0)
                return new AiToolResult(
                    $"使用者最近 {spots.Days} 天沒有做過測驗，還看不出弱點。建議他先做幾題測驗，或用 review_quiz 在這裡考幾題。",
                    Card: new { kind = "link", title = "英文測驗", href = "/english/exam", description = "克漏字、翻譯、聽寫…做幾題就看得出弱點", actionLabel = "去做測驗" });

            var text = new StringBuilder();
            var correct = spots.ByType.Sum(t => t.Correct);
            text.AppendLine($"最近 {spots.Days} 天共作答 {spots.TotalAttempts} 次，答對 {correct} 次（{Percent(correct, spots.TotalAttempts)}）。");
            text.AppendLine("各種測驗：" + string.Join("；", spots.ByType.Select(t =>
                $"{LabelOf(t.ExamType)} {t.Correct}/{t.Attempts}（{Percent(t.Correct, t.Attempts)}）")));
            if (spots.Words.Count == 0)
            {
                text.AppendLine("這段期間考到的單字都答對了，沒有答錯的字。");
            }
            else
            {
                text.AppendLine("最常答錯的單字（答錯／作答次數；最近一次的結果）：");
                foreach (var w in spots.Words)
                    text.AppendLine($"- {w.Word}：{w.Wrong}/{w.Attempts}；最近一次{(w.LatestCorrect ? "答對了" : "還是錯")}");
            }
            text.AppendLine("請先用一兩句話點出最需要加強的地方（答對率最低的測驗、還是錯的字），數字照上面，不要自己估算；" +
                            "再給一個具體的下一步，例如用 review_quiz 考最常錯的幾個字（words 傳那幾個字）、用 look_up_word 解釋最常錯的字，或去做答對率最低的那種測驗。");

            return new AiToolResult(text.ToString().TrimEnd(), Card: spots.Words.Count > 0 ? WordsCard(spots) : TypesCard(spots));
        }

        private static object WordsCard(WeakSpotsResponse spots) => new
        {
            kind = "table",
            title = $"最近 {spots.Days} 天最常答錯的單字",
            columns = new object[]
            {
                new { key = "word", label = "單字" },
                new { key = "wrong", label = "答錯", numeric = true },
                new { key = "attempts", label = "作答", numeric = true },
                new { key = "latest", label = "最近一次" },
            },
            rows = spots.Words.Select(w => new Dictionary<string, object?>
            {
                ["word"] = w.Word,
                ["wrong"] = w.Wrong,
                ["attempts"] = w.Attempts,
                ["latest"] = w.LatestCorrect ? "答對" : "答錯",
            }).ToList(),
        };

        private static object TypesCard(WeakSpotsResponse spots) => new
        {
            kind = "table",
            title = $"最近 {spots.Days} 天的測驗",
            columns = new object[]
            {
                new { key = "type", label = "測驗" },
                new { key = "attempts", label = "作答", numeric = true },
                new { key = "rate", label = "答對 %", numeric = true },
            },
            rows = spots.ByType.Select(t => new Dictionary<string, object?>
            {
                ["type"] = LabelOf(t.ExamType),
                ["attempts"] = t.Attempts,
                ["rate"] = Math.Round(100.0 * t.Correct / t.Attempts),
            }).ToList(),
        };

        private static string Percent(int part, int whole) => $"{Math.Round(100.0 * part / whole)}%";
    }

    /// <summary>
    /// A short quiz on the reader's own words, in the chat (Studio AI's 「考我今天的單字」): a few multiple-choice
    /// questions on the words due for review (<see cref="IReviewQuizService"/>, the daily challenge's kinds, no AI), shown
    /// as a <c>quiz</c> card the reader answers on; for words that are due, the card sends the answers to the review
    /// schedule itself, as the review page does (words not yet due are practice: an early answer would push them out).
    /// A read: making the quiz changes nothing and charges nothing. The model is told the words, and
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
            "使用者說「考我」「出題」「測驗我今天的單字」或想在這裡複習時使用；要考特定的字（例如 weak_spots 列出最常錯的字，" +
            "使用者說「考我這幾個字」）時，把那幾個字放在 words。",
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
                        description = $"題數，預設 {DefaultCount}；有 words 時不用",
                    },
                    words = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        maxItems = ReviewQuizService.MaxQuestions,
                        description = "只考這幾個英文單字（使用者單字庫裡的字），每個字一題；不傳就考到期該複習的字",
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
            var words = WordsOf(input);
            var made = words.Count > 0 ? await quizzes.CreateForAsync(words, ct) : await quizzes.CreateAsync(count, ct);
            if (!made.IsSuccess) return AiToolResult.Error("現在出不了題，請告訴使用者稍後再試。");

            var quiz = made.Data!;
            if (quiz.Asked != null && quiz.Questions.Count == 0)
                return new AiToolResult(
                    $"指定的字（{string.Join("、", quiz.Asked)}）都不在使用者的單字庫裡（或字典裡沒有釋義），沒有出題。" +
                    "可以問他要不要先把它們加入單字庫（add_word_to_repository），或改考到期該複習的字（review_quiz 不傳 words）。",
                    Card: new { kind = "link", title = "單字庫", href = "/english/repository", description = "加入單字庫的字才能出題考你", actionLabel = "看我的單字庫" });
            if (quiz.Questions.Count == 0)
                return new AiToolResult(
                    "使用者的單字庫還沒有可以出題的單字。建議他先查幾個字並加入單字庫（可以用 look_up_word、add_word_to_repository）。",
                    Card: new { kind = "link", title = "字典", href = "/english/dictionary", description = "查單字並加入單字庫，就能出題考你", actionLabel = "去查單字" });

            var letters = "ABCDEFGH";
            var text = new StringBuilder();
            text.AppendLine($"已出 {quiz.Questions.Count} 題小測驗，考的字：{string.Join("、", quiz.Questions.Select(q => q.Word))}" + WhichWords(quiz));
            text.AppendLine("題目已經顯示在使用者畫面上的卡片裡，他會直接在卡片上作答" +
                            (quiz.FromDue ? "，答案會記進複習排程。" : $"；{(quiz.Asked == null ? "這些" : "有的")}字還沒到期，這次是練習，不會記進複習排程。") +
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

        /// <summary>The words the quiz was asked about, as given (strings only), else none.</summary>
        private static List<string> WordsOf(JsonElement input) =>
            input.ValueKind == JsonValueKind.Object && input.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array
                ? [.. words.EnumerateArray().Where(w => w.ValueKind == JsonValueKind.String).Select(w => w.GetString()!)]
                : [];

        /// <summary>Which words these are, for the model: the ones asked for (and those left out), the due ones, or the soonest.</summary>
        private static string WhichWords(ReviewQuiz quiz)
        {
            if (quiz.Asked == null)
                return quiz.FromDue ? $"（到期該複習的共 {quiz.TotalDueCount} 個）。" : "（今天沒有到期的字，挑的是最快要複習的）。";
            var left = quiz.Asked.Where(a => !quiz.Questions.Any(q => string.Equals(q.Word, a, StringComparison.OrdinalIgnoreCase))).ToList();
            return "（使用者指定的字）。" + (left.Count > 0 ? $"不在他單字庫裡、沒有出題的字：{string.Join("、", left)}。" : "");
        }
    }
}
