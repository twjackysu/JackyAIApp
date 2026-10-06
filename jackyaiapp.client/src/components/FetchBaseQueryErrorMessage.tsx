import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import { SerializedError } from '@reduxjs/toolkit';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';

import { getApiErrorMessage } from '@/apis/utils/errorMessage';

interface Props {
  error: FetchBaseQueryError | SerializedError | undefined;
  /** When given, a retry button is shown under the message. */
  onRetry?: () => void;
}

function FetchBaseQueryErrorMessage({ error, onRetry }: Props) {
  return (
    <>
      <Typography role="alert">{getApiErrorMessage(error)}</Typography>
      {onRetry && (
        <Button variant="outlined" onClick={onRetry} sx={{ mt: 1 }}>
          重試
        </Button>
      )}
    </>
  );
}

export default FetchBaseQueryErrorMessage;
