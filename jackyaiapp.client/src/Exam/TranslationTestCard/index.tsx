import {
  useGetTranslationTestQuery,
  useLazyGetTranslationQualityGradingQuery,
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
import { green, grey } from '@mui/material/colors';
import { useEffect, useRef, useState } from 'react';

function TranslationCard() {
  const [input, setInput] = useState<string | null>(null);
  const { data, isFetching, refetch, isError, error } = useGetTranslationTestQuery();
  const [showAnswer, setShowAnswer] = useState<boolean>(false);
  const [feedback, setFeedback] = useState<string | null>(null);

  const [isSupported, setIsSupported] = useState(false);
  const [isRecording, setIsRecording] = useState(false);
  const recognitionRef = useRef<any>(null);

  useEffect(() => {
    // Check if your browser supports the Web Speech API
    if ('webkitSpeechRecognition' in window) {
      setIsSupported(true);
      const recognition = new (window.webkitSpeechRecognition as any)();
      recognition.lang = 'en-US'; // Set the language to English
      recognition.interimResults = false; // Whether to display intermediate results
      recognition.maxAlternatives = 1; // Number of results displayed

      recognition.onresult = (event: any) => {
        const result = event.results[0][0].transcript;
        setInput(result);
      };

      recognition.onerror = (event: any) => {
        console.error('語音辨識錯誤:', event.error);
      };
      recognition.onend = () => {
        setIsRecording(false);
      };
      recognitionRef.current = recognition;
    } else {
      setIsSupported(false);
    }
  }, []);

  const [getTranslationQualityGrading, { isFetching: isTranslationQualityGradingFetching }] =
    useLazyGetTranslationQualityGradingQuery();

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
    if (isRecording && recognitionRef.current) {
      recognitionRef.current.stop();
      setIsRecording(false);
    } else if (recognitionRef.current) {
      recognitionRef.current.start();
      setIsRecording(true);
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
              isSupported
                ? 'Click to start/stop recording'
                : 'Your browser does not support speech recognition.'
            }
            arrow
          >
            <IconButton
              onClick={handleToggleRecording}
              sx={{
                color: isRecording ? green[500] : grey[500],
              }}
              disabled={!isSupported}
            >
              <MicIcon />
            </IconButton>
          </Tooltip>
        </Stack>
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
            disabled={isTranslationQualityGradingFetching || !!feedback}
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
