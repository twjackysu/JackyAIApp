import MicIcon from '@mui/icons-material/Mic';
import SendIcon from '@mui/icons-material/Send';
import { IconButton, Paper, Stack, TextField, Tooltip, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

interface MessageInputProps {
  input: string;
  onInputChange: (value: string) => void;
  onSendMessage: () => void;
  onToggleRecording: () => void;
  isRecording: boolean;
  isTranscribing: boolean;
  isResponding: boolean;
  mediaRecorder: MediaRecorder | null;
}

function MessageInput({
  input,
  onInputChange,
  onSendMessage,
  onToggleRecording,
  isRecording,
  isTranscribing,
  isResponding,
  mediaRecorder,
}: MessageInputProps) {
  const theme = useTheme();

  const handleKeyPress = (event: React.KeyboardEvent) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      onSendMessage();
    }
  };

  return (
    <Paper
      elevation={3}
      sx={{
        p: 2,
        display: 'flex',
        flexDirection: 'column',
        borderRadius: 0,
        backgroundColor: theme.palette.background.paper,
      }}
    >
      <Stack direction="row" spacing={1} alignItems="center">
        <TextField
          fullWidth
          multiline
          maxRows={3}
          variant="outlined"
          value={input}
          onChange={(e) => onInputChange(e.target.value)}
          onKeyPress={handleKeyPress}
          placeholder="輸入你的回應..."
          disabled={isResponding}
          sx={{
            mr: 2,
            '& .MuiOutlinedInput-root': {
              borderRadius: 8,
            },
          }}
        />
        <Tooltip
          title={
            mediaRecorder
              ? isRecording
                ? 'Click to stop recording'
                : 'Click to start recording'
              : 'Microphone not available'
          }
          arrow
        >
          <IconButton
            onClick={onToggleRecording}
            sx={{
              color: isRecording
                ? theme.palette.error.main
                : isTranscribing
                  ? theme.palette.warning.main
                  : theme.palette.action.active,
            }}
            disabled={!mediaRecorder || isResponding || isTranscribing}
          >
            <MicIcon />
          </IconButton>
        </Tooltip>
        <IconButton
          color="primary"
          onClick={onSendMessage}
          disabled={!input.trim() || isResponding || isTranscribing}
        >
          <SendIcon />
        </IconButton>
      </Stack>
      {isTranscribing && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1, textAlign: 'center' }}>
          正在轉換語音為文字...
        </Typography>
      )}
    </Paper>
  );
}

export default MessageInput;
