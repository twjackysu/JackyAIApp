namespace JackyAIApp.Server.DTO
{
    public enum QuestionType
    {
        VocabularyDefinition,
        FillInTheBlank,
        Synonym,
        Antonym,
        Translation
    }

    public class DailyChallengeQuestion
    {
        public int Id { get; set; }
        public QuestionType Type { get; set; }
        public required string Prompt { get; set; }
        public required List<string> Options { get; set; }
        public int CorrectIndex { get; set; }
        public required string Explanation { get; set; }
    }

    public class DailyChallengeResponse
    {
        public required List<DailyChallengeQuestion> Questions { get; set; }
        public required string ChallengeDate { get; set; }
        public bool AlreadyCompleted { get; set; }
        public int? PreviousScore { get; set; }
    }

    public class DailyChallengeSubmitRequest
    {
        public required List<DailyChallengeAnswer> Answers { get; set; }
    }

    public class DailyChallengeAnswer
    {
        public int QuestionId { get; set; }
        public int SelectedIndex { get; set; }
    }

    public class DailyChallengeSubmitResponse
    {
        public int Score { get; set; }
        public int TotalQuestions { get; set; }
        public int XPEarned { get; set; }
        public bool StreakUpdated { get; set; }
        public int NewStreak { get; set; }
        public bool AlreadyCompleted { get; set; }
        public ulong CreditsAwarded { get; set; }
    }

    public class DailyChallengeStatsResponse
    {
        public int CurrentStreak { get; set; }
        public int LongestStreak { get; set; }
        public int TotalXP { get; set; }
        public required string Level { get; set; }
        public bool TodayCompleted { get; set; }
        public int? TodayScore { get; set; }
    }
}
