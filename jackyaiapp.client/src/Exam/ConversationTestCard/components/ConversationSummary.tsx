import AutoFixHighIcon from '@mui/icons-material/AutoFixHigh';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutlined';
import StyleIcon from '@mui/icons-material/Style';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { ConversationSummaryResponse } from '@/apis/examApis/types';

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
                    <Chip key={word} label={word} size="small" variant="outlined" />
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
    </Dialog>
  );
}

export default ConversationSummary;
