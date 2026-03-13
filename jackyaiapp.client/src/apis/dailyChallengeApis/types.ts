export type QuestionType =
  | 'VocabularyDefinition'
  | 'FillInTheBlank'
  | 'Synonym'
  | 'Antonym'
  | 'Translation';

export interface DailyChallengeQuestion {
  id: number;
  type: QuestionType;
  prompt: string;
  options: string[];
  correctIndex: number;
  explanation: string;
}

export interface DailyChallengeResponse {
  questions: DailyChallengeQuestion[];
  challengeDate: string;
  alreadyCompleted: boolean;
  previousScore: number | null;
}

export interface DailyChallengeAnswer {
  questionId: number;
  selectedIndex: number;
}

export interface DailyChallengeSubmitRequest {
  answers: DailyChallengeAnswer[];
}

export interface DailyChallengeSubmitResponse {
  score: number;
  totalQuestions: number;
  xpEarned: number;
  streakUpdated: boolean;
  newStreak: number;
  alreadyCompleted: boolean;
  creditsAwarded: number;
}

export interface DailyChallengeStatsResponse {
  currentStreak: number;
  longestStreak: number;
  totalXP: number;
  level: string;
  todayCompleted: boolean;
  todayScore: number | null;
}
