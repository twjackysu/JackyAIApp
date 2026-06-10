import SendIcon from '@mui/icons-material/Send';
import { Box, IconButton, TextField } from '@mui/material';
import { useTheme } from '@mui/material/styles';

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
        p: { xs: 1.5, sm: 2 },
        display: 'flex',
        alignItems: 'flex-end',
        gap: 1,
        borderTop: '1px solid',
        borderColor: 'divider',
        backgroundColor: theme.palette.background.paper,
      }}
    >
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
  );
};

export default ChatInput;
