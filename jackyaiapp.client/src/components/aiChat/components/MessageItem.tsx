import OpenInFullIcon from '@mui/icons-material/OpenInFull';
import ShortcutIcon from '@mui/icons-material/Shortcut';
import VisibilityIcon from '@mui/icons-material/Visibility';
import { Box, Button, Paper, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { memo } from 'react';

import ToolCards from '../cards/ToolCards';
import { ACCENT, accentWash, hairline, revealActionsOnHover } from '../style';
import { Message } from '../types';
import { ApprovalCall, ApprovalDecision } from '../utils/approval';
import { isThinking } from '../utils/thinking';

import ApprovalCard from './ApprovalCard';
import AssistantMark from './AssistantMark';
import AttachmentChip from './AttachmentChip';
import FollowUps from './FollowUps';
import MessageActions from './MessageActions';
import MessageEditor from './MessageEditor';
import ThinkingTraceView from './ThinkingTraceView';
import ToolActivity from './ToolActivity';

import { TRUNCATED_NOTE } from '@/apis/chatbotApis/chatErrors';
import { attachmentContentUrl } from '@/apis/chatbotApis/types';
import MarkdownMessage from '@/components/MarkdownMessage';
import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

/**
 * What a message does when the reader acts on it. MessageArea hands every message the same object for the
 * conversation's life, whatever the page passed this render, so a message that did not change is not drawn again.
 */
export interface MessageHandlers {
  onPreview: (block: ParsedBlock) => void;
  onStartEdit: (id: string) => void;
  onCancelEdit: () => void;
  onSubmitEdit: (message: Message, text: string) => void;
  onDecide: (pendingId: string, decisions: ApprovalDecision[]) => void;
  onAlwaysAllow: (calls: ApprovalCall[]) => void;
  onFollowUp: (question: string) => void;
}

export interface MessageItemProps {
  message: Message;
  handlers: MessageHandlers;
  /** An answer is being written: no editing. */
  busy: boolean;
  /** This is the answer still being written: nothing final to copy or read aloud yet. */
  answering: boolean;
  /** The reader is editing this message of theirs in place. */
  editing: boolean;
  /** The page lets the reader edit a message (and so resend it). */
  canEditMessages: boolean;
  /** The page lets the reader save a tool's write for good (the approval card's checkbox). */
  canAlwaysAllow: boolean;
  /** False while the panel is hidden: an answer then is not read aloud out of sight. */
  active: boolean;
  /** Questions to carry on with, under the last answer only; undefined under every other message. */
  followUps?: string[];
}

/**
 * Over a message another assistant handed over (D-099): the task came from Studio AI on another page, so the reader
 * sees why this conversation started by itself.
 */
function HandedOver({ by }: { by: string }) {
  return (
    <Typography
      variant="caption"
      color="text.secondary"
      data-testid="handed-over"
      sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5, mb: 0.5 }}
    >
      <ShortcutIcon sx={{ fontSize: 14, color: ACCENT }} aria-hidden />由 {by} 轉來
    </Typography>
  );
}

// 單個區塊渲染組件
const BlockRenderer = ({
  block,
  onPreview,
}: {
  block: ParsedBlock;
  onPreview: (block: ParsedBlock) => void;
}) => {
  const theme = useTheme();

  if (block.contentType === 'markdown') {
    return <MarkdownMessage content={block.content} isOwnMessage={false} theme={theme} />;
  }

  // HTML 區塊：顯示為「預覽」按鈕，點擊後另開寬闊的預覽視窗
  const streaming = !block.isComplete;
  return (
    <Box sx={{ my: 1 }}>
      <Button
        variant="outlined"
        size="small"
        startIcon={streaming ? <VisibilityIcon /> : <OpenInFullIcon />}
        onClick={() => onPreview(block)}
        sx={{ borderRadius: 2, borderColor: hairline(theme), color: 'text.primary' }}
      >
        {streaming ? 'HTML 產生中…點擊預覽' : '開啟 HTML 預覽'}
      </Button>
    </Box>
  );
};

/** The answer has not started: three dots in place of text. */
const ThinkingDots = () => (
  <Box
    aria-label="AI 助手思考中"
    sx={{
      display: 'flex',
      gap: 0.6,
      py: 1,
      '& span': {
        width: 6,
        height: 6,
        borderRadius: '50%',
        bgcolor: 'text.secondary',
        animation: 'assistantDot 1.2s infinite ease-in-out',
      },
      '& span:nth-of-type(2)': { animationDelay: '0.15s' },
      '& span:nth-of-type(3)': { animationDelay: '0.3s' },
      '@keyframes assistantDot': {
        '0%, 80%, 100%': { opacity: 0.25, transform: 'translateY(0)' },
        '40%': { opacity: 1, transform: 'translateY(-3px)' },
      },
    }}
  >
    <span />
    <span />
    <span />
  </Box>
);

/**
 * One message of the conversation: the reader's as a bubble on the right, an answer in the full column. Memoized:
 * while an answer streams (each piece of text or thinking a new `messages` array) and while the reader types in the
 * box, only the message that changed is drawn again, not every earlier answer's Markdown, tables and charts.
 */
function MessageItem({
  message,
  handlers,
  busy,
  answering,
  editing,
  canEditMessages,
  canAlwaysAllow,
  active,
  followUps,
}: MessageItemProps) {
  const theme = useTheme();

  const attachments = message.attachments && message.attachments.length > 0 && (
    <Box
      sx={{
        display: 'flex',
        flexWrap: 'wrap',
        gap: 0.5,
        justifyContent: message.isUser ? 'flex-end' : 'flex-start',
        mb: 0.75,
      }}
    >
      {message.attachments.map((a) => {
        const isFile = a.kind === 'image' || a.kind === 'pdf';
        return (
          <AttachmentChip
            key={a.id}
            fileName={a.fileName}
            kind={a.kind}
            truncated={a.truncated}
            pageCount={a.pageCount}
            thumbnailUrl={a.kind === 'image' ? attachmentContentUrl(a.id) : undefined}
            href={isFile ? attachmentContentUrl(a.id) : undefined}
          />
        );
      })}
    </Box>
  );

  // The reader's own message: a soft bubble on the right — or, being edited, a box in its place.
  if (message.isUser) {
    // Only a stored message (the server knows its id), and not while an answer is written.
    const canEdit = canEditMessages && Boolean(message.serverId) && !busy;
    return (
      <Box
        data-testid="user-message"
        sx={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'flex-end',
          ...revealActionsOnHover,
        }}
      >
        {message.handedOverBy && <HandedOver by={message.handedOverBy} />}
        {attachments}
        {editing ? (
          <MessageEditor
            initial={message.content}
            attachmentCount={message.attachments?.length ?? 0}
            onCancel={handlers.onCancelEdit}
            onSubmit={(text) => handlers.onSubmitEdit(message, text)}
          />
        ) : (
          <>
            <Paper
              elevation={0}
              sx={{
                px: 2,
                py: 1.25,
                maxWidth: '85%',
                borderRadius: '18px 18px 6px 18px',
                bgcolor: accentWash(theme, 0.13),
                border: `1px solid ${accentWash(theme, 0.22)}`,
                color: 'text.primary',
              }}
            >
              <MarkdownMessage content={message.content} isOwnMessage theme={theme} />
            </Paper>
            <MessageActions
              text={message.content}
              align="right"
              onEdit={canEdit ? () => handlers.onStartEdit(message.id) : undefined}
            />
          </>
        )}
      </Box>
    );
  }

  // A question still waiting for the reader comes after the text that leads up to it, where they are
  // reading, and no "thinking" dots go with it (the turn waits on them); once answered it is a record
  // of what ran, above the text with the tools' cards.
  const awaitingReader = message.approval != null && message.approval.state !== 'resolved';
  const approvalCard = message.approval && (
    // A new question in the same answer (a later round of the turn) is a new card, its choices its own.
    <ApprovalCard
      key={message.approval.request.pendingId}
      approval={message.approval.request}
      state={message.approval.state}
      decisions={message.approval.decisions}
      expired={message.approval.expired}
      onSubmit={(decisions) => handlers.onDecide(message.approval!.request.pendingId, decisions)}
      onAlwaysAllow={canAlwaysAllow ? handlers.onAlwaysAllow : undefined}
    />
  );

  // The assistant's answer: no bubble, the full column, its mark beside it.
  return (
    <Box
      data-testid="assistant-message"
      sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start', ...revealActionsOnHover }}
    >
      <Box sx={{ pt: 0.25 }}>
        <AssistantMark size={26} />
      </Box>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        {attachments}
        {message.thinking && <ThinkingTraceView thinking={message.thinking} />}
        {message.toolSteps && <ToolActivity steps={message.toolSteps} />}
        {message.toolSteps && <ToolCards steps={message.toolSteps} />}
        {!message.toolSteps && message.cards && <ToolCards cards={message.cards} />}
        {!awaitingReader && approvalCard}
        {!message.parsedBlocks?.length && message.content ? (
          <MarkdownMessage content={message.content} isOwnMessage={false} theme={theme} />
        ) : !message.parsedBlocks?.length ? (
          // Thinking shows itself (its line counts the seconds); the dots would say it twice.
          awaitingReader || isThinking(message.thinking) ? null : (
            <ThinkingDots />
          )
        ) : (
          <Box>
            {message.parsedBlocks!.map((block) => (
              <BlockRenderer key={block.id} block={block} onPreview={handlers.onPreview} />
            ))}
          </Box>
        )}
        {awaitingReader && approvalCard}
        {message.truncated && (
          <Typography variant="caption" sx={{ display: 'block', mt: 0.5, color: 'warning.main' }}>
            {TRUNCATED_NOTE}
          </Typography>
        )}
        {!answering && message.content && (
          <MessageActions
            text={message.content}
            align="left"
            readAloud
            // Only on screen: an answer done while the drawer is closed or the panel folded is not
            // read out of sight, with no stop button to press (D-120).
            autoReadKey={active && message.readAloud && message.answered ? message.id : undefined}
          />
        )}
        {followUps && <FollowUps texts={followUps} onPick={handlers.onFollowUp} />}
      </Box>
    </Box>
  );
}

export default memo(MessageItem);
