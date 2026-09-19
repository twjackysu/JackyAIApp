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
  /** Delivery/fluency feedback for this turn, if it was recorded by voice and the clip was long enough to measure. */
  deliveryFeedback?: DeliveryFeedback;
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

/**
 * Rule-based delivery/fluency feedback (pace, pauses, filler words) computed from the Whisper
 * transcription call, at zero added AI/API cost. This is delivery/fluency feedback, not
 * phoneme-level pronunciation-accuracy scoring.
 */
export interface DeliveryFeedback {
  paceWpm: number;
  pauseCount: number;
  fillerWordCount: number;
  comments: string[];
}

export interface WhisperTranscriptionResponse {
  text: string;
  /** Omitted when the clip is too short/silent to derive meaningful metrics from. */
  deliveryFeedback?: DeliveryFeedback;
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

export type ExamType =
  | 'Cloze'
  | 'Translation'
  | 'Sentence'
  | 'Listening'
  | 'ListeningResponse'
  | 'Reading'
  | 'Writing'
  | 'Dictation';

export interface RecordExamResultRequest {
  /** Required for word-based exam types; omitted for "Listening"/"ListeningResponse"/"Reading"/"Writing". */
  wordId?: string;
  /** Required when examType is "Listening"; omitted otherwise. */
  listeningTestId?: string;
  /** Required when examType is "ListeningResponse"; omitted otherwise. */
  listeningResponseTestId?: string;
  /** Required when examType is "Reading"; omitted otherwise. */
  readingPassageId?: string;
  /**
   * UserWord ids the writing prompt was built around, for the SM-2 fan-out. Required when
   * examType is "Writing" and the prompt actually had required words; a zero-required-words
   * open-topic submission legitimately omits/empties this.
   */
  writingWordIds?: string[];
  examType: ExamType;
  correct: boolean;
  /** Optional 0–100 score for graded types (Sentence, Writing). */
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

export interface ReadingQuestionOption {
  id: string;
  text: string;
  isCorrect: boolean;
  explanation: string;
}

export interface ReadingQuestion {
  id: string;
  questionText: string;
  options: ReadingQuestionOption[];
}

/** Passage-level reading comprehension exercise: a short passage plus 3–4 multiple-choice
 * comprehension questions. isCorrect/explanation are included up front per option, same
 * UI-gating convention as ListeningResponseTestResponse. */
export interface ReadingPassageResponse {
  id: string;
  title: string;
  passageText: string;
  questions: ReadingQuestion[];
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

export interface WritingPromptWordDto {
  wordId: string;
  wordText: string;
}

/** Open-ended writing prompt: a topic plus up to 5 of the user's own saved words to work into
 * a paragraph. `requiredWords` is empty for a user with no saved words — open-topic writing,
 * not an error. Free to fetch; the AI cost sits entirely on evaluateWriting. */
export interface WritingPromptResponse {
  topic: string;
  requiredWords: WritingPromptWordDto[];
}

/** Echoes topic/requiredWords back from the GET response rather than re-querying, same pattern
 * as TranslationTestUserResponse.examinationQuestion. */
export interface WritingSubmissionRequest {
  topic: string;
  requiredWords: string[];
  submission: string;
}

export interface WritingWordUsageDto {
  word: string;
  used: boolean;
}

export interface WritingGradingResponse {
  score: number;
  grammarFeedback: string;
  /** Topic relevance and structure/coherence. */
  contentFeedback: string;
  /** Overall comment on how the required words were used (or not). */
  vocabularyFeedback: string;
  wordsUsed: WritingWordUsageDto[];
  suggestions: string[];
}

/** Dictation prompt: identifies which word to dictate but deliberately omits the word text —
 * unlike Cloze/Translation's GET response, exposing it here would make the exercise (identify
 * the word from audio alone) trivially bypassable via the Network tab. */
export interface DictationPromptResponse {
  wordId: string;
}

/** User's dictation submission: the word they typed after listening to the audio. */
export interface DictationGradeRequest {
  wordId: string;
  userInput: string;
}

/** Dictation grading result. isCorrect is a trimmed, case-insensitive exact match only; score is
 * a purely informational closeness measure and does not affect isCorrect. correctText reveals
 * the answer for the post-submission UI. */
export interface DictationGradeResponse {
  correctText: string;
  isCorrect: boolean;
  score: number;
}
