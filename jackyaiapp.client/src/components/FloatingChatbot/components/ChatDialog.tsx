import { Dialog, useMediaQuery, useTheme } from '@mui/material';

import { StreamingStatus } from '@/hooks/useChatStreaming';

import { Message } from '../types';

import ChatHeader from './ChatHeader';
import ChatInput from './ChatInput';
import MessageArea from './MessageArea';
import ProgressIndicator from './ProgressIndicator';

interface ChatDialogProps {
  open: boolean;
  onClose: () => void;
  messages: Message[];
  inputValue: string;
  onInputChange: (value: string) => void;
  onSendMessage: () => void;
  onKeyPress: (e: React.KeyboardEvent) => void;
  streamingStatus: StreamingStatus;
  statusText: string;
  statusColor: string;
  messagesEndRef: React.RefObject<HTMLDivElement>;
}

const ChatDialog = ({
  open,
  onClose,
  messages,
  inputValue,
  onInputChange,
  onSendMessage,
  onKeyPress,
  streamingStatus,
  statusText,
  statusColor,
  messagesEndRef,
}: ChatDialogProps) => {
  const theme = useTheme();
  const isSmallScreen = useMediaQuery(theme.breakpoints.down('sm'));

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="sm"
      fullWidth
      PaperProps={{
        sx: {
          height: isSmallScreen ? '100vh' : '600px',
          display: 'flex',
          flexDirection: 'column',
          bgcolor: 'background.default',
        },
      }}
    >
      <ChatHeader statusText={statusText} statusColor={statusColor} onClose={onClose} />

      <ProgressIndicator streamingStatus={streamingStatus} />

      <MessageArea messages={messages} ref={messagesEndRef} />

      <ChatInput
        value={inputValue}
        onChange={onInputChange}
        onSend={onSendMessage}
        onKeyPress={onKeyPress}
        disabled={streamingStatus.isLoading}
      />
    </Dialog>
  );
};

export default ChatDialog;
