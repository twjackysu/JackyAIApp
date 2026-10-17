import { FALLBACK_ERROR_MESSAGE, getCaughtErrorMessage } from '@/apis/utils/errorMessage';

export const UPLOAD_FAILED_MESSAGE = '上傳失敗 — 請再試一次。';
export const UPLOAD_TOO_LARGE_MESSAGE = '資料太大，請減少列數或欄數後再試。';

/** Statuses whose message the server writes for the user: the data's shape or size (400/413), or load (429). */
const USER_FACING_STATUSES = new Set([400, 413, 429]);

/**
 * What the upload dialog shows when a session upload is refused. The server explains limits and
 * "try again later" in the error envelope; anything else (a 500, a network error) stays generic.
 * A 413 straight from the web server has no envelope, so it gets its own message.
 */
export function uploadErrorMessage(error: unknown): string {
  const status =
    typeof error === 'object' && error !== null && 'status' in error ? error.status : undefined;
  if (typeof status !== 'number' || !USER_FACING_STATUSES.has(status)) return UPLOAD_FAILED_MESSAGE;

  const message = getCaughtErrorMessage(error);
  if (message !== FALLBACK_ERROR_MESSAGE) return message;
  return status === 413 ? UPLOAD_TOO_LARGE_MESSAGE : UPLOAD_FAILED_MESSAGE;
}
