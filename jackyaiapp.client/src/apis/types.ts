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
  AttachmentImageTooLarge = 'AttachmentImageTooLarge',
  Unauthorized = 'Unauthorized',
  Forbidden = 'Forbidden',
  /** An MCP App's tool for it alone that runs once the reader allows it on the card (D-207). */
  AppCallNeedsReader = 'AppCallNeedsReader',
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
