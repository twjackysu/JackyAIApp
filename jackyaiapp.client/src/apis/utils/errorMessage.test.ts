import { describe, expect, it } from 'vitest';

import {
  FALLBACK_ERROR_MESSAGE,
  NETWORK_ERROR_MESSAGE,
  getApiErrorMessage,
  getCaughtErrorMessage,
} from './errorMessage';

describe('getApiErrorMessage', () => {
  it('reads the API standard { error: { code, message } } envelope', () => {
    expect(
      getApiErrorMessage({
        status: 403,
        data: { error: { code: 1040300, message: 'Insufficient credits. Required: 2.' } },
      }),
    ).toBe('Insufficient credits. Required: 2.');
  });

  it('falls back when the JSON body is in some other shape', () => {
    // What an unhandled server exception currently serialises to: the envelope is nested
    // one level down, so `data.error` is absent.
    expect(
      getApiErrorMessage({
        status: 500,
        data: { StatusCode: 500, Value: { error: { code: 1050000, message: 'Boom' } } },
      }),
    ).toBe(FALLBACK_ERROR_MESSAGE);
  });

  it('shows a short plain-text body', () => {
    expect(getApiErrorMessage({ status: 401, data: 'User ID not found in claims' })).toBe(
      'User ID not found in claims',
    );
  });

  it('ignores an HTML body from a gateway or proxy', () => {
    expect(
      getApiErrorMessage({
        status: 'PARSING_ERROR',
        originalStatus: 502,
        data: '<html><body>502 Bad Gateway</body></html>',
        error: 'Unexpected token <',
      }),
    ).toBe(FALLBACK_ERROR_MESSAGE);
  });

  it('ignores a plain-text body long enough to be a dump', () => {
    expect(getApiErrorMessage({ status: 500, data: 'x'.repeat(201) })).toBe(FALLBACK_ERROR_MESSAGE);
  });

  it('names the network as the problem when the request never landed', () => {
    expect(getApiErrorMessage({ status: 'FETCH_ERROR', error: 'TypeError: Failed to fetch' })).toBe(
      NETWORK_ERROR_MESSAGE,
    );
  });

  it('falls back for a SerializedError and for no error at all', () => {
    expect(getApiErrorMessage({ name: 'Error', message: 'boom' })).toBe(FALLBACK_ERROR_MESSAGE);
    expect(getApiErrorMessage(undefined)).toBe(FALLBACK_ERROR_MESSAGE);
  });

  it('falls back rather than throwing on a null or empty body', () => {
    expect(getApiErrorMessage({ status: 204, data: null })).toBe(FALLBACK_ERROR_MESSAGE);
    expect(getApiErrorMessage({ status: 500, data: { error: null } })).toBe(FALLBACK_ERROR_MESSAGE);
    expect(getApiErrorMessage({ status: 500, data: { error: { message: '   ' } } })).toBe(
      FALLBACK_ERROR_MESSAGE,
    );
  });
});

describe('getCaughtErrorMessage', () => {
  it('reads a rejected unwrap() the same way as the typed version', () => {
    expect(
      getCaughtErrorMessage({ status: 400, data: 'Invalid provider name. Supported: Google' }),
    ).toBe('Invalid provider name. Supported: Google');
  });

  it('falls back for a value that is not an error object at all', () => {
    expect(getCaughtErrorMessage(undefined)).toBe(FALLBACK_ERROR_MESSAGE);
    expect(getCaughtErrorMessage('boom')).toBe(FALLBACK_ERROR_MESSAGE);
  });
});
