import { isErrorWithData } from '@/apis/utils/safetyTypeChecker';
import { ApiErrorResponse } from '@/apis/types';
import Typography from '@mui/material/Typography';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';
import { SerializedError } from '@reduxjs/toolkit';

interface Props {
  error: FetchBaseQueryError | SerializedError | undefined;
}

function FetchBaseQueryErrorMessage({ error }: Props) {
  return isErrorWithData(error) ? (
    <Typography>{(error?.data as ApiErrorResponse).error.message}</Typography>
  ) : (
    <Typography>Something wrong</Typography>
  );
}

export default FetchBaseQueryErrorMessage;
