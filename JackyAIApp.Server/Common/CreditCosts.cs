namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Credit costs for AI-powered features.
    /// Free features (Daily Challenge, Spaced Review) are not listed here.
    /// </summary>
    public static class CreditCosts
    {
        /// <summary>Dictionary word lookup (AI-generated definitions)</summary>
        public const ulong DictionaryLookup = 1;

        /// <summary>Cloze test generation</summary>
        public const ulong ClozeTest = 2;

        /// <summary>Translation test generation</summary>
        public const ulong TranslationTest = 2;

        /// <summary>Translation quality grading</summary>
        public const ulong TranslationGrading = 1;

        /// <summary>Conversation test (start)</summary>
        public const ulong ConversationStart = 3;

        /// <summary>Conversation response (each turn)</summary>
        public const ulong ConversationResponse = 1;

        /// <summary>Sentence test generation</summary>
        public const ulong SentenceTest = 2;

        /// <summary>Stock analysis (AI-powered)</summary>
        public const ulong StockAnalysis = 5;

        /// <summary>Text-to-speech synthesis</summary>
        public const ulong TextToSpeech = 1;

        /// <summary>Daily login bonus</summary>
        public const ulong DailyLoginBonus = 10;

        /// <summary>Daily challenge completion bonus</summary>
        public const ulong DailyChallengeBonus = 5;

        /// <summary>7-day streak milestone bonus</summary>
        public const ulong StreakMilestone7Day = 50;
    }
}
