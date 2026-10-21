import { describe, expect, it, vi } from 'vitest';

import { rtkQueryErrorLogger } from './errorMiddleware';
import { openUsageLimitModal } from './uiSlice';

/** Minimal stand-in for the `{ type, payload, meta }` shape RTK Query's internal thunk produces
 * on a rejected call with `rejectWithValue` — the only shape `isRejectedWithValue` recognizes. */
function rejectedWithValueAction(payload: unknown, meta: Record<string, unknown> = {}) {
  return {
    type: 'api/executeQuery/rejected',
    payload,
    meta: {
      requestId: 'test-request-id',
      requestStatus: 'rejected',
      rejectedWithValue: true,
      ...meta,
    },
  };
}

function runMiddleware(action: unknown) {
  const dispatch = vi.fn();
  const next = vi.fn((a: unknown) => a);
  const api = { dispatch, getState: vi.fn() };
  rtkQueryErrorLogger(api as never)(next)(action);
  return { dispatch, next };
}

describe('rtkQueryErrorLogger — usage-limit dialog', () => {
  it('opens the usage-limit modal on a 429 carrying the UsageLimitExceeded error code', () => {
    const action = rejectedWithValueAction({
      status: 429,
      data: { error: { code: 'UsageLimitExceeded', message: 'Usage limit exceeded.' } },
    });

    const { dispatch, next } = runMiddleware(action);

    expect(dispatch).toHaveBeenCalledWith(openUsageLimitModal());
    expect(next).toHaveBeenCalledWith(action);
  });

  it('does not open the modal for a 429 without the usage-limit error code', () => {
    const action = rejectedWithValueAction({
      status: 429,
      data: { error: { code: 'InternalServerError', message: 'Some other error.' } },
    });

    const { dispatch } = runMiddleware(action);

    expect(dispatch).not.toHaveBeenCalled();
  });

  it('does not open the modal for a non-429 error, even with a matching code', () => {
    const action = rejectedWithValueAction({
      status: 403,
      data: { error: { code: 'UsageLimitExceeded', message: 'Usage limit exceeded.' } },
    });

    const { dispatch } = runMiddleware(action);

    expect(dispatch).not.toHaveBeenCalled();
  });

  it('judges the error by code alone, never by parsing the message text', () => {
    const action = rejectedWithValueAction({
      status: 429,
      // No `error.code` at all — a message mentioning the same concept must not trigger it.
      data: { error: { message: 'You have exceeded your usage limit.' } },
    });

    const { dispatch } = runMiddleware(action);

    expect(dispatch).not.toHaveBeenCalled();
  });
});
