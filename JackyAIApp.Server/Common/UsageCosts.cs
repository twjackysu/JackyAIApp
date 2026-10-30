namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Per-feature weights for the daily usage-limit budget (DECISION-006/007), replacing the old
    /// per-feature credit-cost table as the gate every AI-powered endpoint consumes against.
    /// Values were carried over unchanged from that table — they already reflect relative AI
    /// cost — except the gamification bonuses (DailyLoginBonus/DailyChallengeBonus/
    /// StreakMilestone7Day), which this mechanism does not model: usage is consumed, never
    /// granted, so there is nothing for a "bonus" to mean here.
    /// What each feature covers is documented on <see cref="UsageFeature"/>.
    /// </summary>
    public static class UsageCosts
    {
        /// <summary>The cost of one use of <paramref name="feature"/>. For a chat message, the
        /// message alone; what it costs with its attachments is <see cref="ForChatMessage"/>.</summary>
        public static ulong Of(UsageFeature feature) => feature switch
        {
            UsageFeature.ClozeTest => 2,
            UsageFeature.TranslationTest => 2,
            UsageFeature.TranslationGrading => 1,
            UsageFeature.ConversationStart => 3,
            UsageFeature.ConversationResponse => 2,
            UsageFeature.ConversationSummary => 2,
            UsageFeature.SentenceTest => 2,
            UsageFeature.SentenceEvaluation => 2,
            UsageFeature.ListeningTest => 4,
            UsageFeature.ListeningResponseTest => 4,
            UsageFeature.ReadingComprehensionTest => 3,
            UsageFeature.WritingEvaluation => 3,
            UsageFeature.DictationTest => 1,
            UsageFeature.AudioTranscription => 1,
            UsageFeature.StockAnalysis => 5,
            UsageFeature.MarketDigest => 3,
            UsageFeature.ChatMessage => 5,
            UsageFeature.TextToSpeech => 1,
            UsageFeature.NlSummarize => 2,
            UsageFeature.PortfolioAiSummary => 2,
            UsageFeature.ChatToolRound => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "No usage cost for this feature."),
        };

        /// <summary>How many requests of one chat turn its message pays for; each request after them
        /// (a tool round: the model asked for tools again) is one <see cref="UsageFeature.ChatToolRound"/>
        /// (docs/ai-native/design/06 §6). Most turns take one or two.</summary>
        public const int FreeChatToolRounds = 3;

        /// <summary>Each file sent with a chat message, on top of the message: its text is sent to
        /// the model again on every later turn of the conversation.</summary>
        public const ulong ChatAttachment = 2;

        /// <summary>A PDF costs <see cref="ChatAttachment"/> plus this per 10 pages (rounded up): the
        /// model reads every page as text and as a picture, again on every later turn.</summary>
        public const ulong ChatPdfPer10Pages = 1;

        /// <summary>What one attachment adds to the message it is sent with.</summary>
        public static ulong ForChatAttachment(int? pdfPages) =>
            ChatAttachment + (pdfPages is > 0 ? ChatPdfPer10Pages * (ulong)((pdfPages.Value + 9) / 10) : 0);

        /// <summary>A chat message with its attachments, given each attachment's PDF page count
        /// (null for anything that is not a PDF).</summary>
        public static ulong ForChatMessage(IEnumerable<int?> attachmentPdfPages) =>
            attachmentPdfPages.Aggregate(Of(UsageFeature.ChatMessage), (sum, pages) => sum + ForChatAttachment(pages));
    }
}
