import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import { useLocation } from 'react-router-dom';

interface Props {
  title: string;
  description: string;
  bullets?: string[];
}

/** Guest-facing explainer shown instead of an instant redirect to Google, so visitors see
 * what the page offers (and that it's free) before being asked to sign in. */
function LoginGate({ title, description, bullets = [] }: Props) {
  const location = useLocation();

  return (
    <Box sx={{ maxWidth: 560, mx: 'auto', py: { xs: 4, md: 8 }, px: 2, textAlign: 'center' }}>
      <Typography variant="h4" component="h1" sx={{ fontWeight: 700, mb: 1.5 }}>
        {title}
      </Typography>
      <Typography variant="body1" color="text.secondary" sx={{ mb: 2 }}>
        {description}
      </Typography>
      {bullets.length > 0 && (
        <Box component="ul" sx={{ textAlign: 'left', display: 'inline-block', m: 0, mb: 2, pl: 3 }}>
          {bullets.map((b) => (
            <Typography key={b} component="li" variant="body1">
              {b}
            </Typography>
          ))}
        </Box>
      )}
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
        完全免費。登入是為了儲存你的單字與學習進度。
      </Typography>
      <Button
        variant="contained"
        href={`/api/account/login/Google?ReturnUrl=${encodeURIComponent(location.pathname)}`}
      >
        使用 Google 登入
      </Button>
    </Box>
  );
}

export default LoginGate;
