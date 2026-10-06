import CheckIcon from '@mui/icons-material/Check';
import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Snackbar from '@mui/material/Snackbar';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { WrongWord } from '../hooks/useExamRound';
import { useSaveWords } from '../hooks/useSaveWords';

interface RoundResultScreenProps {
  correct: number;
  total: number;
  /** Words answered wrong this round; the next-steps block is hidden when empty. */
  wrongWords?: WrongWord[];
  onRestart: () => void;
}

/** Shown after finishing a fixed-size round of exam questions. Mirrors the Review flow's result screen. */
function RoundResultScreen({ correct, total, wrongWords = [], onRestart }: RoundResultScreenProps) {
  const navigate = useNavigate();
  const percentage = total > 0 ? Math.round((correct / total) * 100) : 0;
  const { statuses, save } = useSaveWords();
  const [failed, setFailed] = useState(false);

  const pending = wrongWords.filter((w) => statuses[w.wordId] !== 'saved');
  const isSaving = wrongWords.some((w) => statuses[w.wordId] === 'saving');

  const handleSaveAll = async () => {
    const results = await Promise.all(pending.map((w) => save(w.wordId)));
    if (results.some((ok) => !ok)) setFailed(true);
  };

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

      {wrongWords.length > 0 && (
        <Box sx={{ mb: 3 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 700, mb: 1 }}>
            本輪答錯的單字
          </Typography>
          <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', justifyContent: 'center', mb: 2 }}>
            {wrongWords.map((w) => (
              <Chip
                key={w.wordId}
                label={w.word}
                variant="outlined"
                color={statuses[w.wordId] === 'saved' ? 'success' : 'default'}
                icon={statuses[w.wordId] === 'saved' ? <CheckIcon /> : undefined}
              />
            ))}
          </Box>
          <Stack
            direction="row"
            spacing={2}
            useFlexGap
            sx={{ justifyContent: 'center', flexWrap: 'wrap' }}
          >
            <Button
              variant="outlined"
              onClick={handleSaveAll}
              disabled={isSaving || pending.length === 0}
            >
              {pending.length === 0 ? '已全部加入單字庫' : '全部加入單字庫'}
            </Button>
            <Button
              variant="contained"
              color="secondary"
              onClick={() => navigate('/english/review')}
            >
              前往複習
            </Button>
          </Stack>
        </Box>
      )}

      <Stack direction="row" spacing={2} sx={{ justifyContent: 'center' }}>
        <Button variant="contained" onClick={onRestart}>
          再來一輪
        </Button>
        <Button variant="outlined" onClick={() => navigate('/english/exam')}>
          返回選單
        </Button>
      </Stack>

      <Snackbar open={failed} autoHideDuration={4000} onClose={() => setFailed(false)}>
        <Alert severity="error" onClose={() => setFailed(false)}>
          部分單字加入失敗，請稍後再試
        </Alert>
      </Snackbar>
    </Paper>
  );
}

export default RoundResultScreen;
