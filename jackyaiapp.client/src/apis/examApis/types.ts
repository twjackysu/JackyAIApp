export interface ConversationStartRequest {
  scenario: string;
  userRole: string;
  aiRole: string;
  difficultyLevel: number;
}

export interface ConversationStartResponse {
  scenario: string;
  userRole: string;
  aiRole: string;
  context: string;
  firstMessage: string;
}

export interface ConversationContext {
  scenario: string;
  userRole: string;
  aiRole: string;
  turnNumber: number;
}

export interface ConversationTurn {
  speaker: string;
  message: string;
}

export interface ConversationResponseRequest {
  conversationContext: ConversationContext;
  conversationHistory: ConversationTurn[];
  userMessage: string;
}

export interface ConversationCorrection {
  hasCorrection: boolean;
  originalText?: string;
  suggestedText?: string;
  explanation?: string;
}

export interface ConversationResponseResponse {
  aiResponse: string;
  correction: ConversationCorrection;
}

export interface WhisperTranscriptionResponse {
  text: string;
}

export interface SentenceTestResponse {
  word: string;
  wordId: string;
  prompt: string;
  sampleAnswer: string;
  context: string;
  difficultyLevel: number;
  grammarPattern?: string;
}

export type ExamType = 'Cloze' | 'Translation' | 'Sentence';

export interface RecordExamResultRequest {
  wordId: string;
  examType: ExamType;
  correct: boolean;
  /** Optional 0–100 score for graded types (Sentence). */
  score?: number;
}

export interface RecordExamResultResponse {
  scheduleUpdated: boolean;
  nextReviewDate?: string | null;
}

export interface SentenceTestUserResponse {
  word: string;
  prompt: string;
  context: string;
  userSentence: string;
  difficultyLevel: number;
  grammarPattern?: string;
}

export interface SentenceTestGradingResponse {
  score: number;
  grammarFeedback: string;
  usageFeedback: string;
  creativityFeedback: string;
  overallFeedback: string;
  suggestions: string[];
}
