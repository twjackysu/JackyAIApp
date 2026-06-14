export interface WeeklyReportResponse {
  newWordsThisWeek: number;
  challengesCompletedThisWeek: number;
  correctAnswersThisWeek: number;
  totalAnswersThisWeek: number;
  wordsReviewedThisWeek: number;
  xpEarnedThisWeek: number;
  currentStreak: number;
  totalXP: number;
  level: string;
  percentile: number;
  weekStart: string;
  weekEnd: string;
}

export interface LeaderboardEntry {
  rank: number;
  displayName: string;
  totalXP: number;
  currentStreak: number;
  level: string;
  isCurrentUser: boolean;
}

export interface LeaderboardResponse {
  entries: LeaderboardEntry[];
  currentUserEntry: LeaderboardEntry | null;
  totalUsers: number;
}

export interface ActivityDay {
  date: string; // yyyy-MM-dd
  count: number;
}

export interface ActivityResponse {
  days: ActivityDay[];
  activeDays: number;
}
