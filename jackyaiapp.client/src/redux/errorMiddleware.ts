import { isRejectedWithValue, Middleware, MiddlewareAPI } from '@reduxjs/toolkit';

import { ErrorCodes } from '../apis/types';

import { openCreditModal } from './uiSlice';

export const rtkQueryErrorLogger: Middleware = (api: MiddlewareAPI) => (next) => (action) => {
  if (isRejectedWithValue(action)) {
    const payload = action.payload as {
      status?: number;
      data?: { error?: { code?: number; message?: string } };
    };

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
