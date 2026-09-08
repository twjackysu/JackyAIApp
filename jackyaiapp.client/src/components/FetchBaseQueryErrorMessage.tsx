import Typography from '@mui/material/Typography';
import { SerializedError } from '@reduxjs/toolkit';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';

import { getApiErrorMessage } from '@/apis/utils/errorMessage';

interface Props {
  error: FetchBaseQueryError | SerializedError | undefined;
}

function FetchBaseQueryErrorMessage({ error }: Props) {
  return <Typography>{getApiErrorMessage(error)}</Typography>;
}

export default FetchBaseQueryErrorMessage;
