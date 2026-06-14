export interface PersonalWord {
  id: string;
  partitionKey: string;
  wordId: string;
  userId: string;
  creationDate: Date;
}

export interface GetRepositoryWordRequest {
  pageNumber: number;
  pageSize: number;
}

export interface WordPack {
  id: string;
  name: string;
  description: string;
  wordCount: number;
}

export interface AddWordPackResult {
  added: number;
  alreadyHad: number;
  failed: number;
}
