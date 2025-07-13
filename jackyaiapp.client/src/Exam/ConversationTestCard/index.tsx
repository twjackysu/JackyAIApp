import {
  useStartConversationTestMutation,
  useRespondToConversationMutation,
  useTranscribeAudioMutation,
} from '@/apis/examApis';
import {
  ConversationContext,
  ConversationTurn,
  ConversationCorrection,
} from '@/apis/examApis/types';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';
import MicIcon from '@mui/icons-material/Mic';
import SendIcon from '@mui/icons-material/Send';
import {
  Alert,
  Avatar,
  Chip,
  IconButton,
  Paper,
  TextField,
  Tooltip,
} from '@mui/material';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useTheme } from '@mui/material/styles';
import { useEffect, useRef, useState } from 'react';

interface ConversationState {
  context: ConversationContext;
  history: ConversationTurn[];
  isActive: boolean;
}

function ConversationTestCard() {
  const theme = useTheme();
  const [conversationState, setConversationState] = useState<ConversationState | null>(null);
  const [input, setInput] = useState<string>('');
  const [correction, setCorrection] = useState<ConversationCorrection | null>(null);
  
  const [isRecording, setIsRecording] = useState(false);
  const [mediaRecorder, setMediaRecorder] = useState<MediaRecorder | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  
  const [startConversation, { isLoading: isStarting, error: startError }] = useStartConversationTestMutation();
  const [respondToConversation, { isLoading: isResponding }] = useRespondToConversationMutation();
  const [transcribeAudio, { isLoading: isTranscribing }] = useTranscribeAudioMutation();

  useEffect(() => {
    // Setup MediaRecorder for audio recording
    const setupMediaRecorder = async () => {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        const recorder = new MediaRecorder(stream);
        
        const chunks: Blob[] = [];
        
        recorder.ondataavailable = (event) => {
          if (event.data.size > 0) {
            chunks.push(event.data);
          }
        };

        recorder.onstop = async () => {
          if (chunks.length > 0) {
            const audioBlob = new Blob(chunks, { type: 'audio/wav' });
            await handleAudioTranscription(audioBlob);
            chunks.length = 0; // Clear chunks
          }
        };

        setMediaRecorder(recorder);
      } catch (error) {
        console.error('Error accessing microphone:', error);
      }
    };

    setupMediaRecorder();
  }, []);

  // Auto scroll to bottom when conversation history changes
  useEffect(() => {
    const scrollToBottom = () => {
      messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    };
    
    if (conversationState?.history) {
      // Small delay to ensure DOM is updated
      setTimeout(scrollToBottom, 100);
    }
  }, [conversationState?.history, isResponding]);

  const handleAudioTranscription = async (audioBlob: Blob) => {
    try {
      const formData = new FormData();
      formData.append('audioFile', audioBlob, 'recording.wav');
      
      const response = await transcribeAudio(formData).unwrap();
      if (response.data.text) {
        setInput(response.data.text);
      }
    } catch (error) {
      console.error('Error transcribing audio:', error);
    }
  };

  const handleStartConversation = async () => {
    try {
      const response = await startConversation({
        userVocabularyWords: [], // Backend will fetch user's words
        difficultyLevel: 3,
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
    setConversationState(prev => ({
      ...prev!,
      context: {
        ...prev!.context,
        turnNumber: prev!.context.turnNumber + 1,
      },
      history: [
        ...prev!.history,
        { speaker: 'user', message: userMessage },
      ],
    }));

    try {
      const response = await respondToConversation({
        conversationContext: conversationState.context,
        conversationHistory: conversationState.history,
        userMessage,
      }).unwrap();

      // Then add AI's response
      setConversationState(prev => ({
        ...prev!,
        history: [
          ...prev!.history,
          { speaker: 'ai', message: response.data.aiResponse },
        ],
      }));

      // Set correction if any
      setCorrection(response.data.correction.hasCorrection ? response.data.correction : null);
    } catch (error) {
      console.error('Failed to send message:', error);
      // If error occurs, you might want to remove the user message or show an error
    }
  };

  const handleToggleRecording = () => {
    if (!mediaRecorder) return;
    
    if (isRecording) {
      // Stop recording and transcribe
      mediaRecorder.stop();
      setIsRecording(false);
    } else {
      // Start recording
      if (mediaRecorder.state === 'inactive') {
        mediaRecorder.start();
        setIsRecording(true);
      }
    }
  };

  const handleNewConversation = () => {
    setConversationState(null);
    setInput('');
    setCorrection(null);
  };

  const handleKeyPress = (event: React.KeyboardEvent) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      handleSendMessage();
    }
  };

  if (isStarting) {
    return <AILoading />;
  }

  if (startError) {
    return <FetchBaseQueryErrorMessage error={startError} />;
  }

  if (!conversationState) {
    return (
      <Box sx={{ maxWidth: 600, margin: 'auto', padding: 2, textAlign: 'center' }}>
        <Typography variant="h5" gutterBottom>
          情境對話測驗
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
          透過實際對話場景練習英文，AI會根據你的單字庫生成合適的情境，並在對話中提供即時的語法建議。
        </Typography>
        <Button
          variant="contained"
          color="primary"
          size="large"
          onClick={handleStartConversation}
        >
          開始對話
        </Button>
      </Box>
    );
  }

  return (
    <Box sx={{ maxWidth: 700, margin: 'auto', padding: 2 }}>
      {/* Scenario Info */}
      <Paper sx={{ 
        p: 2, 
        mb: 2, 
        bgcolor: theme.palette.mode === 'dark' ? 'rgba(144, 202, 249, 0.08)' : 'rgba(144, 202, 249, 0.12)'
      }}>
        <Typography variant="h6" gutterBottom>
          📍 {conversationState.context.scenario}
        </Typography>
        <Stack direction="row" spacing={1}>
          <Chip
            label={`你的角色: ${conversationState.context.userRole}`}
            variant="outlined"
            color="primary"
          />
          <Chip
            label={`AI角色: ${conversationState.context.aiRole}`}
            variant="outlined"
            color="secondary"
          />
        </Stack>
      </Paper>

      {/* Chat Messages */}
      <Paper sx={{ p: 2, mb: 2, maxHeight: 400, overflowY: 'auto' }}>
        <Stack spacing={2}>
          {conversationState.history.map((turn, index) => (
            <Box
              key={index}
              sx={{
                display: 'flex',
                justifyContent: turn.speaker === 'user' ? 'flex-end' : 'flex-start',
              }}
            >
              <Box
                sx={{
                  display: 'flex',
                  flexDirection: turn.speaker === 'user' ? 'row-reverse' : 'row',
                  alignItems: 'flex-start',
                  gap: 1,
                  maxWidth: '70%',
                }}
              >
                <Avatar
                  sx={{
                    bgcolor: turn.speaker === 'user' 
                      ? theme.palette.primary.main 
                      : theme.palette.success.main,
                    width: 32,
                    height: 32,
                  }}
                >
                  {turn.speaker === 'user' ? 'U' : 'AI'}
                </Avatar>
                <Paper
                  sx={{
                    p: 1.5,
                    bgcolor: turn.speaker === 'user' 
                      ? theme.palette.mode === 'dark' 
                        ? 'rgba(25, 118, 210, 0.12)' 
                        : 'rgba(25, 118, 210, 0.08)'
                      : theme.palette.mode === 'dark' 
                        ? 'rgba(46, 125, 50, 0.12)' 
                        : 'rgba(46, 125, 50, 0.08)',
                  }}
                >
                  <Typography variant="body1">{turn.message}</Typography>
                </Paper>
              </Box>
            </Box>
          ))}
          {isResponding && (
            <Box sx={{ display: 'flex', justifyContent: 'flex-start' }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Avatar sx={{ bgcolor: theme.palette.success.main, width: 32, height: 32 }}>
                  AI
                </Avatar>
                <Typography variant="body2" color="text.secondary">
                  正在輸入...
                </Typography>
              </Box>
            </Box>
          )}
          {/* Scroll anchor */}
          <div ref={messagesEndRef} />
        </Stack>
      </Paper>

      {/* Correction Alert */}
      {correction && correction.hasCorrection && (
        <Alert severity="info" sx={{ mb: 2 }}>
          <Typography variant="body2">
            💡 <strong>建議修正:</strong> "{correction.originalText}" → "{correction.suggestedText}"
          </Typography>
          <Typography variant="body2" sx={{ mt: 0.5 }}>
            {correction.explanation}
          </Typography>
        </Alert>
      )}

      {/* Input Section */}
      <Paper sx={{ p: 2 }}>
        <Stack direction="row" spacing={1} alignItems="flex-end">
          <TextField
            fullWidth
            multiline
            maxRows={3}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyPress={handleKeyPress}
            placeholder="輸入你的回應..."
            disabled={isResponding}
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
              onClick={handleToggleRecording}
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
            onClick={handleSendMessage}
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

      {/* Action Buttons */}
      <Box sx={{ mt: 2, textAlign: 'center' }}>
        <Button
          variant="outlined"
          onClick={handleNewConversation}
          disabled={isResponding || isTranscribing}
        >
          開始新對話
        </Button>
      </Box>
    </Box>
  );
}

export default ConversationTestCard;