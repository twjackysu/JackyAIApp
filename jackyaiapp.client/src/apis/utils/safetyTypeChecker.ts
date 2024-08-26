import { SerializedError } from '@reduxjs/toolkit';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';

export function isErrorWithData(
  error: FetchBaseQueryError | SerializedError | undefined,
): error is FetchBaseQueryError {
  if (error == null) return false;
  return 'data' in error;
}
