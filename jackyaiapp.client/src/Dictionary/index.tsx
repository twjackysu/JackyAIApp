import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useRef, useState } from 'react';

import { useGetWordQuery } from '@/apis/dictionaryApis';

import { useGetRepositoryWordsByWordIdQuery } from '../apis/repositoryApis';
import WordCard from '../components/WordCard';

import RecentlySearched, { RecentlySearchedRef } from './components/RecentlySearched';
import WordOfTheDay from './components/WordOfTheDay';

function Dictionary() {
  const [text, setText] = useState<string>('');
  const [word, setWord] = useState<string | null>(null);
  const recentlySearchedRef = useRef<RecentlySearchedRef>(null);
  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter') {
      setWord(text);
      recentlySearchedRef.current?.handleEnterKeyDown(e, text.toLowerCase());
    }
  };

  const { data, isFetching, isError, error } = useGetWordQuery(word!, { skip: !word });
  const wordId = data?.data.id ?? '';
  const { data: personalWordsData, isFetching: personalWordsIsFetching } =
    useGetRepositoryWordsByWordIdQuery(wordId, { skip: !wordId });

  const handleClickRecentlySearchedText = (recentlySearchedText: string) => {
    setWord(recentlySearchedText);
    setText(recentlySearchedText);
  };
  return (
    <Stack direction="column" justifyContent={word ? 'flex-start' : 'center'} alignItems="center">
      <TextField
        label="Search"
        variant="outlined"
        value={text}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={handleKeyDown}
        sx={{
          width: '50%',
          marginBottom: word ? '20px' : '0',
          transition: 'margin-bottom 0.3s ease',
        }}
      />
      {!word && (
        <RecentlySearched
          ref={recentlySearchedRef}
          onClickRecentlySearchedText={handleClickRecentlySearchedText}
        />
      )}
      {!word && <WordOfTheDay />}
      {word && (
        <Box width={data?.data.meanings.length == 1 ? '80%' : '100%'}>
          <WordCard
            word={data?.data}
            isFetching={isFetching || personalWordsIsFetching}
            isError={isError}
            isFavorite={!!personalWordsData?.data}
            error={error}
          />
        </Box>
      )}
    </Stack>
  );
}

export default Dictionary;
