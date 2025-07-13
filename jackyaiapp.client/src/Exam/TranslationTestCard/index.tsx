import {
  useGetTranslationTestQuery,
  useLazyGetTranslationQualityGradingQuery,
  useTranscribeAudioMutation,
} from '@/apis/examApis';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import MicIcon from '@mui/icons-material/Mic';
import { TextField } from '@mui/material';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import FormLabel from '@mui/material/FormLabel';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useTheme } from '@mui/material/styles';
import { useEffect, useState } from 'react';

function TranslationCard() {
  const theme = useTheme();
  const [input, setInput] = useState<string | null>(null);
  const { data, isFetching, refetch, isError, error } = useGetTranslationTestQuery();
  const [showAnswer, setShowAnswer] = useState<boolean>(false);
  const [feedback, setFeedback] = useState<string | null>(null);

  const [isRecording, setIsRecording] = useState(false);
  const [mediaRecorder, setMediaRecorder] = useState<MediaRecorder | null>(null);

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

  const [getTranslationQualityGrading, { isFetching: isTranslationQualityGradingFetching }] =
    useLazyGetTranslationQualityGradingQuery();
  const [transcribeAudio, { isLoading: isTranscribing }] = useTranscribeAudioMutation();

  const handleTextFieldInputChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setInput(event.target.value);
  };
  const handleSubmit = () => {
    if (input && data?.data.chinese && data?.data.word) {
      getTranslationQualityGrading({
        translation: input,
        examinationQuestion: data.data.chinese,
        unfamiliarWords: data.data.word,
      }).then((res) => {
        if (res.data) {
          setFeedback(res.data.data.translationQualityGrading);
        } else {
          setFeedback('Error in grading the translation');
        }
      });
    } else {
      setFeedback('Something went wrong, try again!');
    }
  };
  const handleNext = () => {
    setInput(null);
    setFeedback(null);
    setShowAnswer(false);
    refetch();
  };
  const handleShowAnswer = () => {
    setShowAnswer(true);
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
  if (isFetching) {
    return <AILoading />;
  }
  if (isError) {
    return <FetchBaseQueryErrorMessage error={error} />;
  }
  return (
    <Box sx={{ maxWidth: 600, margin: 'auto', padding: 2 }}>
      <Typography variant="h6" gutterBottom>
        {data?.data.chinese}
      </Typography>
      <FormControl component="fieldset">
        <FormLabel component="legend">輸入對應的英文句子:</FormLabel>
        <Stack direction="row" spacing={2} alignItems="center">
          <TextField
            value={input || ''}
            onChange={handleTextFieldInputChange}
            disabled={showAnswer}
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
              disabled={!mediaRecorder || isTranscribing}
            >
              <MicIcon />
            </IconButton>
          </Tooltip>
        </Stack>
        {isTranscribing && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            正在轉換語音為文字...
          </Typography>
        )}
      </FormControl>
      {showAnswer && (
        <Typography variant="h6" sx={{ marginTop: 2 }}>
          {data?.data.english}
        </Typography>
      )}
      {showAnswer || (
        <Box sx={{ marginTop: 2 }}>
          <Button
            variant="contained"
            color="primary"
            onClick={handleSubmit}
            disabled={isTranslationQualityGradingFetching || !!feedback || isTranscribing}
          >
            Submit
          </Button>
        </Box>
      )}
      <Typography variant="h6" sx={{ marginTop: 2, display: 'flex', alignItems: 'center' }}>
        翻譯評級
        <Tooltip
          title={
            <ul style={{ margin: 0, padding: '0 16px' }}>
              <li>A: 完美的翻譯，沒有任何語法、拼字或風格錯誤，並且完整保留了所有含義。</li>
              <li>B: 高品質翻譯，文法或拼字錯誤很少，句子大多流暢。</li>
              <li>C: 翻譯一般，有一些文法或拼字問題，但主要意義清晰。</li>
              <li>D: 翻譯品質低下，有遺漏或重大錯誤，以及影響理解的文法問題。</li>
              <li>E: 翻譯完全不正確，內容與原文不符或相矛盾，錯誤百出。</li>
            </ul>
          }
          arrow
        >
          <InfoOutlinedIcon sx={{ marginLeft: 1, cursor: 'pointer' }} />
        </Tooltip>
      </Typography>
      {feedback && (
        <Typography variant="h6" sx={{ marginTop: 2 }}>
          評級: {feedback}
        </Typography>
      )}
      {(showAnswer || !feedback) && (
        <Button variant="contained" color="primary" onClick={handleNext}>
          Next
        </Button>
      )}
      {showAnswer || (
        <Box sx={{ marginTop: 2 }}>
          <Button variant="outlined" onClick={handleShowAnswer} disabled={showAnswer}>
            Show Answer
          </Button>
        </Box>
      )}
    </Box>
  );
}

export default TranslationCard;
