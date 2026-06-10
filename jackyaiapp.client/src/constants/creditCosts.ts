/**
 * Client-side mirror of JackyAIApp.Server/Common/CreditCosts.cs
 * Backend is the source of truth — keep in sync when backend values change.
 */
export const CREDIT_COSTS = {
  /** Dictionary word lookup (AI-generated definitions) */
  DICTIONARY_LOOKUP: 1,
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
  /** Sentence test generation */
  SENTENCE_TEST: 2,
  /** Stock analysis (AI-powered) */
  STOCK_ANALYSIS: 5,
  /** Floating AI chatbot — charged per message sent */
  CHAT_MESSAGE: 5,
  /** Text-to-speech synthesis */
  TEXT_TO_SPEECH: 1,
} as const;
