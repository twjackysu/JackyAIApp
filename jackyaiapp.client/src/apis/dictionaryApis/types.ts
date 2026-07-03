export interface Word extends WordBase {
  id: string;
  partitionKey: string;
  dateAdded: string;
  lastUpdated: string;
  dataInvalid?: boolean;
  clozeTests?: ClozeTest[];
  translationTests?: string[];
}

interface WordBase {
  word: string;
  kkPhonics: string;
  meanings: WordMeaning[];
}

interface WordMeaning {
  partOfSpeech: string;
  definitions: Definition[];
  exampleSentences: ExampleSentence[];
  synonyms: string[];
  antonyms: string[];
  relatedWords: string[];
}

interface Definition {
  english: string;
  chinese: string;
}

interface ExampleSentence {
  english: string;
  chinese: string;
}

export interface ClozeTest {
  question: string;
  options: string[];
  answer: string;
  /** Present on exam responses (not on words' embedded cloze tests). */
  wordId?: string;
}

export interface TranslationTestResponse {
  word: string;
  wordId: string;
  chinese: string;
  english: string;
}

export interface TranslationTestUserResponse {
  unfamiliarWords: string;
  examinationQuestion: string;
  translation: string;
}

export interface WordSuggestionsResponse {
  suggestions: string[];
}

export type TranslationGrade = 'A' | 'B' | 'C' | 'D' | 'E';

export interface TranslationQualityGradingAssistantResponse {
  grade: TranslationGrade;
  reason: string;
  correctedTranslation: string;
  wordUsed: boolean;
}
