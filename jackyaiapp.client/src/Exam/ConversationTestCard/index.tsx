import { Box, Button, Tooltip, Typography, useMediaQuery, useTheme } from '@mui/material';
import { useState } from 'react';

import ChatMessages from './components/ChatMessages';
import ConversationHeader from './components/ConversationHeader';
import ConversationSummary from './components/ConversationSummary';
import MessageInput from './components/MessageInput';
import ScenarioSelector from './components/ScenarioSelector';
import { useVoiceRecording, useAutoScroll } from './hooks';

import {
  useStartConversationTestMutation,
  useRespondToConversationMutation,
  useSummarizeConversationMutation,
} from '@/apis/examApis';
import { ConversationContext, ConversationTurn } from '@/apis/examApis/types';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';
import { CREDIT_COSTS } from '@/constants/creditCosts';

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
  const [showSummary, setShowSummary] = useState(false);

  const [startConversation, { isLoading: isStarting, error: startError }] =
    useStartConversationTestMutation();
  const [respondToConversation, { isLoading: isResponding }] = useRespondToConversationMutation();
  const [
    summarizeConversation,
    { data: summaryData, isLoading: isSummarizing, reset: resetSummary },
  ] = useSummarizeConversationMutation();

  // Custom hooks
  const messagesEndRef = useAutoScroll(conversationState?.history, isResponding);
  const { isRecording, isTranscribing, mediaRecorder, toggleRecording } = useVoiceRecording({
    onTranscriptionComplete: (text) => setInput(text),
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
    } catch (error) {
      console.error('Failed to start conversation:', error);
    }
  };

  const handleSendMessage = async () => {
    if (!input.trim() || !conversationState || isResponding) return;

    const userMessage = input.trim();
    const userTurnIndex = conversationState.history.length;
    setInput('');

    // First, immediately add user's message to the conversation
    setConversationState((prev) => ({
      ...prev!,
      context: {
        ...prev!.context,
        turnNumber: prev!.context.turnNumber + 1,
      },
      history: [...prev!.history, { speaker: 'user', message: userMessage }],
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
      console.error('Failed to send message:', error);
      // If error occurs, you might want to remove the user message or show an error
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
    } catch (error) {
      console.error('Failed to summarize conversation:', error);
    }
  };

  const handleNewConversation = () => {
    setConversationState(null);
    setInput('');
    setShowSummary(false);
    resetSummary();
  };

  const hasUserTurn = conversationState?.history.some((turn) => turn.speaker === 'user') ?? false;

  if (isStarting) {
    return <AILoading />;
  }

  if (startError) {
    return <FetchBaseQueryErrorMessage error={startError} />;
  }

  if (!conversationState) {
    return <ScenarioSelector onSelectScenario={handleStartConversation} disabled={isStarting} />;
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
        <Tooltip title={hasUserTurn ? '' : '至少對話一輪後才能看總結'}>
          <span>
            <Button
              variant="contained"
              onClick={handleShowSummary}
              disabled={isResponding || isTranscribing || !hasUserTurn}
              sx={{ borderRadius: 2 }}
            >
              結束並看總結
            </Button>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
              消耗 {CREDIT_COSTS.CONVERSATION_SUMMARY} credits
            </Typography>
          </span>
        </Tooltip>
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
        onClose={() => setShowSummary(false)}
        onStartNew={handleNewConversation}
      />
    </Box>
  );
}

export default ConversationTestCard;
