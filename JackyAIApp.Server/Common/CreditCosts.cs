namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Credit costs for AI-powered features.
    /// Free features (Daily Challenge, Spaced Review) are not listed here.
    /// </summary>
    public static class CreditCosts
    {
        // Dictionary word lookup is intentionally free — a generated word is saved to the
        // Word table, so the OpenAI call only ever runs once per word, not once per user.

        /// <summary>Cloze test generation</summary>
        public const ulong ClozeTest = 2;

        /// <summary>Translation test generation</summary>
        public const ulong TranslationTest = 2;

        /// <summary>Translation quality grading</summary>
        public const ulong TranslationGrading = 1;

        /// <summary>Conversation test (start)</summary>
        public const ulong ConversationStart = 3;

        /// <summary>Conversation response (each turn)</summary>
        public const ulong ConversationResponse = 2;

        /// <summary>Conversation end-of-session summary</summary>
        public const ulong ConversationSummary = 2;

        /// <summary>Sentence test generation</summary>
        public const ulong SentenceTest = 2;

        /// <summary>Sentence AI evaluation/grading</summary>
        public const ulong SentenceEvaluation = 2;

        /// <summary>Listening test (TOEIC Part 1 style) — bundles the question fetch plus the
        /// TTS for its 4 candidate statements into one flat charge; playback itself is free.</summary>
        public const ulong ListeningTest = 4;

        /// <summary>Listening response test (TOEIC Part 2 style) — bundles the question fetch
        /// plus TTS for the prompt and its 3 responses (4 audio clips total, same as ListeningTest).</summary>
        public const ulong ListeningResponseTest = 4;

        /// <summary>Whisper audio transcription</summary>
        public const ulong AudioTranscription = 1;

        /// <summary>Stock analysis (AI-powered)</summary>
        public const ulong StockAnalysis = 5;

        /// <summary>Daily AI market digest (global + TW, charged once per user per day)</summary>
        public const ulong MarketDigest = 3;

        /// <summary>Floating AI chatbot — charged per message sent</summary>
        public const ulong ChatMessage = 5;

        /// <summary>Text-to-speech synthesis</summary>
        public const ulong TextToSpeech = 1;

        /// <summary>Natural-language summarize (LLM translates query to parameters + executes)</summary>
        public const ulong NlSummarize = 2;

        /// <summary>Portfolio AI technical-analysis summary for a single position</summary>
        public const ulong PortfolioAiSummary = 2;

        /// <summary>Daily login bonus</summary>
        public const ulong DailyLoginBonus = 10;

        /// <summary>Daily challenge completion bonus</summary>
        public const ulong DailyChallengeBonus = 5;

        /// <summary>7-day streak milestone bonus</summary>
        public const ulong StreakMilestone7Day = 50;
    }
}
