import Box from '@mui/material/Box';
import { alpha, useTheme } from '@mui/material/styles';
import {
  type ReactNode,
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from 'react';

import { AppDraftContext } from './cards/mcpApp/appDraft';
import ChatHeader from './components/ChatHeader';
import ChatInput from './components/ChatInput';
import ConversationDrawer from './components/ConversationDrawer';
import ConversationList from './components/ConversationList';
import { HISTORY_COLUMN_BREAKPOINT, HISTORY_COLUMN_WIDTH } from './components/historyColumn';
import MessageArea, { type MessageReveal } from './components/MessageArea';
import PreviewDialog from './components/PreviewDialog';
import { useAiChatConfig } from './config';
import { useAgentStatus } from './hooks/useAgentStatus';
import { useChatAnnouncer } from './hooks/useChatAnnouncer';
import { useChatConversation } from './hooks/useChatConversation';
import { useKeepPasteFormatting } from './hooks/useKeepPasteFormatting';
import { usePendingAttachments } from './hooks/usePendingAttachments';
import { usePreview } from './hooks/usePreview';
import { stopReading } from './hooks/useSpeechOutput';
import { useWaitingTitle } from './hooks/useWaitingTitle';
import { useWarnBeforeLeaving } from './hooks/useWarnBeforeLeaving';
import { ACCENT, ACCENT_GRADIENT, hairline, panelSurface, visuallyHidden } from './style';
import { hasOpenApproval } from './utils/approval';
import { insertPastedText, readPaste } from './utils/paste';
import { stillSpoken } from './utils/spokenDraft';

import type { ChatAsk } from './hooks/useAskQueue';

import { messageTooLongNote } from '@/apis/chatbotApis/chatErrors';

/** What the folded one-line box shows about a conversation it is hiding. */
export interface AssistantActivity {
  /** A reply is being written. */
  busy: boolean;
  /** There is a conversation on screen (it is kept while folded). */
  hasConversation: boolean;
  /** The last answer on screen finished as an answer (`Message.answered`): not stopped, not failed. */
  answered?: boolean;
}

interface AiAssistantPanelProps {
  /** Whether the panel is open. It stays mounted while folded so a reply that is still streaming,
   *  and the conversation itself, survive folding it away. */
  active: boolean;
  /** The message being typed, shared with the one-line box so typing carries over either way. */
  inputValue: string;
  onInputChange: (value: string) => void;
  /** The box holds text the reader selected on a page (Studio AI's 問 AI on a selection): what they send from it
   *  goes with `quotesPageText`, so the server counts it as outside text. */
  inputQuotesPageText?: boolean;
  /** Fold the panel back into its one-line box. */
  onCollapse: () => void;
  onActivityChange?: (activity: AssistantActivity) => void;
  /** `inline` (a block of the page, the default) or `sheet`: it fills a drawer of its own (Studio AI),
   *  with no history column beside it — the history opens over it, as on a phone. */
  variant?: 'inline' | 'sheet';
  /** A message to send as if the reader had typed it (Studio AI's `askStudio`): each id is sent once,
   *  `fresh` as the first message of a new conversation (`SendOptions.fresh`), `quotesPageText` for text the
   *  reader selected on a page (`SendOptions.quotesPageText`). */
  ask?: ChatAsk;
  /** The `ask` with this id was sent: its owner drops it (`useAskQueue`), so a panel mounted again does not send it
   *  twice, and hands the next one. */
  onAskSent?: (id: number) => void;
  /** An answer takes the reader to a page of the site (Studio AI's `open_page` with `go`), with a question for that
   *  page's own assistant when it hands a task over (`utils/handoff`); without it, only its link. */
  onNavigate?: (path: string, handoff?: string) => void;
  /** One of the profile's own tools finished (Studio AI reads again what a write changed). */
  onToolDone?: (tool: string) => void;
  /** Over the message box, from the app that holds the panel (Studio AI's quick actions for what is typed). */
  inputAccessory?: ReactNode;
  /** What the reader sends from the box (Enter or the send button), offered to the panel's owner first: true when the
   *  owner took it (a `/` draft sends the first ready-made request listed, D-115), and then the box is only emptied.
   *  Not offered while files are picked: the message, files and all, goes as typed. */
  onSendDraft?: (text: string) => boolean;
  /** An MCP App in an answer asked to put this text in the box (`ui/message`) and the reader agreed (D-204): the owner adds
   *  it to the draft as text that is not the reader's (`inputQuotesPageText`). Without it, apps are told the host does not
   *  take messages. */
  onAppText?: (text: string) => void;
}

/**
 * An app's AI assistant as a block of its page (the chatbot used to float over it). What it is
 * about — its greeting, examples, the server's profile — comes from the `AiChatConfig` above it
 * (Finance's: Claude with the TW-stock MCP server attached). The caller is responsible for only
 * rendering it for a signed-in user.
 */
function AiAssistantPanel({
  active,
  inputValue,
  onInputChange: setInputValue,
  inputQuotesPageText = false,
  onCollapse,
  onActivityChange,
  variant = 'inline',
  ask,
  onAskSent,
  onNavigate,
  onToolDone,
  inputAccessory,
  onSendDraft,
  onAppText,
}: AiAssistantPanelProps) {
  const sheet = variant === 'sheet';
  const config = useAiChatConfig();
  const conversation = useChatConversation({
    errorMessages: config.errorMessages,
    profile: config.profile,
    pageContext: config.pageContext,
    toolLabels: config.toolLabels,
    onNavigate,
    onToolDone,
  });
  const { messages, conversationId, busy } = conversation;
  // A confirmation card waits: leaving the page would end its turn (D-105), and a reader on another tab
  // should see that the assistant needs them (D-107).
  const awaitingReader = hasOpenApproval(messages);
  useWarnBeforeLeaving(awaitingReader);
  useWaitingTitle(awaitingReader);
  const preview = usePreview(conversation.blocks, conversation.currentHtmlBlock);
  const attachments = usePendingAttachments();
  const [keepFormatting, setKeepFormatting] = useKeepPasteFormatting();
  const { statusText } = useAgentStatus(conversation.streamingStatus);
  // Said to a screen reader: an answer started, ended, or a write waits for the reader (D-181).
  const announcement = useChatAnnouncer(messages, busy);
  // An app's ui/message, once the reader agreed under it (D-204). Kept the same function for the panel's life, so the
  // messages (memo'd, D-138) are not drawn again: the context's value changes only if an owner starts or stops taking them.
  const appTextRef = useRef(onAppText);
  useLayoutEffect(() => {
    appTextRef.current = onAppText;
  });
  const takesAppText = onAppText !== undefined;
  const appDraft = useMemo(
    () => (takesAppText ? (text: string) => appTextRef.current?.(text) : null),
    [takesAppText],
  );

  // Folded away (or the drawer closed): an answer being read stops with it, as one leaving the screen does.
  const wasActive = useRef(active);
  useEffect(() => {
    if (wasActive.current && !active) stopReading();
    wasActive.current = active;
  }, [active]);

  // Some of the draft was spoken (the box's microphone): its answer is read aloud once done (D-120), while the
  // draft still holds what was heard (`stillSpoken`: a fixed word keeps it, deleting it all and typing does not).
  // Forgotten once the box is emptied — sent, or cleared by the reader.
  const [heardWords, setHeardWords] = useState('');
  useEffect(() => {
    if (!inputValue.trim()) setHeardWords('');
  }, [inputValue]);
  const theme = useTheme();

  // The history is a fixed column beside the chat on wide screens. Where that would leave the chat
  // too narrow, it is a drawer that slides over this panel (not over the page) instead.
  const [historyOpen, setHistoryOpen] = useState(false);
  const [panelEl, setPanelEl] = useState<HTMLElement | null>(null);

  // Opening the panel is the reader asking to type: the cursor goes to the end of the message
  // they started in the one-line box. Also once a reply is done, since the box is disabled (and
  // so loses focus) while it is written.
  const inputRef = useRef<HTMLTextAreaElement>(null);
  useEffect(() => {
    const input = inputRef.current;
    if (!active || busy || !input) return;
    input.focus();
    input.setSelectionRange(input.value.length, input.value.length);
  }, [active, busy]);

  // A page asked (askStudio): send it once, into the conversation on screen or a new one (queued
  // behind a reply still being written, as a typed message would be).
  const { send } = conversation;
  const { close: closePreview } = preview;
  const sentAsk = useRef<number | undefined>(undefined);
  useEffect(() => {
    if (!ask || sentAsk.current === ask.id) return;
    sentAsk.current = ask.id;
    const options = {
      ...(ask.quotesPageText && { quotesPageText: true }),
      ...(ask.handedOverBy && { handedOverBy: ask.handedOverBy }),
      ...(ask.readAloud && { readAloud: true }),
    };
    if (ask.fresh) {
      // A preview of the old conversation's answer has nothing to do with the new one.
      closePreview();
      send(ask.text, [], { fresh: true, ...options });
    } else {
      send(ask.text, [], options);
    }
    onAskSent?.(ask.id);
  }, [ask, send, closePreview, onAskSent]);

  const hasConversation = messages.length > 0;
  const answered = messages[messages.length - 1]?.answered === true;
  useEffect(() => {
    onActivityChange?.({ busy, hasConversation, answered });
  }, [busy, hasConversation, answered, onActivityChange]);

  const handlePaste = (e: React.ClipboardEvent) => {
    const result = readPaste(e.clipboardData, keepFormatting);
    if (result.kind === 'default') return;
    e.preventDefault();
    if (result.kind === 'files') {
      attachments.add(result.files);
      return;
    }
    // Put the converted text where the browser would have put the pasted text.
    const input = inputRef.current;
    const { value, caret } = insertPastedText(
      inputValue,
      input?.selectionStart ?? inputValue.length,
      input?.selectionEnd ?? inputValue.length,
      result.text,
    );
    setInputValue(value);
    requestAnimationFrame(() => inputRef.current?.setSelectionRange(caret, caret));
  };

  // Switching conversation, or starting over, closes a preview of the old one.
  const { newChat, selectConversation } = conversation;
  const handleNewChat = useCallback(() => {
    if (newChat()) closePreview();
  }, [newChat, closePreview]);
  // Opened from a search's find, the conversation shows the message it was found in (D-193); a new object each time,
  // so opening the same find again scrolls again.
  const [reveal, setReveal] = useState<MessageReveal | null>(null);
  const handleSelectConversation = useCallback(
    async (id: string, messageId?: number) => {
      if (!(await selectConversation(id))) return;
      closePreview();
      setReveal(messageId === undefined ? null : { messageId });
    },
    [selectConversation, closePreview],
  );

  const handleSendMessage = () => {
    if (!inputValue.trim()) return;
    if (attachments.uploading) return;
    // The server refuses it (ChatbotController.MaxMessageLength); the box says so and keeps the text.
    if (messageTooLongNote(inputValue)) return;
    const text = inputValue;
    // With files picked, the message is the reader's own: a ready-made request would leave them behind.
    if (attachments.pending.length === 0 && onSendDraft?.(text)) {
      setInputValue('');
      return;
    }
    const sent = attachments.takeForMessage();
    setInputValue('');
    conversation.send(text, sent, {
      ...(inputQuotesPageText && { quotesPageText: true }),
      // Asked by voice, answered by voice (D-120): the reader is likely not looking at the screen.
      ...(stillSpoken(text, heardWords) && { readAloud: true }),
    });
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSendMessage();
    }
  };

  return (
    <AppDraftContext.Provider value={appDraft}>
      <Box
        ref={setPanelEl}
        component="section"
        aria-label="AI 助手"
        // Escape in the message box folds an inline panel, as its header's 收合 does (the drawer closes on its own).
        onKeyDown={(e: React.KeyboardEvent) => {
          if (e.key !== 'Escape' || sheet || e.defaultPrevented || e.target !== inputRef.current)
            return;
          e.preventDefault();
          onCollapse();
        }}
        sx={{
          // `relative` + `overflow: hidden` keep the history drawer inside the panel.
          position: 'relative',
          display: 'flex',
          overflow: 'hidden',
          height: sheet ? '100%' : 'clamp(460px, 74dvh, 780px)',
          borderRadius: sheet ? 0 : '20px',
          // Its own surface, outlined in the accent gradient with a faint glow from the top
          // corners, so it does not read as one more of the market-data cards below it.
          border: '1px solid transparent',
          background: [
            `radial-gradient(520px circle at 0% 0%, ${alpha(ACCENT, 0.07)}, transparent 60%) padding-box`,
            `radial-gradient(520px circle at 100% 0%, ${alpha('#818cf8', 0.06)}, transparent 60%) padding-box`,
            `linear-gradient(${panelSurface(theme)}, ${panelSurface(theme)}) padding-box`,
            `${ACCENT_GRADIENT} border-box`,
          ].join(', '),
          boxShadow: '0 30px 80px -48px rgba(52, 211, 153, 0.55)',
        }}
      >
        {/* Conversation history, always in view where there is room for it */}
        <Box
          sx={{
            display: sheet ? 'none' : { xs: 'none', [HISTORY_COLUMN_BREAKPOINT]: 'block' },
            width: HISTORY_COLUMN_WIDTH,
            flexShrink: 0,
            borderRight: `1px solid ${hairline(theme)}`,
            bgcolor: theme.palette.mode === 'dark' ? 'rgba(0,0,0,0.22)' : 'rgba(0,0,0,0.02)',
          }}
        >
          <ConversationList
            activeConversationId={conversationId}
            onSelect={handleSelectConversation}
            onNewChat={handleNewChat}
            busy={busy}
          />
        </Box>

        <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
          <ChatHeader
            statusText={statusText}
            busy={busy}
            onOpenHistory={() => setHistoryOpen(true)}
            onNewChat={handleNewChat}
            onCollapse={onCollapse}
            sheet={sheet}
            messages={messages}
            conversationId={conversationId}
          />
          <MessageArea
            messages={messages}
            onPreview={preview.show}
            active={active}
            onSuggest={(question) => {
              setInputValue(question);
              inputRef.current?.focus();
            }}
            busy={busy}
            onEdit={conversation.edit}
            onDecide={(pendingId, decisions) => void conversation.decide(pendingId, decisions)}
            onAlwaysAllow={conversation.allowAlways}
            onFollowUp={(question) => conversation.send(question, [])}
            onRetry={conversation.retry}
            reveal={reveal}
            conversationId={conversationId}
          />
          {inputAccessory}
          <ChatInput
            value={inputValue}
            onChange={setInputValue}
            onSend={handleSendMessage}
            onKeyPress={handleKeyPress}
            busy={busy}
            onStop={conversation.stop}
            queued={conversation.queued}
            onRemoveQueued={conversation.removeQueued}
            inputRef={inputRef}
            attachments={attachments.pending}
            onAddFiles={attachments.add}
            onRemoveAttachment={attachments.remove}
            onPaste={handlePaste}
            keepFormatting={keepFormatting}
            onKeepFormattingChange={setKeepFormatting}
            onSpoken={setHeardWords}
          />
        </Box>

        <Box role="status" aria-live="polite" aria-atomic="true" sx={visuallyHidden}>
          {announcement}
        </Box>

        <ConversationDrawer
          open={historyOpen}
          onClose={() => setHistoryOpen(false)}
          activeConversationId={conversationId}
          onSelect={handleSelectConversation}
          onNewChat={handleNewChat}
          busy={busy}
          containerEl={panelEl}
        />
      </Box>

      <PreviewDialog open={preview.open} onClose={preview.close} block={preview.block} />
    </AppDraftContext.Provider>
  );
}

export default AiAssistantPanel;
