namespace JackyAIApp.Server.DTO
{
    /// <summary>
    /// Where the reader went wrong in their exams lately (Studio AI's 「找出我的弱點」): how each exam type went and the
    /// words they answered wrong most, from their own exam attempts in the window.
    /// </summary>
    public class WeakSpotsResponse
    {
        /// <summary>How many days back the window reaches, today (UTC) included.</summary>
        public required int Days { get; set; }

        /// <summary>Every attempt read in the window (at most <c>StatsService.MaxWeakSpotAttempts</c>, the most recent).</summary>
        public required int TotalAttempts { get; set; }

        /// <summary>Each exam type tried in the window, most attempts first.</summary>
        public required List<ExamTypeResult> ByType { get; set; }

        /// <summary>The words answered wrong at least once, most wrong first (at most <c>StatsService.MaxWeakWords</c>).</summary>
        public required List<WeakWord> Words { get; set; }
    }

    public class ExamTypeResult
    {
        /// <summary>The exam type as recorded: "Cloze", "Translation", "Sentence", "Dictation", "Listening", …</summary>
        public required string ExamType { get; set; }
        public required int Attempts { get; set; }
        public required int Correct { get; set; }
    }

    public class WeakWord
    {
        public required string Word { get; set; }
        public required int Attempts { get; set; }
        public required int Wrong { get; set; }

        /// <summary>Whether the latest attempt at it was right: wrong before, right since.</summary>
        public required bool LatestCorrect { get; set; }

        /// <summary>When it was last answered wrong (UTC).</summary>
        public required DateTime LastWrongAt { get; set; }
    }
}
