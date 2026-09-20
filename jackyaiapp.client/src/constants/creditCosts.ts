/**
 * Client-side mirror of JackyAIApp.Server/Common/CreditCosts.cs
 * Backend is the source of truth — keep in sync when backend values change.
 *
 * Every key here must name an existing backend constant (SCREAMING_SNAKE_CASE of its PascalCase
 * name) and carry the same value — creditCosts.test.ts reads the .cs file and fails the build
 * otherwise, so a price the UI shows can no longer silently disagree with the one actually charged.
 * A feature the backend charges for but the UI never labels is exactly the drift that check cannot
 * see, so add the entry here and show it wherever the charged action is triggered.
 */
export const CREDIT_COSTS = {
  /** Cloze test generation */
  CLOZE_TEST: 2,
  /** Translation test generation */
  TRANSLATION_TEST: 2,
  /** Translation quality grading */
  TRANSLATION_GRADING: 1,
  /** Conversation test (start) */
  CONVERSATION_START: 3,
  /** Conversation response (each turn) */
  CONVERSATION_RESPONSE: 2,
  /** Conversation end-of-session summary */
  CONVERSATION_SUMMARY: 2,
  /** Sentence test generation */
  SENTENCE_TEST: 2,
  /** Sentence AI evaluation/grading */
  SENTENCE_EVALUATION: 2,
  /** Listening test (TOEIC Part 1 style) — bundles the question + its audio playback */
  LISTENING_TEST: 4,
  /** Listening response test (TOEIC Part 2 style) — bundles the prompt + 3 response options' audio */
  LISTENING_RESPONSE_TEST: 4,
  /** Reading comprehension test — flat charge per passage play, cache hit or miss */
  READING_COMPREHENSION_TEST: 3,
  /** Writing practice AI evaluation — the prompt fetch itself is free, only grading is charged */
  WRITING_EVALUATION: 3,
  /** Dictation practice — flat charge covering the one TTS clip per attempt */
  DICTATION_TEST: 1,
  /** Whisper audio transcription */
  AUDIO_TRANSCRIPTION: 1,
  /** Stock analysis (AI-powered) */
  STOCK_ANALYSIS: 5,
  /** Daily AI market digest — charged on the day's first view, re-reads that day are free */
  MARKET_DIGEST: 3,
  /** Portfolio AI technical-analysis summary for a single holding */
  PORTFOLIO_AI_SUMMARY: 2,
  /** Floating AI chatbot — charged per message sent */
  CHAT_MESSAGE: 5,
  /** Text-to-speech synthesis */
  TEXT_TO_SPEECH: 1,
  /** Natural-language summarize (LLM translates query to parameters + executes) */
  NL_SUMMARIZE: 2,
} as const;
