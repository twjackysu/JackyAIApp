import { useGetTranslationTestQuery } from '@/apis/examApis';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';
import { TextField } from '@mui/material';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import FormLabel from '@mui/material/FormLabel';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

const CORRECT_TEXT = 'Correct!';
function TranslationCard() {
  const [input, setInput] = useState<string | null>(null);
  const { data, isFetching, refetch, isError, error } = useGetTranslationTestQuery();
  const [showAnswer, setShowAnswer] = useState<boolean>(false);
  const [feedback, setFeedback] = useState<string | null>(null);

  const handleTextFieldInputChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setInput(event.target.value);
  };
  const handleSubmit = () => {
    if (input === data?.data.english) {
      setFeedback(CORRECT_TEXT);
    } else {
      setFeedback('Incorrect, try again!');
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
        <TextField
          value={input || ''}
          onChange={handleTextFieldInputChange}
          disabled={showAnswer}
        />
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
            disabled={feedback === CORRECT_TEXT}
          >
            Submit
          </Button>
        </Box>
      )}
      {feedback && (
        <Typography variant="h6" sx={{ marginTop: 2 }}>
          {feedback}
        </Typography>
      )}
      {(showAnswer || feedback === CORRECT_TEXT) && (
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
