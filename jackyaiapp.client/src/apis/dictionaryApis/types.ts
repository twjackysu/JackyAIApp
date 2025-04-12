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
}

export interface TranslationTestResponse {
  word: string;
  chinese: string;
  english: string;
}

export interface TranslationTestUserResponse {
  unfamiliarWords: string;
  examinationQuestion: string;
  translation: string;
}

export interface TranslationQualityGradingAssistantResponse {
  translationQualityGrading: string;
}
