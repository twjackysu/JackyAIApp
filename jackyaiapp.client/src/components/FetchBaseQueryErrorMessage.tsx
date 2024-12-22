import { ApiErrorResponse } from '@/apis/types';
import { isErrorWithData } from '@/apis/utils/safetyTypeChecker';
import Typography from '@mui/material/Typography';
import { SerializedError } from '@reduxjs/toolkit';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';

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
