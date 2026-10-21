namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// What a usage charge is for. Each feature has exactly one cost, in <see cref="UsageCosts.Of"/>,
    /// so a charge names the feature and never a cost of its own (chat is the one feature whose cost
    /// depends on the request — see <see cref="UsageCosts.ForChatMessage"/>).
    /// </summary>
    public enum UsageFeature
    {
        // Dictionary word lookup is intentionally free — a generated word is saved to the
        // Word table, so the OpenAI call only ever runs once per word, not once per user.

        /// <summary>Cloze test generation</summary>
        ClozeTest,

        /// <summary>Translation test generation</summary>
        TranslationTest,

        /// <summary>Translation quality grading</summary>
        TranslationGrading,

        /// <summary>Conversation test (start)</summary>
        ConversationStart,

        /// <summary>Conversation response (each turn)</summary>
        ConversationResponse,

        /// <summary>Conversation end-of-session summary</summary>
        ConversationSummary,

        /// <summary>Sentence test generation</summary>
        SentenceTest,

        /// <summary>Sentence AI evaluation/grading</summary>
        SentenceEvaluation,

        /// <summary>Listening test (TOEIC Part 1 style) — bundles the question fetch plus the
        /// TTS for its 4 candidate statements into one flat charge; playback itself is free.</summary>
        ListeningTest,

        /// <summary>Listening response test (TOEIC Part 2 style) — bundles the question fetch
        /// plus TTS for the prompt and its 3 responses (4 audio clips total, same as ListeningTest).</summary>
        ListeningResponseTest,

        /// <summary>Reading comprehension test — a passage-sized text-only completion, charged
        /// flat per play (cache hit or miss), same convention as ListeningResponseTest.</summary>
        ReadingComprehensionTest,

        /// <summary>Writing evaluation — the GET api/exam/writing prompt itself is a free DB
        /// query (no AI cost, deliberately not usage-gated); the whole charge for the feature
        /// sits here, on the one-shot paragraph grading call.</summary>
        WritingEvaluation,

        /// <summary>Dictation practice — one TTS clip per attempt (a single word, not
        /// ListeningTest's four options), priced at raw TextToSpeech cost rather than
        /// ListeningTest's bundled rate.</summary>
        DictationTest,

        /// <summary>Whisper audio transcription</summary>
        AudioTranscription,

        /// <summary>Stock analysis (AI-powered)</summary>
        StockAnalysis,

        /// <summary>Daily AI market digest (global + TW, charged once per user per day)</summary>
        MarketDigest,

        /// <summary>Finance AI assistant (chatbot) — charged per message sent, plus its attachments
        /// (<see cref="UsageCosts.ForChatMessage"/>)</summary>
        ChatMessage,

        /// <summary>Text-to-speech synthesis</summary>
        TextToSpeech,

        /// <summary>Natural-language summarize (LLM translates query to parameters + executes)</summary>
        NlSummarize,

        /// <summary>Portfolio AI technical-analysis summary for a single position</summary>
        PortfolioAiSummary,
    }
}
