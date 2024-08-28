import Box from '@mui/material/Box';
import { useGetClozeTestQuery } from '@/apis/examApis';
import {
  Button,
  FormControl,
  FormControlLabel,
  FormLabel,
  LinearProgress,
  Radio,
  RadioGroup,
  Typography,
} from '@mui/material';
import { useState } from 'react';

const CORRECT_TEXT = 'Correct!';
function ClozeTestCard() {
  const [selectedOption, setSelectedOption] = useState<string | null>(null);
  const { data, isFetching, refetch } = useGetClozeTestQuery();
  const [feedback, setFeedback] = useState<string | null>(null);

  const handleOptionChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setSelectedOption(event.target.value);
  };
  const handleSubmit = () => {
    if (selectedOption === data?.data.answer) {
      setFeedback(CORRECT_TEXT);
    } else {
      setFeedback('Incorrect, try again!');
    }
  };
  const handleNext = () => {
    setSelectedOption(null);
    setFeedback(null);
    refetch();
  };
  if (isFetching) {
    return (
      <Box sx={{ width: '100%' }}>
        <LinearProgress />
        <Typography variant="h6">Generating AI response, please wait...</Typography>
      </Box>
    );
  }
  return (
    <Box sx={{ maxWidth: 600, margin: 'auto', padding: 2 }}>
      <Typography variant="h6" gutterBottom>
        {data?.data.question}
      </Typography>
      <FormControl component="fieldset">
        <FormLabel component="legend">Select an option:</FormLabel>
        <RadioGroup value={selectedOption} onChange={handleOptionChange}>
          {data?.data.options.map((option) => (
            <FormControlLabel key={option} value={option} control={<Radio />} label={option} />
          ))}
        </RadioGroup>
      </FormControl>
      <Box sx={{ marginTop: 2 }}>
        <Button variant="contained" color="primary" onClick={handleSubmit}>
          Submit
        </Button>
      </Box>
      {feedback && (
        <Typography variant="h6" sx={{ marginTop: 2 }}>
          {feedback}
        </Typography>
      )}
      {feedback === CORRECT_TEXT && (
        <Button variant="contained" color="primary" onClick={handleNext}>
          Next
        </Button>
      )}
    </Box>
  );
}

export default ClozeTestCard;
