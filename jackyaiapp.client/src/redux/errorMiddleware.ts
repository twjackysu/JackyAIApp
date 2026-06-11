import { isRejectedWithValue, Middleware, MiddlewareAPI } from '@reduxjs/toolkit';

import { ErrorCodes } from '../apis/types';

import { openCreditModal } from './uiSlice';

// Endpoints that require login — a 401 from these means the session is gone, so send the
// user to sign in (with a ReturnUrl so they come back). A blanket 401 redirect is unsafe
// because the userInfo probe 401s for anonymous visitors browsing the public demo.
const LOGIN_REQUIRED_ENDPOINTS = new Set(['nlSummarize']);

export const rtkQueryErrorLogger: Middleware = (api: MiddlewareAPI) => (next) => (action) => {
  if (isRejectedWithValue(action)) {
    const payload = action.payload as {
      status?: number;
      data?: { error?: { code?: number; message?: string } };
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

    // Check if it's a 403 Forbidden with Insufficient Credits error code
    if (
      payload?.status === 403 &&
      payload?.data?.error?.code === ErrorCodes.Forbidden &&
      (payload?.data?.error?.message as string)?.includes('Insufficient credits')
    ) {
      const errorMessage = payload.data.error.message as string;

      // Determine required credits by parsing the message or based on the endpoint name
      // E.g. "Insufficient credits. Required: 2. Please purchase more credits to continue."
      let requiredCredits: number | undefined;
      const match = errorMessage.match(/Required:\s*(\d+)/i);
      if (match && match[1]) {
        requiredCredits = parseInt(match[1], 10);
      }

      api.dispatch(
        openCreditModal({
          message: errorMessage,
          requiredCredits,
        }),
      );
    }
  }

  return next(action);
};
