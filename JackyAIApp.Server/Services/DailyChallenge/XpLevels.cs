namespace JackyAIApp.Server.Services.DailyChallenge
{
    /// <summary>The level shown for a user's total XP. Daily-challenge stats and the weekly
    /// report/leaderboard both read it from here.</summary>
    public static class XpLevels
    {
        public static string For(int totalXp) => totalXp switch
        {
            >= 5000 => "Master 👑",
            >= 2000 => "Advanced 🎯",
            >= 500 => "Explorer 🧭",
            >= 100 => "Learner 📖",
            _ => "Beginner 🌱"
        };
    }
}
