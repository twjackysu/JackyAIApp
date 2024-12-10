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
