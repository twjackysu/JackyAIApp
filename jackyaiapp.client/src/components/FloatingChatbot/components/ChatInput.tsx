import SendIcon from '@mui/icons-material/Send';
import { IconButton, Paper, TextField } from '@mui/material';
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

  return (
    <Paper
      elevation={3}
      sx={{
        p: 2,
        display: 'flex',
        alignItems: 'center',
        borderRadius: 0,
        backgroundColor: theme.palette.background.paper,
      }}
    >
      <TextField
        fullWidth
        multiline
        maxRows={3}
        variant="outlined"
        placeholder="輸入訊息..."
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onKeyPress={onKeyPress}
        disabled={disabled}
        sx={{
          mr: 2,
          '& .MuiOutlinedInput-root': {
            borderRadius: 8,
          },
        }}
      />
      <IconButton color="primary" onClick={onSend} disabled={!value.trim() || disabled}>
        <SendIcon />
      </IconButton>
    </Paper>
  );
};

export default ChatInput;
