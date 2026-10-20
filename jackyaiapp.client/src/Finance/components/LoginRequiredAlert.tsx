import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';

interface LoginRequiredAlertProps {
  message: string;
}

/** Info banner for a tab that needs a signed-in user; its button signs in and comes back here. */
export const LoginRequiredAlert = ({ message }: LoginRequiredAlertProps) => (
  <Alert
    severity="info"
    sx={{ mb: 2 }}
    action={
      <Button
        color="inherit"
        size="small"
        onClick={() => {
          window.location.href = `/api/account/login/Google?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
        }}
      >
        登入
      </Button>
    }
  >
    {message}
  </Alert>
);
