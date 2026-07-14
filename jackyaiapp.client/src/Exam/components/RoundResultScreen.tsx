import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useNavigate } from 'react-router-dom';

interface RoundResultScreenProps {
  correct: number;
  total: number;
  onRestart: () => void;
}

/** Shown after finishing a fixed-size round of exam questions. Mirrors the Review flow's result screen. */
function RoundResultScreen({ correct, total, onRestart }: RoundResultScreenProps) {
  const navigate = useNavigate();
  const percentage = total > 0 ? Math.round((correct / total) * 100) : 0;

  return (
    <Paper sx={{ maxWidth: 500, mx: 'auto', mt: 4, p: 4, borderRadius: 3, textAlign: 'center' }}>
      <Typography variant="h1" sx={{ mb: 1 }}>
        <EmojiEventsIcon sx={{ fontSize: 56, color: '#FFD700' }} />
      </Typography>
      <Typography variant="h5" sx={{ fontWeight: 'bold', mb: 2 }}>
        這輪完成了！
      </Typography>
      <Typography variant="h3" color="primary.main" sx={{ fontWeight: 'bold', mb: 1 }}>
        {correct} / {total}
      </Typography>
      <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
        正確率 {percentage}%
      </Typography>
      <Stack direction="row" spacing={2} sx={{ justifyContent: 'center' }}>
        <Button variant="contained" onClick={onRestart}>
          再來一輪
        </Button>
        <Button variant="outlined" onClick={() => navigate('/english/exam')}>
          返回選單
        </Button>
      </Stack>
    </Paper>
  );
}

export default RoundResultScreen;
