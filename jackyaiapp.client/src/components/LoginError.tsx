import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import { Box, Button, Container, Typography } from '@mui/material';

function LoginError() {
  const handleRetry = () => {
    window.location.href = '/api/account/login/Google';
  };

  const handleGoHome = () => {
    window.location.href = '/';
  };

  return (
    <Container maxWidth="sm">
      <Box
        sx={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: '60vh',
          textAlign: 'center',
        }}
      >
        <ErrorOutlineIcon sx={{ fontSize: 80, color: 'error.main', mb: 2 }} />
        <Typography variant="h4" gutterBottom>
          登入失敗
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 4 }}>
          無法完成 Google 登入。請確認您的網路連線，或稍後再試。
        </Typography>
        <Box sx={{ display: 'flex', gap: 2 }}>
          <Button variant="contained" onClick={handleRetry}>
            重試登入
          </Button>
          <Button variant="outlined" onClick={handleGoHome}>
            返回首頁
          </Button>
        </Box>
      </Box>
    </Container>
  );
}

export default LoginError;
