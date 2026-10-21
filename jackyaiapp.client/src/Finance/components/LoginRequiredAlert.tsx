import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';

import { signInAndComeBack } from '../utils/signIn';

interface LoginRequiredAlertProps {
  message: string;
}

/** Info banner for a tab that needs a signed-in user; its button signs in and comes back here. */
export const LoginRequiredAlert = ({ message }: LoginRequiredAlertProps) => (
  <Alert
    severity="info"
    sx={{ mb: 2 }}
    action={
      <Button color="inherit" size="small" onClick={signInAndComeBack}>
        登入
      </Button>
    }
  >
    {message}
  </Alert>
);
