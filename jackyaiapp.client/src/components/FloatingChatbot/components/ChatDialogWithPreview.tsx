import { Dialog } from '@mui/material';
import { useState } from 'react';

import { Message } from '../types';

import ChatHeader from './ChatHeader';
import ChatInput from './ChatInput';
import ConversationDrawer from './ConversationDrawer';
import MessageAreaWithBlocks from './MessageAreaWithBlocks';
import PreviewDialog from './PreviewDialog';
import ProgressIndicator from './ProgressIndicator';

import { StreamingStatus } from '@/hooks/useChatStreaming';
import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

interface ChatDialogWithPreviewProps {
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
  messagesEndRef: React.RefObject<HTMLDivElement | null>;
  // Conversation history (drawer scoped to this dialog) + header controls.
  onOpenHistory: () => void;
  onNewChat: () => void;
  historyOpen: boolean;
  onCloseHistory: () => void;
  activeConversationId: string;
  onSelectConversation: (conversationId: string) => void;
  // Preview modal (HTML / Markdown) — opened on demand from a message button.
  onPreview: (block: ParsedBlock) => void;
  previewBlock: ParsedBlock | null;
  previewOpen: boolean;
  onClosePreview: () => void;
}

const ChatDialogWithPreview = ({
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
  onOpenHistory,
  onNewChat,
  historyOpen,
  onCloseHistory,
  activeConversationId,
  onSelectConversation,
  onPreview,
  previewBlock,
  previewOpen,
  onClosePreview,
}: ChatDialogWithPreviewProps) => {
  // The dialog paper hosts the history drawer so it slides over the chat window
  // (not the whole screen) and shares a single backdrop layer.
  const [paperEl, setPaperEl] = useState<HTMLElement | null>(null);

  return (
    <>
      <Dialog
        open={open}
        onClose={onClose}
        maxWidth={false}
        slotProps={{
          paper: {
            ref: setPaperEl,
            sx: {
              position: 'relative',
              width: { xs: '100vw', sm: 'min(840px, 95vw)' },
              height: { xs: '100dvh', sm: 'min(82vh, 760px)' },
              m: { xs: 0, sm: 2 },
              borderRadius: { xs: 0, sm: 3 },
              display: 'flex',
              flexDirection: 'column',
              overflow: 'hidden',
              bgcolor: 'background.default',
            },
          },
        }}
      >
        <ChatHeader
          statusText={statusText}
          statusColor={statusColor}
          onClose={onClose}
          onOpenHistory={onOpenHistory}
          onNewChat={onNewChat}
        />
        <ProgressIndicator streamingStatus={streamingStatus} />
        <MessageAreaWithBlocks
          messages={messages}
          ref={messagesEndRef}
          onPreview={onPreview}
          style={{ flex: 1, minHeight: 0 }}
        />
        <ChatInput
          value={inputValue}
          onChange={onInputChange}
          onSend={onSendMessage}
          onKeyPress={onKeyPress}
          disabled={streamingStatus.isLoading}
        />

        <ConversationDrawer
          open={historyOpen}
          onClose={onCloseHistory}
          activeConversationId={activeConversationId}
          onSelect={onSelectConversation}
          onNewChat={onNewChat}
          containerEl={paperEl}
        />
      </Dialog>

      <PreviewDialog open={previewOpen} onClose={onClosePreview} block={previewBlock} />
    </>
  );
};

export default ChatDialogWithPreview;
