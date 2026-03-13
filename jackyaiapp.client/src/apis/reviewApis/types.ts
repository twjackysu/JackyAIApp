export interface ReviewDefinition {
  english: string;
  chinese: string;
}

export interface ReviewExampleSentence {
  english: string;
  chinese: string;
}

export interface ReviewWordMeaning {
  partOfSpeech: string;
  definitions: ReviewDefinition[];
  exampleSentences: ReviewExampleSentence[];
  synonyms: string[];
  antonyms: string[];
}

export interface ReviewWordItem {
  userWordId: number;
  wordText: string;
  kkPhonics: string;
  meanings: ReviewWordMeaning[];
  reviewCount: number;
  consecutiveCorrect: number;
}

export interface DueReviewsResponse {
  dueWords: ReviewWordItem[];
  totalDueCount: number;
}

export interface ReviewAnswerRequest {
  userWordId: number;
  quality: number; // 0-5 SM-2 quality
}

export interface ReviewSubmitRequest {
  reviews: ReviewAnswerRequest[];
}

export interface ReviewSubmitResponse {
  wordsReviewed: number;
  correctCount: number;
  xpEarned: number;
}

export interface DueCountResponse {
  dueCount: number;
}
