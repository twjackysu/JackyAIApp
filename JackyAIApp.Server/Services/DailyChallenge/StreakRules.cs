using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.DailyChallenge
{
    /// <summary>What completing a day did to the streak.</summary>
    public readonly record struct StreakChange(bool Updated, bool FreezeUsed);

    /// <summary>
    /// The daily-challenge streak rules, kept in one place so that completing a day
    /// (<see cref="RecordCompletion"/>) and showing the streak (stats, weekly report, leaderboard:
    /// <see cref="DisplayStreak(User, DateTime)"/>) cannot drift apart. A streak shown as alive is one
    /// that completing today would continue.
    ///
    /// All dates are UTC calendar dates, like the challenge itself.
    /// </summary>
    public static class StreakRules
    {
        /// <summary>Freezes granted at the start of each week. One freeze forgives one missed day.</summary>
        public const int WeeklyFreezes = 1;

        /// <summary>Monday-based start of the week containing <paramref name="date"/>.</summary>
        public static DateTime WeekStart(DateTime date)
        {
            var d = date.Date;
            int daysSinceMonday = ((int)d.DayOfWeek + 6) % 7;
            return d.AddDays(-daysSinceMonday);
        }

        /// <summary>
        /// Freezes the user can spend today. The weekly grant is only written on the next completion,
        /// so a stored count from an earlier week is out of date: this week's grant is what counts.
        /// </summary>
        public static int FreezesAvailable(int storedFreezes, DateTime? lastFreezeGrantDate, DateTime today) =>
            IsNewFreezeWeek(lastFreezeGrantDate, today) ? WeeklyFreezes : storedFreezes;

        public static int FreezesAvailable(User user, DateTime today) =>
            FreezesAvailable(user.StreakFreezesAvailable, user.LastFreezeGrantDate, today);

        /// <summary>
        /// The streak as it stands today: unchanged while completing today would still continue it
        /// (last completed today or yesterday, or exactly one day missed with a freeze to spend),
        /// otherwise 0.
        /// </summary>
        public static int DisplayStreak(
            int currentStreak, DateTime? lastStreakDate, int storedFreezes, DateTime? lastFreezeGrantDate, DateTime today)
        {
            if (lastStreakDate == null) return currentStreak;

            int gap = (today.Date - lastStreakDate.Value.Date).Days;
            bool alive = gap <= 1
                || (gap == 2 && FreezesAvailable(storedFreezes, lastFreezeGrantDate, today) > 0);
            return alive ? currentStreak : 0;
        }

        public static int DisplayStreak(User user, DateTime today) =>
            DisplayStreak(user.CurrentStreak, user.LastStreakDate, user.StreakFreezesAvailable, user.LastFreezeGrantDate, today);

        /// <summary>
        /// Applies a completed challenge on <paramref name="today"/> to <paramref name="user"/>: grants
        /// the week's freeze if it has not been granted yet, then continues the streak (spending a
        /// freeze over a single missed day) or restarts it at 1. Completing the same day again changes
        /// nothing.
        /// </summary>
        public static StreakChange RecordCompletion(User user, DateTime today)
        {
            today = today.Date;
            if (IsNewFreezeWeek(user.LastFreezeGrantDate, today))
            {
                user.StreakFreezesAvailable = WeeklyFreezes;
                user.LastFreezeGrantDate = today;
            }

            bool freezeUsed = false;
            if (user.LastStreakDate == null)
            {
                user.CurrentStreak = 1;
            }
            else
            {
                int gap = (today - user.LastStreakDate.Value.Date).Days;
                if (gap <= 0)
                    return new StreakChange(Updated: false, FreezeUsed: false);

                if (gap == 1)
                {
                    user.CurrentStreak++;
                }
                else if (gap == 2 && user.StreakFreezesAvailable > 0)
                {
                    user.StreakFreezesAvailable--;
                    user.CurrentStreak++;
                    freezeUsed = true;
                }
                else
                {
                    user.CurrentStreak = 1;
                }
            }

            user.LastStreakDate = today;
            if (user.CurrentStreak > user.LongestStreak)
                user.LongestStreak = user.CurrentStreak;

            return new StreakChange(Updated: true, FreezeUsed: freezeUsed);
        }

        private static bool IsNewFreezeWeek(DateTime? lastFreezeGrantDate, DateTime today) =>
            lastFreezeGrantDate == null || WeekStart(lastFreezeGrantDate.Value) != WeekStart(today);
    }
}
