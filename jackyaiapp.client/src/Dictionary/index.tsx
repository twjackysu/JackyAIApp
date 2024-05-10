import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import WordCard from './WordCard';

function Dictionary() {
  const [text, setText] = useState<string>('');
  const [word, setWord] = useState<string | null>(null);
  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter') {
      setWord(text);
    }
  };
  return (
    <Box>
      <Stack>
        <TextField
          label="Search"
          variant="outlined"
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={handleKeyDown}
        />
        <Typography variant="body1" gutterBottom>
          <WordCard word={word} />
        </Typography>
      </Stack>
    </Box>
  );
}

export default Dictionary;
