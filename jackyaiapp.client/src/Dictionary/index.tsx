import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { useGetRepositoryWordsByWordIdQuery } from '../apis/repositoryApis';
import WordCard from '../components/WordCard';

import RecentlySearched, { RecentlySearchedRef } from './components/RecentlySearched';
import SpellSuggestions from './components/SpellSuggestions';
import WordOfTheDay from './components/WordOfTheDay';

import { useGetWordQuery } from '@/apis/dictionaryApis';

function Dictionary() {
  const [searchParams] = useSearchParams();
  const initialWord = searchParams.get('word') ?? '';
  const [text, setText] = useState<string>(initialWord);
  const [word, setWord] = useState<string | null>(initialWord || null);
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
    <Stack
      direction="column"
      sx={{
        justifyContent: word ? 'flex-start' : 'center',
        alignItems: 'center',
      }}
    >
      <TextField
        label="Search"
        variant="outlined"
        value={text}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={handleKeyDown}
        sx={{
          width: { xs: '100%', sm: '50%' },
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
        <Box
          sx={{
            width: { xs: '100%', sm: data?.data.meanings.length == 1 ? '80%' : '100%' },
          }}
        >
          <WordCard
            word={data?.data}
            isFetching={isFetching || personalWordsIsFetching}
            isError={isError}
            isFavorite={!!personalWordsData?.data}
            error={error}
          />
          {isError && !isFetching && (
            <SpellSuggestions word={word} onSelectSuggestion={handleClickRecentlySearchedText} />
          )}
        </Box>
      )}
    </Stack>
  );
}

export default Dictionary;
