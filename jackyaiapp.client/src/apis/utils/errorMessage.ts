import { SerializedError } from '@reduxjs/toolkit';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';

/** Shown when nothing in the error is fit to put in front of a user. */
export const FALLBACK_ERROR_MESSAGE = 'Something wrong';

/** The browser could not reach the API at all — worth saying plainly instead of falling back. */
export const NETWORK_ERROR_MESSAGE =
  'Cannot reach the server. Check your connection and try again.';

/** A body longer than this is a page or a stack trace, not a message. */
const MAX_PLAIN_TEXT_LENGTH = 200;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function nonBlank(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() !== '' ? value : undefined;
}

/**
 * Extracts a displayable message from whatever RTK Query hands back.
 *
 * `FetchBaseQueryError.data` is typed `unknown` for a reason: it is the API's standard
 * `{ error: { code, message } }` envelope only when the server both returned JSON and got
 * as far as the response factory. A gateway HTML page, a bare text body, or a JSON object
 * in some other shape all land here too, and reading `.error.message` off those throws —
 * which crashed the whole tree, since this runs inside render.
 */
export function getApiErrorMessage(
  error: FetchBaseQueryError | SerializedError | undefined,
): string {
  if (error == null) return FALLBACK_ERROR_MESSAGE;

  if ('status' in error && error.status === 'FETCH_ERROR') return NETWORK_ERROR_MESSAGE;

  if ('data' in error) {
    const { data } = error;

    // The app's standard envelope.
    if (isRecord(data) && isRecord(data.error)) {
      const message = nonBlank(data.error.message);
      if (message) return message;
    }

    // A plain-text body, e.g. an ASP.NET `Unauthorized("...")`. Markup is a page, not a
    // message, and anything long enough to be a dump belongs in the console, not the UI.
    const text = nonBlank(data)?.trim();
    if (text && !text.startsWith('<') && text.length <= MAX_PLAIN_TEXT_LENGTH) return text;
  }

  return FALLBACK_ERROR_MESSAGE;
}
