export interface Word extends WordBase {
  id: string;
  partitionKey: string;
  dateAdded: string;
  lastUpdated: string;
  dataInvalid?: boolean;
}

interface WordBase {
  word: string;
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
