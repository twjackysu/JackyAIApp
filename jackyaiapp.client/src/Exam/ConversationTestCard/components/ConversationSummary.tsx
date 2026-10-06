import AutoFixHighIcon from '@mui/icons-material/AutoFixHigh';
import CheckIcon from '@mui/icons-material/Check';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutlined';
import StyleIcon from '@mui/icons-material/Style';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import Snackbar from '@mui/material/Snackbar';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { useLazyGetWordQuery } from '@/apis/dictionaryApis';
import { ConversationSummaryResponse } from '@/apis/examApis/types';
import { useSaveWords } from '@/Exam/hooks/useSaveWords';

interface ConversationSummaryProps {
  open: boolean;
  summary: ConversationSummaryResponse | null;
  isLoading: boolean;
  onClose: () => void;
  onStartNew: () => void;
}

function ConversationSummary({
  open,
  summary,
  isLoading,
  onClose,
  onStartNew,
}: ConversationSummaryProps) {
  const [getWord] = useLazyGetWordQuery();
  const { save } = useSaveWords();
  const [resolving, setResolving] = useState<Set<string>>(new Set());
  const [saved, setSaved] = useState<Set<string>>(new Set());
  const [toast, setToast] = useState<{ severity: 'success' | 'error'; message: string } | null>(
    null,
  );

  // Suggested words arrive as plain text, so resolve the word id first, then reuse the
  // repository PUT; a word that is already in the repository counts as added.
  const handleAddWord = async (word: string) => {
    setResolving((prev) => new Set(prev).add(word));
    try {
      const wordId = (await getWord(word).unwrap()).data.id;
      if (await save(wordId)) {
        setSaved((prev) => new Set(prev).add(word));
        setToast({ severity: 'success', message: `已將「${word}」加入單字庫` });
      } else {
        setToast({ severity: 'error', message: `「${word}」加入失敗，請稍後再試` });
      }
    } catch {
      setToast({ severity: 'error', message: `「${word}」加入失敗，請稍後再試` });
    } finally {
      setResolving((prev) => {
        const next = new Set(prev);
        next.delete(word);
        return next;
      });
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>對話總結</DialogTitle>
      <DialogContent dividers>
        {isLoading && (
          <Typography variant="body2" color="text.secondary">
            正在整理這次對話的重點...
          </Typography>
        )}
        {!isLoading && summary && (
          <Stack spacing={3}>
            <Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                <AutoFixHighIcon fontSize="small" color="warning" />
                <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
                  文法糾正回顧
                </Typography>
              </Box>
              {summary.correctionsReview.length === 0 ? (
                <Typography variant="body2" color="text.secondary">
                  這次對話沒有明顯的文法錯誤，做得很好！
                </Typography>
              ) : (
                <Stack spacing={1}>
                  {summary.correctionsReview.map((item, i) => (
                    <Typography key={i} variant="body2">
                      • {item}
                    </Typography>
                  ))}
                </Stack>
              )}
            </Box>

            <Divider />

            <Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                <CheckCircleOutlineIcon fontSize="small" color="success" />
                <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
                  更道地的說法
                </Typography>
              </Box>
              {summary.betterPhrasings.length === 0 ? (
                <Typography variant="body2" color="text.secondary">
                  這次對話還太短，累積更多輪次後會有更多建議。
                </Typography>
              ) : (
                <Stack spacing={1}>
                  {summary.betterPhrasings.map((item, i) => (
                    <Typography key={i} variant="body2">
                      • {item}
                    </Typography>
                  ))}
                </Stack>
              )}
            </Box>

            <Divider />

            <Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                <StyleIcon fontSize="small" color="info" />
                <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
                  值得加入單字庫
                </Typography>
              </Box>
              {summary.suggestedWords.length === 0 ? (
                <Typography variant="body2" color="text.secondary">
                  沒有特別的單字建議。
                </Typography>
              ) : (
                <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                  {summary.suggestedWords.map((word) => (
                    <Chip
                      key={word}
                      label={word}
                      size="small"
                      variant="outlined"
                      color={saved.has(word) ? 'success' : 'default'}
                      icon={saved.has(word) ? <CheckIcon /> : undefined}
                      disabled={resolving.has(word)}
                      onClick={saved.has(word) ? undefined : () => handleAddWord(word)}
                      aria-label={
                        saved.has(word) ? `${word}（已加入單字庫）` : `加入單字庫：${word}`
                      }
                    />
                  ))}
                </Box>
              )}
            </Box>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>關閉</Button>
        <Button variant="contained" onClick={onStartNew}>
          開始新對話
        </Button>
      </DialogActions>
      <Snackbar open={toast !== null} autoHideDuration={3000} onClose={() => setToast(null)}>
        {toast ? (
          <Alert severity={toast.severity} onClose={() => setToast(null)}>
            {toast.message}
          </Alert>
        ) : undefined}
      </Snackbar>
    </Dialog>
  );
}

export default ConversationSummary;
