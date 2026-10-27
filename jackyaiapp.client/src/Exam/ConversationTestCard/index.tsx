import { Alert, Box, Button, Typography, useMediaQuery, useTheme } from '@mui/material';
import { useEffect, useRef, useState } from 'react';

import ChatMessages from './components/ChatMessages';
import ConversationHeader from './components/ConversationHeader';
import ConversationSummary from './components/ConversationSummary';
import MessageInput from './components/MessageInput';
import ScenarioSelector from './components/ScenarioSelector';
import {
  SEND_FAILED_MESSAGE,
  SEND_FAILED_KEPT_NOTE,
  SEND_FAILED_RETRY_NOTE,
  START_FAILED_MESSAGE,
  START_FAILED_RETRY_NOTE,
  SUMMARY_FAILED_MESSAGE,
} from './conversationErrors';
import { useVoiceRecording, useAutoScroll } from './hooks';

import {
  useStartConversationTestMutation,
  useRespondToConversationMutation,
  useSummarizeConversationMutation,
} from '@/apis/examApis';
import { ConversationContext, ConversationTurn, DeliveryFeedback } from '@/apis/examApis/types';
import { getApiErrorMessage, getCaughtErrorMessage } from '@/apis/utils/errorMessage';
import AILoading from '@/components/AILoading';

interface ConversationState {
  context: ConversationContext;
  history: ConversationTurn[];
  isActive: boolean;
  difficultyLevel: number;
}

function ConversationTestCard() {
  const theme = useTheme();
  const isSmallScreen = useMediaQuery(theme.breakpoints.down('sm'));

  const [conversationState, setConversationState] = useState<ConversationState | null>(null);
  const [input, setInput] = useState<string>('');
  // What the box holds now, for a reply that fails after the reader has typed (or dictated) something new.
  const inputRef = useRef(input);
  useEffect(() => {
    inputRef.current = input;
  }, [input]);
  const [showSummary, setShowSummary] = useState(false);
  // Set by the most recent voice transcription, attached to the user turn it produces once sent,
  // then cleared — so it doesn't leak onto a later, unrelated turn.
  const [pendingDeliveryFeedback, setPendingDeliveryFeedback] = useState<DeliveryFeedback | null>(
    null,
  );
  // Why the last message got no reply. The message itself goes back into the box, so sending again
  // is one press; cleared by the next send or a new conversation.
  const [sendError, setSendError] = useState<string | null>(null);

  const [startConversation, { isLoading: isStarting, error: startError }] =
    useStartConversationTestMutation();
  const [respondToConversation, { isLoading: isResponding }] = useRespondToConversationMutation();
  const [
    summarizeConversation,
    { data: summaryData, isLoading: isSummarizing, error: summaryError, reset: resetSummary },
  ] = useSummarizeConversationMutation();

  // Custom hooks
  const messagesEndRef = useAutoScroll(conversationState?.history, isResponding);
  const { isRecording, isTranscribing, mediaRecorder, recordingNote, toggleRecording } =
    useVoiceRecording({
      onTranscriptionComplete: (text, deliveryFeedback) => {
        setInput(text);
        setPendingDeliveryFeedback(deliveryFeedback ?? null);
      },
    });

  const handleStartConversation = async (
    scenario: string,
    userRole: string,
    aiRole: string,
    difficultyLevel: number,
  ) => {
    try {
      const response = await startConversation({
        scenario,
        userRole,
        aiRole,
        difficultyLevel,
      }).unwrap();

      setConversationState({
        context: {
          scenario: response.data.scenario,
          userRole: response.data.userRole,
          aiRole: response.data.aiRole,
          turnNumber: 1,
        },
        history: [
          {
            speaker: 'ai',
            message: response.data.firstMessage,
          },
        ],
        isActive: true,
        difficultyLevel,
      });
    } catch {
      // Shown over the scenarios (startError), which stay there to pick again.
    }
  };

  const handleSendMessage = async () => {
    if (!input.trim() || !conversationState || isResponding) return;

    const userMessage = input.trim();
    const userTurnIndex = conversationState.history.length;
    const deliveryFeedback = pendingDeliveryFeedback ?? undefined;
    setInput('');
    setPendingDeliveryFeedback(null);
    setSendError(null);

    // First, immediately add user's message to the conversation
    setConversationState((prev) => ({
      ...prev!,
      context: {
        ...prev!.context,
        turnNumber: prev!.context.turnNumber + 1,
      },
      history: [...prev!.history, { speaker: 'user', message: userMessage, deliveryFeedback }],
    }));

    try {
      const response = await respondToConversation({
        conversationContext: conversationState.context,
        conversationHistory: conversationState.history,
        userMessage,
      }).unwrap();

      const { correction } = response.data;

      // Add AI's response and attach the correction (if any) to the user turn it
      // applies to, so it stays visible in the transcript instead of being
      // overwritten by the next turn's correction.
      setConversationState((prev) => {
        const history = [...prev!.history, { speaker: 'ai', message: response.data.aiResponse }];
        if (correction.hasCorrection) {
          history[userTurnIndex] = { ...history[userTurnIndex], correction };
        }
        return { ...prev!, history };
      });
    } catch (error) {
      // The reply never came, so the turn did not happen: take the message back out of the
      // transcript (the next request would otherwise send it as history with no answer after it)
      // and put it back in the box, with its delivery feedback, to send again.
      setConversationState((prev) => ({
        ...prev!,
        context: { ...prev!.context, turnNumber: prev!.context.turnNumber - 1 },
        history: prev!.history.slice(0, userTurnIndex),
      }));
      // What was typed meanwhile is kept, with its own delivery feedback; then the message is not put back.
      const restored = !inputRef.current.trim();
      if (restored) {
        setInput(userMessage);
        setPendingDeliveryFeedback(deliveryFeedback ?? null);
      }
      setSendError(
        `${getCaughtErrorMessage(error, SEND_FAILED_MESSAGE)} ${
          restored ? SEND_FAILED_RETRY_NOTE : SEND_FAILED_KEPT_NOTE
        }`,
      );
    }
  };

  const handleShowSummary = async () => {
    if (!conversationState) return;
    setShowSummary(true);
    try {
      await summarizeConversation({
        conversationContext: conversationState.context,
        conversationHistory: conversationState.history,
      }).unwrap();
    } catch {
      // Shown in the summary dialog (summaryError).
    }
  };

  const handleNewConversation = () => {
    setConversationState(null);
    setInput('');
    setPendingDeliveryFeedback(null);
    setSendError(null);
    setShowSummary(false);
    resetSummary();
  };

  const hasUserTurn = conversationState?.history.some((turn) => turn.speaker === 'user') ?? false;

  if (isStarting) {
    return <AILoading />;
  }

  if (!conversationState) {
    return (
      <>
        {startError && (
          <Alert severity="error" role="alert" sx={{ mb: 2, maxWidth: 800, mx: 'auto' }}>
            {`${getApiErrorMessage(startError, START_FAILED_MESSAGE)} ${START_FAILED_RETRY_NOTE}`}
          </Alert>
        )}
        <ScenarioSelector onSelectScenario={handleStartConversation} disabled={isStarting} />
      </>
    );
  }

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        height: isSmallScreen ? 'calc(100vh - 64px)' : '600px',
        maxWidth: 800,
        margin: 'auto',
        bgcolor: 'background.default',
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 1,
        overflow: 'hidden',
      }}
    >
      <ConversationHeader
        context={conversationState.context}
        difficultyLevel={conversationState.difficultyLevel}
      />

      <ChatMessages
        messages={conversationState.history}
        isResponding={isResponding}
        ref={messagesEndRef}
      />

      <MessageInput
        input={input}
        onInputChange={setInput}
        onSendMessage={handleSendMessage}
        onToggleRecording={toggleRecording}
        isRecording={isRecording}
        isTranscribing={isTranscribing}
        isResponding={isResponding}
        mediaRecorder={mediaRecorder}
        recordingNote={recordingNote}
        sendError={sendError}
      />

      <Box
        sx={{
          p: 2,
          display: 'flex',
          gap: 2,
          justifyContent: 'center',
          alignItems: 'center',
          flexWrap: 'wrap',
          borderTop: '1px solid',
          borderColor: 'divider',
        }}
      >
        {/* A disabled button can't be hovered or tapped, so the reason it's locked is printed
            in the caption under it instead of hiding in a tooltip. */}
        <Box>
          <Button
            variant="contained"
            onClick={handleShowSummary}
            disabled={isResponding || isTranscribing || !hasUserTurn}
            sx={{ borderRadius: 2 }}
          >
            結束並看總結
          </Button>
          {!hasUserTurn && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
              至少對話一輪後才能看總結
            </Typography>
          )}
        </Box>
        <Button
          variant="outlined"
          onClick={handleNewConversation}
          disabled={isResponding || isTranscribing}
          sx={{ borderRadius: 2 }}
        >
          開始新對話
        </Button>
      </Box>

      <ConversationSummary
        open={showSummary}
        summary={summaryData?.data ?? null}
        isLoading={isSummarizing}
        error={summaryError ? getApiErrorMessage(summaryError, SUMMARY_FAILED_MESSAGE) : null}
        onClose={() => setShowSummary(false)}
        onStartNew={handleNewConversation}
      />
    </Box>
  );
}

export default ConversationTestCard;
