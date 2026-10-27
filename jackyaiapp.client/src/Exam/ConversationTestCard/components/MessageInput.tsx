import MicIcon from '@mui/icons-material/Mic';
import SendIcon from '@mui/icons-material/Send';
import { IconButton, Paper, Stack, TextField, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { SEND_FAILED_RETRY_NOTE } from '../conversationErrors';

import { ActionTooltip } from '@/components/hints';
import { START_RECORDING_LABEL } from '@/hooks/useVoiceRecording';

interface MessageInputProps {
  input: string;
  onInputChange: (value: string) => void;
  onSendMessage: () => void;
  onToggleRecording: () => void;
  isRecording: boolean;
  /** A line about the last recording (it stopped by itself, or was not turned into text). */
  recordingNote?: string | null;
  /** Why the last message got no reply (it is back in the box). */
  sendError?: string | null;
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
  recordingNote,
  sendError,
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
      <Stack
        direction="row"
        spacing={1}
        sx={{
          alignItems: 'center',
        }}
      >
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
        <ActionTooltip
          title={
            mediaRecorder
              ? isRecording
                ? '點擊停止錄音'
                : START_RECORDING_LABEL
              : '麥克風無法使用'
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
        </ActionTooltip>
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
      {recordingNote && !isTranscribing && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1, textAlign: 'center' }}>
          {recordingNote}
        </Typography>
      )}
      {sendError && !isResponding && (
        <Typography role="alert" variant="body2" color="error" sx={{ mt: 1, textAlign: 'center' }}>
          {sendError} {SEND_FAILED_RETRY_NOTE}
        </Typography>
      )}
    </Paper>
  );
}

export default MessageInput;
