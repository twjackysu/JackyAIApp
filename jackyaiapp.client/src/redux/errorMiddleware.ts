import { isRejectedWithValue, Middleware, MiddlewareAPI } from '@reduxjs/toolkit';

import { ErrorCodes } from '../apis/types';

import { openUsageLimitModal } from './uiSlice';

// Endpoints that require login — a 401 from these means the session is gone, so send the
// user to sign in (with a ReturnUrl so they come back). A blanket 401 redirect is unsafe
// because the userInfo probe 401s for anonymous visitors browsing the public demo.
const LOGIN_REQUIRED_ENDPOINTS = new Set(['nlSummarize']);

export const rtkQueryErrorLogger: Middleware = (api: MiddlewareAPI) => (next) => (action) => {
  if (isRejectedWithValue(action)) {
    const payload = action.payload as {
      status?: number;
      data?: { error?: { code?: string; message?: string } };
    };

    // 401 on a login-required endpoint → redirect to sign in
    const endpointName = (action.meta as { arg?: { endpointName?: string } } | undefined)?.arg
      ?.endpointName;
    if (payload?.status === 401 && endpointName && LOGIN_REQUIRED_ENDPOINTS.has(endpointName)) {
      window.location.href = `/api/account/login/Google?ReturnUrl=${encodeURIComponent(
        window.location.pathname,
      )}`;
      return next(action);
    }

    // 429 Too Many Requests with the usage-limit error code → open the usage-limit dialog.
    // Judged on `error.code` alone, never by parsing `message` text.
    if (payload?.status === 429 && payload?.data?.error?.code === ErrorCodes.UsageLimitExceeded) {
      api.dispatch(openUsageLimitModal());
    }
  }

  return next(action);
};
