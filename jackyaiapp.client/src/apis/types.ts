/**
 * The `code` of the API's error envelope. The server serializes its ErrorCodes enum by name
 * (JsonStringEnumConverter in Program.cs), so this is the name, not the number.
 */
export enum ErrorCodes {
  InternalServerError = 'InternalServerError',
  NotFound = 'NotFound',
  BadRequest = 'BadRequest',
  QueryOpenAIFailed = 'QueryOpenAIFailed',
  OpenAIResponseUnsuccessful = 'OpenAIResponseUnsuccessful',
  UnparseableFormat = 'UnparseableFormat',
  TheWordCannotBeFound = 'TheWordCannotBeFound',
  AttachmentUnreadable = 'AttachmentUnreadable',
  AttachmentTooManyPages = 'AttachmentTooManyPages',
  Unauthorized = 'Unauthorized',
  Forbidden = 'Forbidden',
  PayloadTooLarge = 'PayloadTooLarge',
  AttachmentUnsupportedType = 'AttachmentUnsupportedType',
  UsageLimitExceeded = 'UsageLimitExceeded',
}

export interface ApiOkResponse<T> {
  data: T;
}

export interface ApiErrorResponse {
  error: ErrorDetails;
}

export interface ErrorDetails {
  code: ErrorCodes;
  message: string;
}
