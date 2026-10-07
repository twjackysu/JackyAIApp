import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useEffect, useRef, useState } from 'react';
import { Helmet } from 'react-helmet-async';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useGetRepositoryWordsByWordIdQuery } from '../apis/repositoryApis';
import WordCard from '../components/WordCard';

import RecentlySearched, { RecentlySearchedRef } from './components/RecentlySearched';
import SpellSuggestions from './components/SpellSuggestions';
import WordOfTheDay from './components/WordOfTheDay';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGetWordQuery } from '@/apis/dictionaryApis';

/** Must match `SitemapController.DictionaryPathPrefix` (backend TASK-001). */
const DICTIONARY_PATH = '/english/dictionary';

function Dictionary() {
  const navigate = useNavigate();
  const { word: wordParam } = useParams<{ word?: string }>();
  const [searchParams] = useSearchParams();
  const word = wordParam ?? null;

  // Back-compat for the old query-string URL (`?word=apple`): redirect to the canonical
  // path-param URL instead of dropping support for existing bookmarks/shares.
  const legacyWord = !word ? searchParams.get('word') : null;
  useEffect(() => {
    if (legacyWord) {
      navigate(`${DICTIONARY_PATH}/${encodeURIComponent(legacyWord)}`, { replace: true });
    }
  }, [legacyWord, navigate]);

  const [text, setText] = useState<string>(word ?? '');
  // Keep the search box in sync when the word changes via navigation (recently-searched
  // click, browser back/forward) — not on every keystroke, since this only depends on `word`.
  useEffect(() => {
    setText(word ?? '');
  }, [word]);

  // In-page word links (synonyms etc.) navigate without a reload, so land at the top of the
  // new word's page instead of wherever the previous one was scrolled.
  useEffect(() => {
    if (word) window.scrollTo(0, 0);
  }, [word]);

  const recentlySearchedRef = useRef<RecentlySearchedRef>(null);

  const navigateToWord = (searchText: string) => {
    if (!searchText) return;
    navigate(`${DICTIONARY_PATH}/${encodeURIComponent(searchText)}`);
  };
  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter' && text) {
      recentlySearchedRef.current?.handleEnterKeyDown(e, text.toLowerCase());
      navigateToWord(text);
    }
  };

  const { data, isFetching, isError, error } = useGetWordQuery(word!, { skip: !word });
  const wordId = data?.data.id ?? '';

  const { data: userInfo } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfo?.data;
  const { data: personalWordsData, isFetching: personalWordsIsFetching } =
    useGetRepositoryWordsByWordIdQuery(wordId, { skip: !wordId || !isLoggedIn });

  const handleClickRecentlySearchedText = (recentlySearchedText: string) => {
    navigateToWord(recentlySearchedText);
  };
  return (
    <Stack
      direction="column"
      sx={{
        justifyContent: word ? 'flex-start' : 'center',
        alignItems: 'center',
      }}
    >
      {data?.data && (
        <Helmet>
          <title>{data.data.seoTitle}</title>
          <meta name="description" content={data.data.seoDescription} />
          <link
            rel="canonical"
            href={`${window.location.origin}${DICTIONARY_PATH}/${encodeURIComponent(word ?? '')}`}
          />
        </Helmet>
      )}
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
            isHideFavoriteButton={!isLoggedIn}
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
