import SendIcon from '@mui/icons-material/Send';
import { Box, IconButton, TextField, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { CREDIT_COSTS } from '@/constants/creditCosts';

interface ChatInputProps {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  onKeyPress: (e: React.KeyboardEvent) => void;
  disabled: boolean;
}

const ChatInput = ({ value, onChange, onSend, onKeyPress, disabled }: ChatInputProps) => {
  const theme = useTheme();
  const canSend = Boolean(value.trim()) && !disabled;

  return (
    <Box
      sx={{
        px: { xs: 1.5, sm: 2 },
        pt: { xs: 1.5, sm: 2 },
        pb: 1,
        borderTop: '1px solid',
        borderColor: 'divider',
        backgroundColor: theme.palette.background.paper,
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 1 }}>
        <TextField
          fullWidth
          multiline
          maxRows={5}
          variant="outlined"
          placeholder="輸入訊息…（Enter 送出，Shift+Enter 換行）"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={onKeyPress}
          disabled={disabled}
          sx={{
            '& .MuiOutlinedInput-root': {
              borderRadius: 3,
              backgroundColor:
                theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.04)' : 'rgba(0,0,0,0.02)',
            },
          }}
        />
        <IconButton
          color="primary"
          onClick={onSend}
          disabled={!canSend}
          sx={{
            bgcolor: canSend ? 'primary.main' : 'action.disabledBackground',
            color: canSend ? 'primary.contrastText' : 'action.disabled',
            width: 44,
            height: 44,
            '&:hover': { bgcolor: 'primary.dark' },
            '&.Mui-disabled': { color: 'action.disabled' },
          }}
        >
          <SendIcon fontSize="small" />
        </IconButton>
      </Box>
      <Typography
        variant="caption"
        color="text.secondary"
        sx={{ display: 'block', textAlign: 'center', mt: 0.5 }}
      >
        每則對話消耗 {CREDIT_COSTS.CHAT_MESSAGE} credits
      </Typography>
    </Box>
  );
};

export default ChatInput;
