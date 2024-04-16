export enum ErrorCodes {
  InternalServerError = 1050000,
  NotFound = 1040400,
  BadRequest = 1040000,
  OpenAIIsNotResponding = 1040001,
  OpenAIResponseUnsuccessful = 1040002,
  UnarseableFormat = 1040003,
  TheWordCannotBeFound = 1040004,
  Unauthorized = 1040100,
  Forbidden = 1040300,
}

export interface ApiOkResponse<T> {
  Data: T;
}

export interface ApiErrorResponse {
  Error: ErrorDetails;
}

export interface ErrorDetails {
  Code: ErrorCodes;
  Message: string;
}
