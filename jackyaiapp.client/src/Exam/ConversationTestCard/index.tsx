import { Box, Button, useMediaQuery, useTheme } from '@mui/material';
import { useState } from 'react';

import {
  useStartConversationTestMutation,
  useRespondToConversationMutation,
} from '@/apis/examApis';
import {
  ConversationContext,
  ConversationTurn,
  ConversationCorrection,
} from '@/apis/examApis/types';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';

import ChatMessages from './components/ChatMessages';
import ConversationHeader from './components/ConversationHeader';
import CorrectionAlert from './components/CorrectionAlert';
import MessageInput from './components/MessageInput';
import ScenarioSelector from './components/ScenarioSelector';
import { useVoiceRecording, useAutoScroll } from './hooks';

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
  const [correction, setCorrection] = useState<ConversationCorrection | null>(null);

  const [startConversation, { isLoading: isStarting, error: startError }] =
    useStartConversationTestMutation();
  const [respondToConversation, { isLoading: isResponding }] = useRespondToConversationMutation();

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
      setCorrection(null);
    } catch (error) {
      console.error('Failed to start conversation:', error);
    }
  };

  const handleSendMessage = async () => {
    if (!input.trim() || !conversationState || isResponding) return;

    const userMessage = input.trim();
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

      // Then add AI's response
      setConversationState((prev) => ({
        ...prev!,
        history: [...prev!.history, { speaker: 'ai', message: response.data.aiResponse }],
      }));

      // Set correction if any
      setCorrection(response.data.correction.hasCorrection ? response.data.correction : null);
    } catch (error) {
      console.error('Failed to send message:', error);
      // If error occurs, you might want to remove the user message or show an error
    }
  };

  const handleNewConversation = () => {
    setConversationState(null);
    setInput('');
    setCorrection(null);
  };

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

      {correction && (
        <Box sx={{ px: 2 }}>
          <CorrectionAlert correction={correction} />
        </Box>
      )}

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

      <Box sx={{ p: 2, textAlign: 'center', borderTop: '1px solid', borderColor: 'divider' }}>
        <Button
          variant="outlined"
          onClick={handleNewConversation}
          disabled={isResponding || isTranscribing}
          sx={{ borderRadius: 2 }}
        >
          開始新對話
        </Button>
      </Box>
    </Box>
  );
}

export default ConversationTestCard;
