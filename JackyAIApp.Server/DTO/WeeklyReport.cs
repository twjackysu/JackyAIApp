namespace JackyAIApp.Server.DTO
{
    public class WeeklyReportResponse
    {
        /// <summary>New words added to repository this week.</summary>
        public int NewWordsThisWeek { get; set; }

        /// <summary>Daily challenges completed this week.</summary>
        public int ChallengesCompletedThisWeek { get; set; }

        /// <summary>Total questions answered correctly this week.</summary>
        public int CorrectAnswersThisWeek { get; set; }

        /// <summary>Total questions answered this week.</summary>
        public int TotalAnswersThisWeek { get; set; }

        /// <summary>Words reviewed via spaced repetition this week.</summary>
        public int WordsReviewedThisWeek { get; set; }

        /// <summary>XP earned this week.</summary>
        public int XPEarnedThisWeek { get; set; }

        /// <summary>Current streak.</summary>
        public int CurrentStreak { get; set; }

        /// <summary>User's total XP.</summary>
        public int TotalXP { get; set; }

        /// <summary>User's level.</summary>
        public required string Level { get; set; }

        /// <summary>Percentile rank among all users (0-100).</summary>
        public int Percentile { get; set; }

        /// <summary>Week start date (Monday).</summary>
        public required string WeekStart { get; set; }

        /// <summary>Week end date (Sunday).</summary>
        public required string WeekEnd { get; set; }
    }

    public class LeaderboardEntry
    {
        public int Rank { get; set; }
        public required string DisplayName { get; set; }
        public int TotalXP { get; set; }
        public int CurrentStreak { get; set; }
        public required string Level { get; set; }
        public bool IsCurrentUser { get; set; }
    }

    public class LeaderboardResponse
    {
        public required List<LeaderboardEntry> Entries { get; set; }
        public LeaderboardEntry? CurrentUserEntry { get; set; }
        public int TotalUsers { get; set; }
    }
}
