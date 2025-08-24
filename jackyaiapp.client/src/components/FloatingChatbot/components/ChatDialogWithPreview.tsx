import { Dialog, useMediaQuery, useTheme } from '@mui/material';
import { Allotment } from 'allotment';
import 'allotment/dist/style.css';
import { StreamingStatus } from '@/hooks/useChatStreaming';
import { Message } from '../types';
import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';
import HtmlViewer from '@/components/HtmlViewer';
import ChatHeader from './ChatHeader';
import ProgressIndicator from './ProgressIndicator';
import MessageArea from './MessageArea';
import MessageAreaWithBlocks from './MessageAreaWithBlocks';
import ChatInput from './ChatInput';

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
  messagesEndRef: React.RefObject<HTMLDivElement>;
  // 新增的 HTML 預覽相關 props
  currentHtmlBlock: ParsedBlock | null;
  showHtmlViewer: boolean;
  selectedHtmlContent: string;
  onHtmlPreview: (content: string) => void;
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
  currentHtmlBlock,
  showHtmlViewer,
  selectedHtmlContent,
  onHtmlPreview,
}: ChatDialogWithPreviewProps) => {
  const theme = useTheme();
  const isSmallScreen = useMediaQuery(theme.breakpoints.down('sm'));

  // 手機版不顯示分割面板，保持原有行為
  if (isSmallScreen) {
    return (
      <Dialog
        open={open}
        onClose={onClose}
        maxWidth="sm"
        fullWidth
        PaperProps={{
          sx: {
            height: '100vh',
            display: 'flex',
            flexDirection: 'column',
            bgcolor: 'background.default',
          },
        }}
      >
        <ChatHeader
          statusText={statusText}
          statusColor={statusColor}
          onClose={onClose}
        />

        <ProgressIndicator streamingStatus={streamingStatus} />

        <MessageArea 
          messages={messages} 
          ref={messagesEndRef} 
        />

        <ChatInput
          value={inputValue}
          onChange={onInputChange}
          onSend={onSendMessage}
          onKeyPress={onKeyPress}
          disabled={streamingStatus.isLoading}
        />
      </Dialog>
    );
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth={showHtmlViewer ? "xl" : "sm"}
      fullWidth
      PaperProps={{
        sx: {
          height: '600px',
          display: 'flex',
          flexDirection: 'column',
          bgcolor: 'background.default',
          // 當顯示 HTML 預覽時，調整寬度
          width: showHtmlViewer ? '90vw' : undefined,
        },
      }}
    >
      <ChatHeader
        statusText={statusText}
        statusColor={statusColor}
        onClose={onClose}
      />

      <ProgressIndicator streamingStatus={streamingStatus} />

      {/* 主要內容區域 */}
      <div style={{ flex: 1, display: 'flex', overflow: 'hidden' }}>
        {showHtmlViewer ? (
          <Allotment defaultSizes={[70, 30]}>
            {/* 左側：聊天區域 */}
            <Allotment.Pane minSize={300}>
              <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
                <MessageAreaWithBlocks 
                  messages={messages} 
                  ref={messagesEndRef}
                  onHtmlPreview={onHtmlPreview}
                  style={{ flex: 1 }}
                />
                <ChatInput
                  value={inputValue}
                  onChange={onInputChange}
                  onSend={onSendMessage}
                  onKeyPress={onKeyPress}
                  disabled={streamingStatus.isLoading}
                />
              </div>
            </Allotment.Pane>

            {/* 右側：HTML 預覽 */}
            <Allotment.Pane minSize={250}>
              <HtmlViewer
                content={selectedHtmlContent}
                isComplete={!currentHtmlBlock || currentHtmlBlock.isComplete}
                title="HTML Preview"
              />
            </Allotment.Pane>
          </Allotment>
        ) : (
          // 沒有 HTML 預覽時的原有布局
          <div style={{ width: '100%', display: 'flex', flexDirection: 'column' }}>
            <MessageAreaWithBlocks 
              messages={messages} 
              ref={messagesEndRef}
              onHtmlPreview={onHtmlPreview}
              style={{ flex: 1 }}
            />
            <ChatInput
              value={inputValue}
              onChange={onInputChange}
              onSend={onSendMessage}
              onKeyPress={onKeyPress}
              disabled={streamingStatus.isLoading}
            />
          </div>
        )}
      </div>
    </Dialog>
  );
};

export default ChatDialogWithPreview;