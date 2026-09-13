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
import { toWordPath } from './wordSlug';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGetWordQuery } from '@/apis/dictionaryApis';

function Dictionary() {
  const { word: wordParam } = useParams<{ word?: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const legacyWord = searchParams.get('word');
  const word = wordParam ?? null;
  const [text, setText] = useState<string>(wordParam ?? legacyWord ?? '');
  const recentlySearchedRef = useRef<RecentlySearchedRef>(null);

  // Old query-string links (`/english/dictionary?word=apple`) keep working via a redirect to
  // the canonical path-param URL instead of being silently dropped.
  useEffect(() => {
    if (!wordParam && legacyWord) {
      navigate(toWordPath(legacyWord), { replace: true });
    }
  }, [wordParam, legacyWord, navigate]);

  // Keep the search box in sync with the URL (e.g. browser back/forward between two words).
  useEffect(() => {
    if (wordParam) {
      setText(wordParam);
    }
  }, [wordParam]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter' && text.trim()) {
      recentlySearchedRef.current?.handleEnterKeyDown(e, text.trim().toLowerCase());
      navigate(toWordPath(text));
    }
  };

  const { data, isFetching, isError, error } = useGetWordQuery(word!, { skip: !word });
  const wordId = data?.data.id ?? '';
  const { data: userInfoRes } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;
  const { data: personalWordsData, isFetching: personalWordsIsFetching } =
    useGetRepositoryWordsByWordIdQuery(wordId, { skip: !wordId || !isLoggedIn });

  const handleClickRecentlySearchedText = (recentlySearchedText: string) => {
    setText(recentlySearchedText);
    navigate(toWordPath(recentlySearchedText));
  };

  const canonicalUrl = word ? `${window.location.origin}${toWordPath(word)}` : null;

  return (
    <Stack
      direction="column"
      sx={{
        justifyContent: word ? 'flex-start' : 'center',
        alignItems: 'center',
      }}
    >
      {canonicalUrl && (
        <Helmet>
          <link rel="canonical" href={canonicalUrl} />
          {/* seoTitle/seoDescription are formatted once, server-side (see TASK-001), and
              rendered verbatim here — no title/description text is composed in TS, so this
              page and the bot-preview middleware (TASK-003) can never drift apart. Absent
              until the backend ships those fields, in which case the default app title stays. */}
          {data?.data.seoTitle && <title>{data.data.seoTitle}</title>}
          {data?.data.seoDescription && (
            <meta name="description" content={data.data.seoDescription} />
          )}
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
