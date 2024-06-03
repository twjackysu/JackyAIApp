import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import WordCard from '../components/WordCard';
import { useGetWordQuery } from '@/apis/dictionaryApis';

function Dictionary() {
  const [text, setText] = useState<string>('');
  const [word, setWord] = useState<string | null>(null);
  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter') {
      setWord(text);
    }
  };

  const { data, isFetching, isError } = useGetWordQuery(word!, { skip: !word });
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
        <WordCard word={data?.data} isFetching={isFetching} isError={isError} />
      </Stack>
    </Box>
  );
}

export default Dictionary;
