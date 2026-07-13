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
  /** Grammar correction for this turn, if the AI flagged one on the response that followed it. */
  correction?: ConversationCorrection;
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

export interface ConversationSummaryRequest {
  conversationContext: ConversationContext;
  conversationHistory: ConversationTurn[];
}

export interface ConversationSummaryResponse {
  correctionsReview: string[];
  betterPhrasings: string[];
  suggestedWords: string[];
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

export type ExamType = 'Cloze' | 'Translation' | 'Sentence' | 'Listening' | 'ListeningResponse';

export interface RecordExamResultRequest {
  /** Required for word-based exam types; omitted for "Listening"/"ListeningResponse". */
  wordId?: string;
  /** Required when examType is "Listening"; omitted otherwise. */
  listeningTestId?: string;
  /** Required when examType is "ListeningResponse"; omitted otherwise. */
  listeningResponseTestId?: string;
  examType: ExamType;
  correct: boolean;
  /** Optional 0–100 score for graded types (Sentence). */
  score?: number;
}

export interface ListeningTestOption {
  id: string;
  text: string;
  isCorrect: boolean;
  explanation: string;
}

/** TOEIC Part 1 style "look at the picture, pick the best description" question.
 * isCorrect/explanation are included up front; the UI gates their display until answered. */
export interface ListeningTestResponse {
  id: string;
  imageUrl: string;
  options: ListeningTestOption[];
}

/** Admin-only paginated view of the listening question bank. */
export interface ListeningBankPageResponse {
  items: ListeningTestResponse[];
  totalCount: number;
}

export interface ListeningResponseOption {
  id: string;
  text: string;
  isCorrect: boolean;
  explanation: string;
}

/** TOEIC Part 2 style "hear a question/statement, pick the best spoken response" question.
 * isCorrect/explanation are included up front; the UI gates their display until answered. */
export interface ListeningResponseTestResponse {
  id: string;
  prompt: string;
  options: ListeningResponseOption[];
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
