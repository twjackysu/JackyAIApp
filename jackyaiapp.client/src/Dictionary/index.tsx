import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useGetRepositoryWordsByWordIdQuery } from '../apis/repositoryApis';
import WordCard from '../components/WordCard';

import RecentlySearched, { RecentlySearchedRef } from './components/RecentlySearched';
import SpellSuggestions from './components/SpellSuggestions';
import WordOfTheDay from './components/WordOfTheDay';
import { wordPath } from './utils/wordPath';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGetWordQuery } from '@/apis/dictionaryApis';

function Dictionary() {
  const navigate = useNavigate();
  const { word: wordParam } = useParams<{ word?: string }>();
  const word = wordParam ?? null;

  // Legacy `?word=` query-string links (the format used before path-param routing)
  // redirect to the canonical path instead of silently no-oping.
  const [searchParams] = useSearchParams();
  const legacyWord = searchParams.get('word');
  useEffect(() => {
    if (!wordParam && legacyWord) {
      navigate(wordPath(legacyWord), { replace: true });
    }
  }, [wordParam, legacyWord, navigate]);

  const [text, setText] = useState<string>(word ?? '');
  // Keep the search box in sync when the word changes via navigation that didn't
  // originate from this input (recently-searched click, spell suggestion, back/forward).
  useEffect(() => {
    setText(word ?? '');
  }, [word]);

  const recentlySearchedRef = useRef<RecentlySearchedRef>(null);
  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter' && text.trim()) {
      const normalized = text.trim().toLowerCase();
      navigate(wordPath(normalized));
      recentlySearchedRef.current?.handleEnterKeyDown(e, normalized);
    }
  };

  const { data, isFetching, isError, error } = useGetWordQuery(word!, { skip: !word });
  const wordId = data?.data.id ?? '';

  const { data: userInfoRes } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;
  const { data: personalWordsData, isFetching: personalWordsIsFetching } =
    useGetRepositoryWordsByWordIdQuery(wordId, { skip: !wordId || !isLoggedIn });

  const handleClickRecentlySearchedText = (recentlySearchedText: string) => {
    navigate(wordPath(recentlySearchedText));
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
            isHideFavoriteButton={!isLoggedIn}
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
